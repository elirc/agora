using Agora.Domain;
using Agora.Domain.Inventory;

namespace Agora.Domain.Tests.Inventory;

public class StockLevelTests
{
    private static readonly Guid Skillet = Guid.NewGuid();
    private static readonly DateTimeOffset Noon = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_stock_is_born_at_version_1()
    {
        StockLevel stock = StockLevel.Create(Skillet, 3, Noon);

        Assert.Equal(Skillet, stock.ProductId);
        Assert.Equal(3, stock.OnHand);
        Assert.Equal(1, stock.Version);
    }

    [Fact]
    public void Stock_cannot_be_created_negative()
    {
        Assert.Throws<DomainRuleException>(() => StockLevel.Create(Skillet, -1, Noon));
    }

    [Fact]
    public void Stock_can_be_created_empty()
    {
        // Zero is a legitimate quantity of skillets to own. Only NEGATIVE is nonsense.
        Assert.Equal(0, StockLevel.Create(Skillet, 0, Noon).OnHand);
    }

    [Fact]
    public void Receiving_adds_to_the_shelf_and_bumps_the_version()
    {
        StockLevel stock = StockLevel.Create(Skillet, 3, Noon);

        stock.Receive(5, Noon.AddHours(1));

        Assert.Equal(8, stock.OnHand);
        Assert.Equal(2, stock.Version);
        Assert.Equal(Noon.AddHours(1), stock.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Receiving_a_non_positive_quantity_is_refused(int quantity)
    {
        StockLevel stock = StockLevel.Create(Skillet, 3, Noon);

        Assert.Throws<DomainRuleException>(() => stock.Receive(quantity, Noon));
    }

    [Fact]
    public void A_stock_take_states_the_count_rather_than_the_delta()
    {
        StockLevel stock = StockLevel.Create(Skillet, 3, Noon);

        // Someone counted the shelf and found two. They know the answer, not the difference.
        stock.CountedAt(2, Noon);

        Assert.Equal(2, stock.OnHand);
    }

    [Fact]
    public void A_stock_take_cannot_go_negative()
    {
        StockLevel stock = StockLevel.Create(Skillet, 3, Noon);

        Assert.Throws<DomainRuleException>(() => stock.CountedAt(-1, Noon));
    }

    [Fact]
    public void Claiming_against_stock_bumps_the_version_without_moving_a_skillet()
    {
        StockLevel stock = StockLevel.Create(Skillet, 3, Noon);

        stock.ClaimedAgainst(Noon);

        // Nothing left the building — but what the NEXT shopper may conclude has changed, and that
        // is the thing the token serialises.
        Assert.Equal(3, stock.OnHand);
        Assert.Equal(2, stock.Version);
    }
}

public class ReservationTests
{
    private static readonly Guid Skillet = Guid.NewGuid();
    private static readonly Guid SomeCart = Guid.NewGuid();
    private static readonly DateTimeOffset Noon = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);

    private static Reservation Take(int quantity = 1) =>
        Reservation.Take(Skillet, SomeCart, quantity, Noon, TenMinutes);

    [Fact]
    public void A_reservation_opens_at_now_and_closes_after_the_hold()
    {
        Reservation reservation = Take();

        Assert.Equal(Noon, reservation.Window.Starts);
        Assert.Equal(Noon.AddMinutes(10), reservation.Window.Ends);
        Assert.False(reservation.IsReleased);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_reservation_must_be_for_at_least_one_unit(int quantity)
    {
        Assert.Throws<DomainRuleException>(() => Take(quantity));
    }

    [Fact]
    public void A_reservation_needs_a_cart_to_belong_to()
    {
        Assert.Throws<DomainRuleException>(
            () => Reservation.Take(Skillet, Guid.Empty, 1, Noon, TenMinutes));
    }

    [Fact]
    public void A_reservation_must_be_held_for_a_positive_duration()
    {
        Assert.Throws<DomainRuleException>(
            () => Reservation.Take(Skillet, SomeCart, 1, Noon, TimeSpan.Zero));
    }

    // The window, pinned at the boundary to the tick — the same treatment SaleCampaign's window gets,
    // and for the same reason: half-open intervals are where off-by-one bugs go to live.
    [Fact]
    public void The_window_is_half_open_to_the_tick()
    {
        Reservation reservation = Take();

        Assert.True(reservation.IsActiveAt(Noon));
        Assert.True(reservation.IsActiveAt(Noon.AddMinutes(10).AddTicks(-1)));
        Assert.False(reservation.IsActiveAt(Noon.AddTicks(-1)));
        Assert.False(reservation.IsActiveAt(Noon.AddMinutes(10)));
    }

    [Fact]
    public void An_expired_reservation_stops_counting_with_nobody_doing_anything()
    {
        Reservation reservation = Take();

        // No sweep ran. No method was called. Ten minutes simply happened, and the claim is over —
        // which is the entire mechanism. The janitor is convenience; the window is correctness.
        Assert.False(reservation.IsActiveAt(Noon.AddMinutes(11)));
        Assert.False(reservation.IsReleased);
    }

    [Fact]
    public void Releasing_stops_the_claim_immediately()
    {
        Reservation reservation = Take();

        reservation.Release(Noon.AddMinutes(2));

        Assert.True(reservation.IsReleased);
        Assert.Equal(Noon.AddMinutes(2), reservation.ReleasedAtUtc);
        Assert.False(reservation.IsActiveAt(Noon.AddMinutes(3)));

        // Released BEFORE its window ended — and it is not active even back inside the window,
        // because "somebody let it go" beats "time has not run out yet".
        Assert.False(reservation.IsActiveAt(Noon.AddMinutes(1)));
    }

    [Fact]
    public void Releasing_twice_is_a_no_op_and_keeps_the_first_instant()
    {
        Reservation reservation = Take();

        reservation.Release(Noon.AddMinutes(2));
        reservation.Release(Noon.AddMinutes(5));

        // No throw — and the record still says when it ACTUALLY happened, not when the retry did.
        // Everything that releases a reservation is a retry waiting to happen (see S10).
        Assert.Equal(Noon.AddMinutes(2), reservation.ReleasedAtUtc);
    }
}

public class StockAvailabilityTests
{
    private static readonly Guid Skillet = Guid.NewGuid();
    private static readonly DateTimeOffset Noon = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    private static StockLevel Shelf(int onHand = 3) => StockLevel.Create(Skillet, onHand, Noon);

    private static Reservation Held(int quantity, int minutes = 10) =>
        Reservation.Take(Skillet, Guid.NewGuid(), quantity, Noon, TimeSpan.FromMinutes(minutes));

    [Fact]
    public void With_nothing_reserved_everything_on_the_shelf_is_available()
    {
        Assert.Equal(3, StockAvailability.Available(Shelf(), [], Noon));
    }

    [Fact]
    public void Live_reservations_come_off_the_top()
    {
        Reservation[] reservations = [Held(1), Held(1)];

        Assert.Equal(1, StockAvailability.Available(Shelf(), reservations, Noon));
        Assert.Equal(2, StockAvailability.HeldAt(reservations, Noon));
    }

    [Fact]
    public void Availability_is_a_function_of_the_instant_you_ask_about()
    {
        // Two claims: one short, one long. Nothing runs, nothing is released, nobody touches a
        // skillet — and the answer changes three times, purely because time passed.
        Reservation[] reservations = [Held(1, minutes: 5), Held(1, minutes: 30)];

        Assert.Equal(1, StockAvailability.Available(Shelf(), reservations, Noon));
        Assert.Equal(2, StockAvailability.Available(Shelf(), reservations, Noon.AddMinutes(6)));
        Assert.Equal(3, StockAvailability.Available(Shelf(), reservations, Noon.AddMinutes(31)));
    }

    [Fact]
    public void A_released_reservation_gives_its_stock_back_at_once()
    {
        Reservation reservation = Held(2);
        Reservation[] reservations = [reservation];

        Assert.Equal(1, StockAvailability.Available(Shelf(), reservations, Noon));

        reservation.Release(Noon.AddMinutes(1));

        Assert.Equal(3, StockAvailability.Available(Shelf(), reservations, Noon.AddMinutes(1)));
    }

    [Fact]
    public void Oversold_stock_reads_as_none_available_rather_than_a_negative_number()
    {
        // The stock take found two; three are already promised. Nobody was wrong, and the shop owes
        // somebody an apology — but a NEW shopper is simply told no, not "-1 left".
        Reservation[] reservations = [Held(3)];

        Assert.Equal(0, StockAvailability.Available(Shelf(onHand: 2), reservations, Noon));
        Assert.False(StockAvailability.CanTake(Shelf(onHand: 2), reservations, 1, Noon));
    }

    [Fact]
    public void CanTake_answers_the_friendly_question()
    {
        Reservation[] reservations = [Held(2)];

        Assert.True(StockAvailability.CanTake(Shelf(), reservations, 1, Noon));
        Assert.False(StockAvailability.CanTake(Shelf(), reservations, 2, Noon));

        // Asking for nothing is not "sure, have nothing" — it is a nonsense request.
        Assert.False(StockAvailability.CanTake(Shelf(), reservations, 0, Noon));

        // ...and the same question, six minutes later, once a ten-minute hold has expired.
        Assert.True(StockAvailability.CanTake(Shelf(), [Held(2, minutes: 5)], 3, Noon.AddMinutes(6)));
    }
}
