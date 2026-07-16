using Agora.Application.Abstractions;
using Agora.Application.Errors;
using Agora.Application.Inventory;
using Agora.Application.Tests.Fakes;
using Agora.Domain.Access;
using Agora.Domain.Catalog;
using Agora.Domain.Inventory;

namespace Agora.Application.Tests.Inventory;

public class ReserveStockTests
{
    private static readonly Guid Skillet = Guid.NewGuid();
    private static readonly Guid MyCart = Guid.NewGuid();
    private static readonly Guid TheirCart = Guid.NewGuid();
    private static readonly DateTimeOffset Noon = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    private static (ReserveStock UseCase, InMemoryInventoryRepository Inventory, CountingUnitOfWork Uow)
        Subject(int onHand = 3, TimeProvider? clock = null)
    {
        var inventory = new InMemoryInventoryRepository();
        inventory.Stock(Skillet, onHand, Noon);
        var uow = new CountingUnitOfWork();

        return (new ReserveStock(inventory, uow, clock ?? new FixedClock(Noon)), inventory, uow);
    }

    [Fact]
    public async Task Reserving_available_stock_succeeds_and_holds_it()
    {
        (ReserveStock reserve, InMemoryInventoryRepository inventory, CountingUnitOfWork uow) = Subject();

        ReservationResult result = await reserve.ExecuteAsync(Skillet, MyCart, 2, default);

        Assert.True(result.Success);
        Assert.Equal(2, result.Reservation!.Quantity);
        Assert.Equal(Noon.Add(ReservationPolicy.HoldFor), result.Reservation.Window.Ends);
        Assert.Equal(1, uow.Commits);

        StockSnapshot snapshot = (await inventory.SnapshotAsync(Skillet, default))!;
        Assert.Equal(1, snapshot.AvailableAt(Noon));
    }

    [Fact]
    public async Task Reserving_bumps_the_stock_version_without_moving_stock()
    {
        (ReserveStock reserve, InMemoryInventoryRepository inventory, _) = Subject();

        await reserve.ExecuteAsync(Skillet, MyCart, 1, default);

        StockLevel stock = (await inventory.FindStockAsync(Skillet, default))!;

        // The version is what serialises the race — see the concurrency test in Api.Tests for the
        // proof that the database enforces it. Here we only pin that the use case touches it at all,
        // because a token nobody touches serialises nothing.
        Assert.Equal(2, stock.Version);
        Assert.Equal(3, stock.OnHand);
    }

    [Fact]
    public async Task More_than_is_available_is_refused_with_the_number_that_is_left()
    {
        (ReserveStock reserve, InMemoryInventoryRepository inventory, CountingUnitOfWork uow) = Subject();
        inventory.Reserve(Skillet, TheirCart, 2, Noon, ReservationPolicy.HoldFor);

        ReservationResult result = await reserve.ExecuteAsync(Skillet, MyCart, 2, default);

        Assert.False(result.Success);
        Assert.Null(result.Reservation);

        // The UI needs the number, not just the "no" — "only 1 left" is a sentence a shopper can act
        // on; "unavailable" makes them refresh and guess.
        Assert.Equal(1, result.Available);

        // Nothing was written. Being told no is not a state change.
        Assert.Equal(0, uow.Commits);
    }

    [Fact]
    public async Task Stock_held_by_somebody_else_that_has_expired_is_available_again()
    {
        var clock = new MovableClock(Noon);
        (ReserveStock reserve, InMemoryInventoryRepository inventory, _) = Subject(onHand: 1, clock: clock);

        // Somebody else took the last one at noon and then wandered off.
        inventory.Reserve(Skillet, TheirCart, 1, Noon, ReservationPolicy.HoldFor);

        ReservationResult tooSoon = await reserve.ExecuteAsync(Skillet, MyCart, 1, default);
        Assert.False(tooSoon.Success);

        // Sixteen minutes later. NOTHING RAN — no sweep, no job, no notification. Time passed, which
        // is the whole mechanism.
        clock.Advance(TimeSpan.FromMinutes(16));

        ReservationResult later = await reserve.ExecuteAsync(Skillet, MyCart, 1, default);
        Assert.True(later.Success);
    }

    [Fact]
    public async Task Reserving_twice_from_the_same_cart_replaces_the_claim_rather_than_stacking_it()
    {
        (ReserveStock reserve, InMemoryInventoryRepository inventory, _) = Subject(onHand: 3);

        await reserve.ExecuteAsync(Skillet, MyCart, 2, default);
        ReservationResult second = await reserve.ExecuteAsync(Skillet, MyCart, 3, default);

        // Without the replace, this shopper would be holding 2 + 3 = 5 against a shelf of 3 and would
        // be told the shop is out of a thing they are personally holding.
        Assert.True(second.Success);

        StockSnapshot snapshot = (await inventory.SnapshotAsync(Skillet, default))!;
        Assert.Equal(3, StockAvailability.HeldAt(snapshot.Reservations, Noon));
        Assert.Equal(0, snapshot.AvailableAt(Noon));
    }

