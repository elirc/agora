using Agora.Api.Carts;
using Agora.Application.Carts;
using Agora.Application.Inventory;
using Agora.Contracts.V1;
using Agora.Domain.Inventory;

namespace Agora.Api.Endpoints;

/// <summary>
/// Stock: what a shopper may have (public), and what the shop's team needs to know about what is
/// running out (StoreAdmin and Ops).
/// </summary>
public static class InventoryEndpoints
{
    public static RouteGroupBuilder MapInventory(this RouteGroupBuilder api)
    {
        // 🔍 THE ROUTE SAYS WHOSE QUESTION IT IS. Availability hangs off the product
        // (`/products/{id}/stock`) because "how many skillets can I have" is a fact about a skillet.
        // The low-stock list hangs off /admin because "what is running out" is a fact about the SHOP —
        // same table underneath, two different audiences, two different shapes, two different
        // authorization answers. A single `/stock?lowStockOnly=true` would have collapsed all of that
        // into a query parameter and made the authz rule depend on the value of a flag.
        api.MapGet("/products/{id:guid}/stock", async (
            Guid id,
            StockAvailabilityQuery query,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            IReadOnlyDictionary<Guid, int> available = await query.AvailableAsync([id], ct);
            int count = available[id];

            return Results.Ok(new StockDto(count, count > 0, clock.GetUtcNow()));
        })
        .WithSummary("How many of a product a shopper could claim right now.")
        .WithTags("Catalog")
        .Produces<StockDto>();

        // ⚠️ ANONYMOUS, LIKE THE CART IT SERVES. A shopper without an account can hold a cart (S05),
        // so a shopper without an account must be able to hold the stock that cart is for — otherwise
        // the last skillet is reservable only by people who already signed up, which is exactly
        // backwards. The claim is tied to the CART (which may be a token), never to a user.
        api.MapPost("/cart/reservations", async (
            ReserveStockRequest request,
            HttpContext http,
            CartLookup cartLookup,
            ReserveStock useCase,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            // The cart must exist before it can hold anything — and OpenAsync mints one (and its
            // cookie) if this is the shopper's first write, exactly as adding a line does.
            OpenedCart opened = await cartLookup.OpenAsync(CartCookie.Read(http.Request), ct);

            if (opened.IssuedToken is not null)
            {
                CartCookie.Issue(http.Response, opened.IssuedToken, clock.GetUtcNow());
            }

            ReservationResult result = await useCase.ExecuteAsync(
                request.ProductId,
                opened.Cart.Id,
                request.Quantity,
                ct);

            return Results.Ok(new ReservationResultDto(
                result.Success,
                result.Reservation is null ? null : ToDto(result.Reservation),
                result.Available));
        })
        .WithSummary("Hold stock for the caller's cart while they check out.")
        .WithTags("Cart")
        .Produces<ReservationResultDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapDelete("/cart/reservations", async (
            HttpContext http,
            CartLookup cartLookup,
            ReleaseCartReservations useCase,
            CancellationToken ct) =>
        {
            OpenedCart opened = await cartLookup.OpenAsync(CartCookie.Read(http.Request), ct);

            await useCase.ExecuteAsync(opened.Cart.Id, ct);

            return Results.NoContent();
        })
        .WithSummary("Give back everything the caller's cart is holding.")
        .WithTags("Cart")
        .Produces(StatusCodes.Status204NoContent);

        RouteGroupBuilder admin = api.MapGroup("/admin").WithTags("Admin inventory");

        admin.MapGet("/stock/low", async (
            int? threshold,
            ListLowStock useCase,
            CancellationToken ct) =>
        {
            IReadOnlyList<LowStockLine> lines =
                await useCase.ExecuteAsync(threshold ?? ListLowStock.DefaultThreshold, ct);

            // 🔍 MAPPED, not returned. LowStockLine is an Application type and it is one field away
            // from being identical to LowStockDto — which is exactly when the temptation to skip this
            // line is strongest. Return it directly and the OpenAPI contract, and therefore the
            // React app, is now shaped by a type that exists to serve a use case: the next person to
            // add an internal field to it ships it to every client, and the drift test dutifully
            // blesses the leak. Contracts change when we decide the CONTRACT changes.
            return Results.Ok(lines
                .Select(line => new LowStockDto(
                    line.ProductId,
                    line.Name,
                    line.Sku,
                    line.OnHand,
                    line.Reserved,
                    line.Available))
                .ToList());
        })
        .WithSummary("Products at or below a stock threshold — the shop's own view.")
        .Produces<IEnumerable<LowStockDto>>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        return api;
    }

    private static ReservationDto ToDto(Reservation reservation) =>
        new(reservation.Id, reservation.ProductId, reservation.Quantity, reservation.Window.Ends);
}
