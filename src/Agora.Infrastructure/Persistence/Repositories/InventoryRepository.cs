using Agora.Application.Abstractions;
using Agora.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Persistence.Repositories;

internal sealed class InventoryRepository(AgoraDbContext db) : IInventoryRepository
{
    public async Task<StockLevel?> FindStockAsync(Guid productId, CancellationToken cancellationToken) =>
        await db.StockLevels.SingleOrDefaultAsync(s => s.ProductId == productId, cancellationToken);

    public async Task<StockSnapshot?> SnapshotAsync(Guid productId, CancellationToken cancellationToken)
    {
        // 🔍 TRACKED, NOT AsNoTracking — and that is the one difference between this and every read
        // in S01–S04. ReserveStock MUTATES what this returns: it bumps the stock row's version and
        // releases a claim. Untracked entities would let both of those changes vanish silently at
        // CommitAsync, and the test that catches it is the concurrency one, three layers away.
        //
        // The read-only queries below are AsNoTracking, because they are reads. When a repository
        // method's result will be written to, say so here rather than hoping the caller knows.
        StockLevel? stock = await db.StockLevels
            .SingleOrDefaultAsync(s => s.ProductId == productId, cancellationToken);

        if (stock is null)
        {
            return null;
        }

        List<Reservation> reservations = await db.Reservations
            .Where(r => r.ProductId == productId)
            .ToListAsync(cancellationToken);

        return new StockSnapshot(stock, reservations);
    }

    public async Task<IReadOnlyDictionary<Guid, StockSnapshot>> SnapshotManyAsync(
        IReadOnlyList<Guid> productIds,
        CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return new Dictionary<Guid, StockSnapshot>();
        }

        // 📘 TWO QUERIES FOR A WHOLE PAGE, not two per product. This is the shape that keeps a
        // twelve-product grid at 2 round trips instead of 24 — the N+1 that S14 goes looking for,
        // declined in advance. The join is done in memory, on two small sets already fetched, which
        // is cheaper and clearer than a grouped query and cannot surprise the SQL planner.
        List<StockLevel> stock = await db.StockLevels
            .AsNoTracking()
            .Where(s => productIds.Contains(s.ProductId))
            .ToListAsync(cancellationToken);

        List<Reservation> reservations = await db.Reservations
            .AsNoTracking()
            .Where(r => productIds.Contains(r.ProductId))
            .ToListAsync(cancellationToken);

        ILookup<Guid, Reservation> byProduct = reservations.ToLookup(r => r.ProductId);

        return stock.ToDictionary(
            s => s.ProductId,
            s => new StockSnapshot(s, [.. byProduct[s.ProductId]]));
    }

    public async Task<IReadOnlyList<StockSnapshot>> ListLowStockAsync(
        int threshold,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        // ⚠️ THE HONEST VERSION OF A QUERY THAT WANTS TO BE CLEVER. "Available" is on-hand minus live
        // claims as of an instant — a computation the DOMAIN owns (StockAvailability), not a WHERE
        // clause. Pushing it into SQL would mean a second definition of availability living in the
        // database, in a different language, that no domain test can reach, and which would drift the
        // first time the rule gains a nuance.
        //
        // So the narrowing done in SQL is a cheap over-approximation: a product cannot be at or below
        // the threshold unless its on-hand is — reservations only ever REMOVE availability, never add
        // it. That filter is safe to be sloppy in exactly one direction, and the domain makes the real
        // decision on the rows that survive.
        //
        // 🔗 It over-fetches, and that is a real cost this sprint accepts. When "low stock" becomes a
        // dashboard someone loads every thirty seconds, this is precisely the query S14 turns into a
        // read model — and it will be a good example BECAUSE the rule lives in one testable place.
        List<StockLevel> candidates = await db.StockLevels
            .AsNoTracking()
            .Where(s => s.OnHand <= threshold || db.Reservations.Any(r => r.ProductId == s.ProductId))
            .ToListAsync(cancellationToken);

        Guid[] productIds = [.. candidates.Select(s => s.ProductId)];

        List<Reservation> reservations = await db.Reservations
            .AsNoTracking()
            .Where(r => productIds.Contains(r.ProductId))
            .ToListAsync(cancellationToken);

        ILookup<Guid, Reservation> byProduct = reservations.ToLookup(r => r.ProductId);

        return
        [
            .. candidates
                .Select(s => new StockSnapshot(s, [.. byProduct[s.ProductId]]))
                .Where(snapshot => snapshot.AvailableAt(asOf) <= threshold)
        ];
    }

    public void Add(StockLevel stock) => db.StockLevels.Add(stock);

    public void Add(Reservation reservation) => db.Reservations.Add(reservation);

    public async Task<IReadOnlyList<Reservation>> ListForCartAsync(
        Guid cartId,
        CancellationToken cancellationToken) =>
        await db.Reservations
            .Where(r => r.CartId == cartId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Reservation>> ListExpiredAsync(
        DateTimeOffset asOf,
        int limit,
        CancellationToken cancellationToken) =>
        // The sweep's query. `Ends <= asOf` is a DateTimeOffset comparison inside a complex property —
        // the thing SQLite cannot do natively and the UTC-ticks converter makes into an integer
        // compare. Ordered so a capped batch is deterministic: oldest lapsed claims tidied first,
        // rather than whichever 500 the planner felt like.
        await db.Reservations
            .Where(r => r.ReleasedAtUtc == null && r.Window.Ends <= asOf)
            .OrderBy(r => r.Window.Ends)
            .Take(limit)
            .ToListAsync(cancellationToken);
}