    [Fact]
    public async Task Reserving_stock_for_a_product_that_was_never_stocked_is_a_404()
    {
        (ReserveStock reserve, _, _) = Subject();

        await Assert.ThrowsAsync<NotFoundException>(
            () => reserve.ExecuteAsync(Guid.NewGuid(), MyCart, 1, default));
    }
}

public class ReleaseStockTests
{
    private static readonly Guid Skillet = Guid.NewGuid();
    private static readonly Guid MyCart = Guid.NewGuid();
    private static readonly DateTimeOffset Noon = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Releasing_a_carts_claims_gives_the_stock_back()
    {
        var inventory = new InMemoryInventoryRepository();
        inventory.Stock(Skillet, 3, Noon);
        inventory.Reserve(Skillet, MyCart, 2, Noon, ReservationPolicy.HoldFor);
        var uow = new CountingUnitOfWork();

        var release = new ReleaseCartReservations(inventory, uow, new FixedClock(Noon.AddMinutes(1)));
        int released = await release.ExecuteAsync(MyCart, default);

        Assert.Equal(1, released);

        StockSnapshot snapshot = (await inventory.SnapshotAsync(Skillet, default))!;
        Assert.Equal(3, snapshot.AvailableAt(Noon.AddMinutes(1)));
    }

    [Fact]
    public async Task Releasing_a_carts_claims_twice_releases_nothing_the_second_time()
    {
        var inventory = new InMemoryInventoryRepository();
        inventory.Stock(Skillet, 3, Noon);
        inventory.Reserve(Skillet, MyCart, 2, Noon, ReservationPolicy.HoldFor);
        var uow = new CountingUnitOfWork();
        var release = new ReleaseCartReservations(inventory, uow, new FixedClock(Noon.AddMinutes(1)));

        Assert.Equal(1, await release.ExecuteAsync(MyCart, default));
        Assert.Equal(0, await release.ExecuteAsync(MyCart, default));
    }

    // ⚠️ THE TEST THE REVIEWER DEMANDED IN THE DESIGN DEBATE. A sweep that runs on a timer WILL
    // overlap itself, be retried, and race a checkout releasing the same claim. If any of those
    // double-releases is an error, every caller needs a try/catch around a thing that already worked —
    // and the first `catch { }` written there swallows a real failure along with the harmless one.
    [Fact]
    public async Task The_sweep_is_idempotent_when_it_overlaps_itself()
    {
        var clock = new MovableClock(Noon);
        var inventory = new InMemoryInventoryRepository();
        inventory.Stock(Skillet, 5, Noon);
        inventory.Reserve(Skillet, Guid.NewGuid(), 1, Noon, TimeSpan.FromMinutes(5));
        inventory.Reserve(Skillet, Guid.NewGuid(), 1, Noon, TimeSpan.FromMinutes(5));
        var uow = new CountingUnitOfWork();
        var sweep = new ReleaseExpiredReservations(inventory, uow, clock);

        // Nothing has expired yet.
        Assert.Equal(0, await sweep.ExecuteAsync(default));

        clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal(2, await sweep.ExecuteAsync(default));

        // The second pass finds nothing, because the first marked them — and crucially it does not
        // throw, does not double-count, and does not give back stock twice.
        Assert.Equal(0, await sweep.ExecuteAsync(default));

        StockSnapshot snapshot = (await inventory.SnapshotAsync(Skillet, default))!;
        Assert.Equal(5, snapshot.AvailableAt(clock.GetUtcNow()));
    }

    [Fact]
    public async Task The_sweep_does_not_touch_live_claims()
    {
        var clock = new MovableClock(Noon);
        var inventory = new InMemoryInventoryRepository();
        inventory.Stock(Skillet, 5, Noon);
        inventory.Reserve(Skillet, MyCart, 1, Noon, TimeSpan.FromMinutes(5));
        inventory.Reserve(Skillet, Guid.NewGuid(), 1, Noon, TimeSpan.FromMinutes(30));
        var sweep = new ReleaseExpiredReservations(inventory, new CountingUnitOfWork(), clock);

        clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal(1, await sweep.ExecuteAsync(default));

        // The thirty-minute claim is still holding its unit — the sweep is a janitor, not a bailiff.
        StockSnapshot snapshot = (await inventory.SnapshotAsync(Skillet, default))!;
        Assert.Equal(4, snapshot.AvailableAt(clock.GetUtcNow()));
    }
}

