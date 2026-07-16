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

        // 📘 THE ADMIN SEES BOTH NUMBERS, and that is the whole design of this view. "2 available" is
        // an emergency if there are two on the shelf and a non-event if there are nine with seven in
        // people's carts — same figure, opposite actions. A low-stock report that only shows
        // availability sends someone to the stockroom to count skillets that are already there.
        return snapshots
            .Select(snapshot => new LowStockLine(
                snapshot.Stock.ProductId,
                snapshot.Stock.OnHand,
                StockAvailability.HeldAt(snapshot.Reservations, now),
                snapshot.AvailableAt(now)))
            .OrderBy(line => line.Available)
            .ThenBy(line => line.ProductId)
            .ToList();
    }
}

/// <summary>One row of the low-stock view: the shelf, the claims, and what's left.</summary>
public sealed record LowStockLine(Guid ProductId, int OnHand, int Reserved, int Available);
