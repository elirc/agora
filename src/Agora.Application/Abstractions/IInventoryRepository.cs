using Agora.Domain.Inventory;

namespace Agora.Application.Abstractions;

/// <summary>Stock levels and the reservations held against them.</summary>
// 📘 ONE PORT FOR BOTH, and that is a deliberate answer to "one repository per entity?" — a rule
// people repeat as though it were the dependency rule. StockLevel and Reservation are never usefully
// loaded apart: every question worth asking ("can this shopper have two?") needs the shelf AND the
// claims against it, as of one instant. Two repositories would mean two round trips and two chances
// to read them a moment apart — which, in a file about time, is the bug we are here to prevent.
//
// The unit is the QUESTION, not the table.
public interface IInventoryRepository
{
    /// <summary>The stock row for a product, or null if the product was never stocked.</summary>
    Task<StockLevel?> FindStockAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>
    /// Stock plus every reservation against it — the pair every availability question needs.
    /// </summary>
    // WHY reservations that are already released or expired come back too: because filtering them out
    // in SQL would mean the database deciding what "still counts", using its own clock, in a WHERE
    // clause — a THIRD place time enters the system, invisible to the domain and untestable with a
    // fake clock. StockAvailability does that filtering, as of the instant the caller names.
    //
    // The honest cost is rows: a popular product accumulates reservations forever and this returns
    // all of them. That is what the sweep is for (it releases and, in a later sprint, could prune),
    // and it is why the sweep matters operationally even though it is not what makes expiry CORRECT.
    // 🔗 S14 revisits this when the read models arrive — it is exactly the shape of query that turns
    // into a projection.
    Task<StockSnapshot?> SnapshotAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>Snapshots for many products at once — the catalog page's query.</summary>
    // 🔗 The N+1 this prevents is the one S14 goes hunting for. A grid of twelve products asking
    // twelve times is the classic, and it is easier not to write it than to find it later.
    Task<IReadOnlyDictionary<Guid, StockSnapshot>> SnapshotManyAsync(
        IReadOnlyList<Guid> productIds,
        CancellationToken cancellationToken);

    /// <summary>Products whose available count at <paramref name="asOf"/> is at or below a threshold.</summary>
    Task<IReadOnlyList<StockSnapshot>> ListLowStockAsync(
        int threshold,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);

    void Add(StockLevel stock);

    void Add(Reservation reservation);

    /// <summary>Live-or-not, every reservation for a cart — checkout and abandonment both need these.</summary>
    Task<IReadOnlyList<Reservation>> ListForCartAsync(Guid cartId, CancellationToken cancellationToken);

    /// <summary>Unreleased reservations whose window has closed by <paramref name="asOf"/>.</summary>
    Task<IReadOnlyList<Reservation>> ListExpiredAsync(
        DateTimeOffset asOf,
        int limit,
        CancellationToken cancellationToken);
}

/// <summary>A product's shelf and every claim against it, loaded together.</summary>
public sealed record StockSnapshot(StockLevel Stock, IReadOnlyList<Reservation> Reservations)
{
    /// <summary>What a new shopper may have at <paramref name="asOf"/>.</summary>
    public int AvailableAt(DateTimeOffset asOf) =>
        StockAvailability.Available(Stock, Reservations, asOf);
}
