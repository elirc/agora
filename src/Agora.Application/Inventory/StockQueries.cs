using Agora.Application.Abstractions;
using Agora.Domain.Inventory;

namespace Agora.Application.Inventory;

/// <summary>What the storefront needs to know about stock, as of one instant.</summary>
// 🔍 THE SAME SHAPE AS ProductPricing (S04), deliberately: one clock read per request, handed to a
// pure domain function, so that every product on a page answers about the same moment. A grid that
// read the clock per product would render its top half at 11:59 and its bottom half at 12:00 — and
// the one day a year that matters, it would be the day a sale started.
public sealed class StockAvailabilityQuery(IInventoryRepository inventory, TimeProvider clock)
{
    /// <summary>Availability for a page of products.</summary>
    public async Task<IReadOnlyDictionary<Guid, int>> AvailableAsync(
        IReadOnlyList<Guid> productIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        if (productIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        DateTimeOffset now = clock.GetUtcNow();

        IReadOnlyDictionary<Guid, StockSnapshot> snapshots =
            await inventory.SnapshotManyAsync(productIds, cancellationToken);

        // Products with no stock row read as zero rather than absent. A product nobody has ever
        // stocked and a product that sold out are the same sentence to a shopper — "you can't have
        // this" — and making the caller distinguish `null` from `0` invites exactly one bug: the day
        // someone writes `stock[id] > 0` against a dictionary that doesn't contain the key.
        return productIds.ToDictionary(
            id => id,
            id => snapshots.TryGetValue(id, out StockSnapshot? snapshot) ? snapshot.AvailableAt(now) : 0);
    }
}

/// <summary>What the shop's own team needs: what is about to run out.</summary>
public sealed class ListLowStock(
    IInventoryRepository inventory,
    IProductRepository products,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    /// <summary>The default line in the sand for "nearly out".</summary>
    // An arbitrary number, and the ADR says so honestly rather than inventing a justification. It is
    // a default for a query parameter, not a business rule — the admin can ask for any threshold, and
    // the shop will discover its own number by using the thing.
    public const int DefaultThreshold = 3;

    public async Task<IReadOnlyList<LowStockLine>> ExecuteAsync(
        int threshold,
        CancellationToken cancellationToken)
    {
        currentUser.RequireRole(Domain.Access.Role.StoreAdmin, Domain.Access.Role.Ops);

        if (threshold < 0)
        {
            throw new Errors.RequestValidationException("Threshold cannot be negative.");
        }

        DateTimeOffset now = clock.GetUtcNow();

        IReadOnlyList<StockSnapshot> snapshots =
            await inventory.ListLowStockAsync(threshold, now, cancellationToken);

        if (snapshots.Count == 0)
        {
            return [];
        }

        // ONE query for every name, not one per row. 🔗 Exactly the N+1 S14 hunts — and the reason
        // this view needs names at all is that "AGA-KIT-002 is nearly out" is not a sentence anybody
        // can act on without looking something up.
        IReadOnlyList<Domain.Catalog.Product> named = await products.ListByIdsAsync(
            [.. snapshots.Select(snapshot => snapshot.Stock.ProductId)],
            Catalog.ProductStatusFilter.Active,
            cancellationToken);

        Dictionary<Guid, Domain.Catalog.Product> byId = named.ToDictionary(product => product.Id);

        // 📘 THE ADMIN SEES BOTH NUMBERS, and that is the whole design of this view. "2 available" is
        // an emergency if there are two on the shelf and a non-event if there are nine with seven in
        // people's carts — same figure, opposite actions. A low-stock report that only shows
        // availability sends someone to the stockroom to count skillets that are already there.
        // 📘 ARCHIVED PRODUCTS DROP OUT HERE, AND THAT IS THE ANSWER TO A REAL QUESTION rather than a
        // null guard. This list exists to answer "what should I reorder?" — and a product the shop
        // deliberately withdrew (S03) is not something to reorder, however few are left on the shelf.
        // Asking for Active above and inner-joining here means the report says nothing about
        // discontinued stock, which is exactly right: an archived skillet running low is not news.
        //
        // Note the shape: filtering by an ABSENT name is doing real work, not defending against a
        // missing row. Written as `byId.TryGetValue(...) ? name : "(unknown)"` it would silently turn
        // a policy into a rendering quirk, and the report would fill up with rows nobody can act on.
        return
        [
            .. snapshots
                .Where(snapshot => byId.ContainsKey(snapshot.Stock.ProductId))
                .Select(snapshot => new LowStockLine(
                    snapshot.Stock.ProductId,
                    byId[snapshot.Stock.ProductId].Name,
                    byId[snapshot.Stock.ProductId].Sku.Value,
                    snapshot.Stock.OnHand,
                    StockAvailability.HeldAt(snapshot.Reservations, now),
                    snapshot.AvailableAt(now)))
                .OrderBy(line => line.Available)
                .ThenBy(line => line.ProductId)
        ];
    }
}

/// <summary>One row of the low-stock view: the shelf, the claims, and what's left.</summary>
public sealed record LowStockLine(
    Guid ProductId,
    string Name,
    string Sku,
    int OnHand,
    int Reserved,
    int Available);
