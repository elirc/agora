using Agora.Api.Auth;
using Agora.Api.Carts;
using Agora.Application.Abstractions;
using Agora.Application.Auth;
using Agora.Application.Carts;
using Agora.Contracts.V1;
using Agora.Domain.Access;

namespace Agora.Api.Endpoints;

public static class AuthEndpoints
{
    // 🔍 like the catalog endpoints, this file reads as a table of contents: route → use case →
    // cookie → contract. The only Api-specific work here is issuing/clearing cookies; the credential
    // logic all lives in the use cases (Agora.Application.Auth).
    public static RouteGroupBuilder MapAuth(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/register", async (
            RegisterRequest request,
            HttpContext http,
            RegisterCustomer useCase,
            MergeCarts mergeCarts,
            CancellationToken ct) =>
        {
            CustomerRegistered result = await useCase.ExecuteAsync(request.Email, request.Password, ct);

            // The token leaves the server exactly once, in this Set-Cookie header — never in the body.
            SessionCookie.Issue(http.Response, result.Token, result.ExpiresAtUtc);

            // ⚠️ REGISTER MERGES TOO, and it is not a copy-paste of the login path — it is the more
            // important of the two. "Create an account to check out" is the oldest funnel in retail,
            // so signing UP is if anything MORE likely to happen with a full cart than signing in. A
            // register path that quietly dropped the cart would lose it at the highest-intent moment
            // of the shopper's entire visit — which is also the moment they are least likely to
            // rebuild it and most likely to leave.
            await AdoptCartAsync(http, mergeCarts, result.User.Id, ct);

            return Results.Created("/api/v1/auth/me", ToMeDto(result.User));
        })
        .Produces<MeDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/login", async (
            LoginRequest request,
            HttpContext http,
            Login useCase,
            MergeCarts mergeCarts,
            CancellationToken ct) =>
        {
            LoggedIn result = await useCase.ExecuteAsync(request.Email, request.Password, ct);

            SessionCookie.Issue(http.Response, result.Token, result.ExpiresAtUtc);

            await AdoptCartAsync(http, mergeCarts, result.User.Id, ct);

            return Results.Ok(ToMeDto(result.User));
        })
        .Produces<MeDto>()
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", async (
            HttpContext http,
            ICurrentUser currentUser,
            Logout useCase,
            CancellationToken ct) =>
        {
            // WHY require a user to log out: logout acts on YOUR session, so an anonymous caller
            // has nothing to act on and gets a 401 — the same uniform authz stance every mutating
            // endpoint takes. (This guard is S02's RED→GREEN lesson: it was missing at first, the
            // authorization-matrix test went red on the anonymous row, and this line is what turned
            // it green. A hidden button is not a boundary; the server re-checks.)
            currentUser.RequireUser();

            if (http.Request.Cookies.TryGetValue(SessionCookie.Name, out string? token)
                && !string.IsNullOrEmpty(token))
            {
                await useCase.ExecuteAsync(token, ct);
            }

            SessionCookie.Clear(http.Response);

            // ⚠️ AND LOGOUT MINTS NOTHING — no cart cookie comes back. Signing out does not hand you
            // the anonymous cart you arrived with: that cart was consumed at login and its items are
            // in your account, where they belong. Re-anonymising on the way out would mean copying a
            // signed-in customer's cart into an unprivileged token, in the browser of a machine they
            // are deliberately walking away from. That is the shared-laptop bug, written on purpose.
            // You logged out; your cart is still yours; sign in and it is there.
            return Results.NoContent();
        })
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/me", async (GetCurrentUser useCase, CancellationToken ct) =>
        {
            User me = await useCase.ExecuteAsync(ct);
            return Results.Ok(ToMeDto(me));
        })
        .Produces<MeDto>()
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        return api;
    }

    /// <summary>The cart follows the shopper across the identity boundary. Both sign-in paths do it.</summary>
    // 📘 THE SPRINT'S CROSSING, AND WHY IT LIVES HERE RATHER THAN INSIDE Login. Two use cases run for
    // one HTTP request, and an endpoint composing them is exactly what a composition point is for —
    // but the alternative deserves an argument, because it is the tidier-looking one.
    //
    // `Login` could take a cart token and merge inside its own transaction: one commit, atomic, no
    // window. What that costs is that Login — which answers exactly one question, "are these
    // credentials good, and here is a session" — starts knowing about carts. And then the next thing
    // that should happen at sign-in (a welcome-back email, a loyalty check, a fraud signal) is also a
    // parameter of Login, and within a quarter Login is a sign-in PIPELINE that no longer answers a
    // question. Shopping already depends on identity; identity must not learn to depend on shopping.
    // Arrows that point both ways are how a modular monolith stops being modular.
    //
    // ⚠️ THE PRICE, STATED PLAINLY: this is TWO transactions. The session commits, then the merge
    // commits. If the merge throws, the shopper gets a 500, their anonymous cart is un-merged, and
    // the cart cookie is NOT cleared — we never reach that line. They retry the login and it works,
    // because MergeCarts is idempotent: retrying is safe rather than hopeful. That is not a
    // coincidence, it is the payoff for choosing `max` over `sum` as the merge rule, and it is why a
    // rule's algebra is worth arguing about before its ergonomics. The window is real and is written
    // down in ADR-0007 §Consequences rather than papered over here.
    private static async Task AdoptCartAsync(
        HttpContext http,
        MergeCarts mergeCarts,
        Guid userId,
        CancellationToken cancellationToken)
    {
        // No `if` — MergeCarts answers "no token, nothing to do" itself, because "there was no cart"
        // is a fact about carts and belongs in a use case, not in an endpoint's control flow.
        await mergeCarts.ExecuteAsync(userId, CartCookie.Read(http.Request), cancellationToken);

        // Unconditionally: after this response there is no state in which keeping an anonymous cart
        // token is right. See CartCookie.Clear.
        CartCookie.Clear(http.Response);
    }

    // WHERE the value objects unwrap: Email → string, Role enum → its name, HERE at the edge, so
    // the contract stays primitives-only (same stance as catalog's DtoMapping).
    private static MeDto ToMeDto(User user) =>
        new(user.Id, user.Email.Value, user.Role.ToString());
}