public class StockQueryTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Availability_for_a_page_is_one_round_trip_not_one_per_product()
    {
        var inventory = new InMemoryInventoryRepository();
        Guid[] products = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        foreach (Guid product in products)
        {
            inventory.Stock(product, 2, Noon);
        }

        var query = new StockAvailabilityQuery(inventory, new FixedClock(Noon));
        IReadOnlyDictionary<Guid, int> available = await query.AvailableAsync(products, default);

        Assert.Equal(3, available.Count);
        Assert.All(available.Values, count => Assert.Equal(2, count));

        // 🔗 The N+1 that S14 goes hunting for, refused at the layer where the decision is made.
        Assert.Equal(1, inventory.SnapshotCalls);
    }

    [Fact]
    public async Task A_product_that_was_never_stocked_reads_as_zero_not_missing()
    {
        var inventory = new InMemoryInventoryRepository();
        Guid unstocked = Guid.NewGuid();

        var query = new StockAvailabilityQuery(inventory, new FixedClock(Noon));
        IReadOnlyDictionary<Guid, int> available = await query.AvailableAsync([unstocked], default);

        Assert.Equal(0, available[unstocked]);
    }

    [Fact]
    public async Task An_empty_page_asks_the_database_nothing()
    {
        var inventory = new InMemoryInventoryRepository();
        var query = new StockAvailabilityQuery(inventory, new FixedClock(Noon));

        Assert.Empty(await query.AvailableAsync([], default));
        Assert.Equal(0, inventory.SnapshotCalls);
    }

    [Fact]
    public async Task Low_stock_shows_the_shelf_the_claims_and_what_is_left()
    {
        Category kitchen = CatalogFixtures.CategoryAt("Kitchen", "kitchen", 0);
        Product skillet = CatalogFixtures.ProductAt(kitchen.Id, "Skillet", "skillet", "AGA-KIT-002", 1);
        var catalog = new InMemoryProductRepository([skillet], [kitchen]);

        var inventory = new InMemoryInventoryRepository();
        inventory.Stock(skillet.Id, 9, Noon);

        // Nine on the shelf, seven in people's carts. Availability is 2 — but sending someone to the
        // stockroom would be a wasted trip, and only showing them "2" is what causes that trip.
        inventory.Reserve(skillet.Id, Guid.NewGuid(), 7, Noon, ReservationPolicy.HoldFor);

        var list = new ListLowStock(
            inventory,
            catalog,
            StubCurrentUser.SignedIn(Role.StoreAdmin),
            new FixedClock(Noon));

        IReadOnlyList<LowStockLine> lines = await list.ExecuteAsync(ListLowStock.DefaultThreshold, default);

        LowStockLine line = Assert.Single(lines);
        Assert.Equal(9, line.OnHand);
        Assert.Equal(7, line.Reserved);
        Assert.Equal(2, line.Available);

        // The name and SKU are why this report is actionable: "AGA-KIT-002 is nearly out" is not a
        // sentence anybody can act on without looking something up.
        Assert.Equal("Skillet", line.Name);
        Assert.Equal("AGA-KIT-002", line.Sku);
    }

    [Fact]
    public async Task An_archived_product_is_not_something_to_reorder()
    {
        Category kitchen = CatalogFixtures.CategoryAt("Kitchen", "kitchen", 0);
        Product discontinued = CatalogFixtures.ProductAt(kitchen.Id, "Old Kettle", "old-kettle", "AGA-KIT-009", 1);
        discontinued.Archive(Noon.AddMinutes(-1));
        var catalog = new InMemoryProductRepository([discontinued], [kitchen]);

        var inventory = new InMemoryInventoryRepository();
        inventory.Stock(discontinued.Id, 1, Noon);

        var list = new ListLowStock(
            inventory,
            catalog,
            StubCurrentUser.SignedIn(Role.StoreAdmin),
            new FixedClock(Noon));

        // One left on the shelf — and it does not appear, because the shop withdrew it on purpose.
        // A discontinued skillet running low is not news.
        Assert.Empty(await list.ExecuteAsync(ListLowStock.DefaultThreshold, default));
    }

    [Fact]
    public async Task Low_stock_is_not_a_shoppers_report()
    {
        var list = new ListLowStock(
            new InMemoryInventoryRepository(),
            new InMemoryProductRepository([], []),
            StubCurrentUser.SignedIn(Role.Customer),
            new FixedClock(Noon));

        await Assert.ThrowsAsync<ForbiddenException>(() => list.ExecuteAsync(3, default));
    }

    [Fact]
    public async Task Low_stock_rejects_a_negative_threshold()
    {
        var list = new ListLowStock(
            new InMemoryInventoryRepository(),
            new InMemoryProductRepository([], []),
            StubCurrentUser.SignedIn(Role.StoreAdmin),
            new FixedClock(Noon));

        await Assert.ThrowsAsync<RequestValidationException>(() => list.ExecuteAsync(-1, default));
    }
}
