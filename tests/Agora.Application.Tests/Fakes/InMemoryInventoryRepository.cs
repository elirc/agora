using Agora.Application.Abstractions;
using Agora.Domain.Inventory;

namespace Agora.Application.Tests.Fakes;

/// <summary>The inventory port, in a list. No database, no clock, no surprises.</summary>
public sealed class InMemoryInventoryRepository : IInventoryRepository
{
    private readonly List<StockLevel> _stock = [];
    private readonly List<Reservation> _reservations = [];

    /// <summary>How many times a snapshot was asked for — the N+1 tripwire.</summary>
    // 🔗 A fake is allowed to be an instrument as well as a stand-in. S14 builds a real query counter
    // against EF; this is the same idea for the price of an int, and it lets a use-case test assert
    // "one round trip per page, not one per product" at the layer where that decision is actually made.
    public int SnapshotCalls { get; private set; }

    public StockLevel Stock(Guid productId, int onHand, DateTimeOffset nowUtc)
    {
        var level = StockLevel.Create(productId, onHand, nowUtc);
        _stock.Add(level);
        return level;
    }

    public Reservation Reserve(Guid productId, Guid cartId, int quantity, DateTimeOffset at, TimeSpan holdFor)
    {
        var reservation = Reservation.Take(productId, cartId, quantity, at, holdFor);
        _reservations.Add(reservation);
        return reservation;
    }

    public Task<StockLevel?> FindStockAsync(Guid productId, CancellationToken cancellationToken) =>
        Task.FromResult(_stock.SingleOrDefault(level => level.ProductId == productId));

    public Task<StockSnapshot?> SnapshotAsync(Guid productId, CancellationToken cancellationToken)
    {
        SnapshotCalls++;

        StockLevel? level = _stock.SingleOrDefault(candidate => candidate.ProductId == productId);

        return Task.FromResult(level is null
            ? null
            : new StockSnapshot(level, ReservationsFor(productId)));
    }

    public Task<IReadOnlyDictionary<Guid, StockSnapshot>> SnapshotManyAsync(
        IReadOnlyList<Guid> productIds,
        CancellationToken cancellationToken)
    {
        SnapshotCalls++;

        IReadOnlyDictionary<Guid, StockSnapshot> result = _stock
            .Where(level => productIds.Contains(level.ProductId))
            .ToDictionary(
                level => level.ProductId,
                level => new StockSnapshot(level, ReservationsFor(level.ProductId)));

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<StockSnapshot>> ListLowStockAsync(
        int threshold,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StockSnapshot> result = _stock
            .Select(level => new StockSnapshot(level, ReservationsFor(level.ProductId)))
            .Where(snapshot => snapshot.AvailableAt(asOf) <= threshold)
            .ToList();

        return Task.FromResult(result);
    }

    public void Add(StockLevel stock) => _stock.Add(stock);

    public void Add(Reservation reservation) => _reservations.Add(reservation);

    public Task<IReadOnlyList<Reservation>> ListForCartAsync(Guid cartId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Reservation> result = _reservations
            .Where(reservation => reservation.CartId == cartId)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Reservation>> ListExpiredAsync(
        DateTimeOffset asOf,
        int limit,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Reservation> result = _reservations
            .Where(reservation => !reservation.IsReleased && reservation.Window.Ends <= asOf)
            .Take(limit)
            .ToList();

        return Task.FromResult(result);
    }

    private List<Reservation> ReservationsFor(Guid productId) =>
        [.. _reservations.Where(reservation => reservation.ProductId == productId)];
}
