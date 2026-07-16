using Agora.Api.Auth;
using Agora.Application.Abstractions;
using Agora.Application.Auth;
using Agora.Contracts.V1;
using Agora.Domain.Access;

namespace Agora.Api.Endpoints;

public static class AuthEndpoints
{
    // 🔍 like the catalog endpoints, this file reads as a table of contents: route → use case →
    // cookie → contract. The only Api-specific work here is issuing/clearing the session cookie;
    // the credential logic all lives in the use cases (Agora.Application.Auth).
    public static RouteGroupBuilder MapAuth(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/register", async (
            RegisterRequest request,
            HttpContext http,
            RegisterCustomer useCase,
            CancellationToken ct) =>
        {
            CustomerRegistered result = await useCase.ExecuteAsync(request.Email, request.Password, ct);

            // The token leaves the server exactly once, in this Set-Cookie header — never in the body.
            SessionCookie.Issue(http.Response, result.Token, result.ExpiresAtUtc);

            return Results.Created("/api/v1/auth/me", ToMeDto(result.User));
        })
        .Produces<MeDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/login", async (
            LoginRequest request,
            HttpContext http,
            Login useCase,
            CancellationToken ct) =>
        {
            LoggedIn result = await useCase.ExecuteAsync(request.Email, request.Password, ct);

            SessionCookie.Issue(http.Response, result.Token, result.ExpiresAtUtc);

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
            if (http.Request.Cookies.TryGetValue(SessionCookie.Name, out string? token)
                && !string.IsNullOrEmpty(token))
            {
                await useCase.ExecuteAsync(token, ct);
            }

            SessionCookie.Clear(http.Response);
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

    // WHERE the value objects unwrap: Email → string, Role enum → its name, HERE at the edge, so
    // the contract stays primitives-only (same stance as catalog's DtoMapping).
    private static MeDto ToMeDto(User user) =>
        new(user.Id, user.Email.Value, user.Role.ToString());
}
