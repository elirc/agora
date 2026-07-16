using Agora.Api.Carts;
using Agora.Api.Mapping;
using Agora.Application.Carts;
using Agora.Contracts.V1;

namespace Agora.Api.Endpoints;

public static class CartEndpoints
{
    // 🔍 THE FIRST ENDPOINTS IN AGORA WITH NO AUTHORIZATION AT ALL — no RequireUser, no RequireRole,
    // no 401 and no 403 on any of the five. Scroll the file and check; then notice that the
    // authorization matrix has a row for every one of them and every persona gets a 2xx.
    //
    // That is not a gap, it is a different axis. A cart is not gated by WHO you are — a shopper with
    // no account has one, and so does the Ops user, because they are also a person who might buy a
    // skillet. It is scoped by WHICH cart you can name, and you can only ever name one: the server
    // resolves it from your session, or from your cart cookie, and there is no route, no request body
    // and no id anywhere in this file that lets you ask for a different one.
    //
    // ⚠️ so the deny case still exists, and it is in the matrix: a cart-line id belonging to someone
    // else is a 404 — for everybody, signed in or not. That is the only "no" these endpoints can say,
    // and it is enforced by the LOOKUP rather than by a guard someone could forget to write. (S02's
    // "ownership as a policy, not an if-statement", finally cashed in. See CartLookup.)
    public static RouteGroupBuilder MapCart(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/cart").WithTags("Cart");

        group.MapGet("/", async (HttpContext http, GetCart useCase, CancellationToken ct) =>
        {
            PricedCart cart = await useCase.ExecuteAsync(CartCookie.Read(http.Request), ct);

            return Results.Ok(cart.ToDto());
        })
        // Note the single response type: no 404, ever. Every shopper has a cart; an empty one is a
        // cart. And note that this route does NOT mint a cookie — a GET that Set-Cookies is a GET
        // with a side effect, and the side effect is a row. Every crawler, link preview and uptime
        // check that ever touched the storefront would leave one behind, and then "abandoned carts"
        // is a metric about robots.
        .Produces<CartDto>();

        group.MapPost("/lines", async (
            AddCartLineRequest request,
            HttpContext http,
            AddCartLine useCase,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            CartMutation result = await useCase.ExecuteAsync(
                request.ProductId, request.Quantity, CartCookie.Read(http.Request), ct);

            // The first WRITE is what mints an anonymous cart's token — the first thing that needs
            // the browser to remember something. The use case says "a new cart needed a token, here
            // it is"; deciding that a token reaches a browser as an HttpOnly Set-Cookie is this
            // layer's job and nobody else's.
            if (result.IssuedToken is { } token)
            {
                CartCookie.Issue(http.Response, token, clock.GetUtcNow());
            }

            return Results.Ok(result.Cart.ToDto());
        })
        // 📘 200, NOT 201, AND THIS ONE IS WORTH ARGUING ABOUT because 201 is the reflex. Two reasons,
        // and the second is the real one:
        //   1. There is nothing to point a Location header at. A line is not a resource — there is no
        //      GET /cart/lines/{id}, and there never will be, because the only readable thing here is
        //      the cart.
        //   2. Half the time it CREATES NOTHING. Add a skillet you already have and no line is born;
        //      an existing one goes from 2 to 3. A 201 would be a lie on exactly those requests, and
        //      the client cannot tell which kind it made. (The event has the same problem and makes
        //      the same call: cart.product_added, not cart.line_added.)
        // The body is the whole cart, resolved, so the drawer never has to ask twice or add up
        // anything itself.
        .Produces<CartDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPatch("/lines/{id:guid}", async (
            Guid id,
            UpdateCartLineQuantityRequest request,
            HttpContext http,
            UpdateCartLineQuantity useCase,
            CancellationToken ct) =>
        {
            CartMutation result = await useCase.ExecuteAsync(
                id, request.Quantity, CartCookie.Read(http.Request), ct);

            return Results.Ok(result.Cart.ToDto());
        })
        // WHY PATCH and not PUT: the body carries the one field that is changing, not the whole line.
        // A PUT would be a promise that the body is the complete new representation — so a client
        // omitting `addedAtUtc` would be asking us to erase it, and we would have to either honour
        // that or admit the PUT was a PATCH wearing a hat.
        .Produces<CartDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/lines/{id:guid}", async (
            Guid id,
            HttpContext http,
            RemoveCartLine useCase,
            CancellationToken ct) =>
        {
            CartMutation result = await useCase.ExecuteAsync(id, CartCookie.Read(http.Request), ct);

            return Results.Ok(result.Cart.ToDto());
        })
        // ⚠️ 200 WITH THE CART, NOT 204. 204 is the conventional answer and it is the wrong one here:
        // removing a line changes the SUBTOTAL, so a 204 leaves the drawer holding a number that is
        // now false. Its two options are then a second round trip to learn what it already could have
        // been told, or subtracting the line total in JavaScript — which is money math in a language
        // with no decimal type, i.e. the exact bug this course keeps circling. Convention is a good
        // default and a bad master; the response IS the authoritative cart.
        .Produces<CartDto>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/", async (HttpContext http, ClearCart useCase, CancellationToken ct) =>
        {
            CartMutation result = await useCase.ExecuteAsync(CartCookie.Read(http.Request), ct);

            return Results.Ok(result.Cart.ToDto());
        })
        // No 404 here either: DELETE is idempotent, so emptying an empty cart succeeds. The
        // postcondition the caller asked for — "the cart is empty" — holds.
        .Produces<CartDto>();

        return api;
    }
}
