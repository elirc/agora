using Agora.Domain;
using Agora.Domain.Values;

namespace Agora.Domain.Tests.Values;

public class DateRangeTests
{
    private static readonly DateTimeOffset Friday = new(2026, 8, 7, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Monday = new(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_range_keeps_its_endpoints()
    {
        DateRange window = DateRange.From(Friday, Monday);

        Assert.Equal(Friday, window.Starts);
        Assert.Equal(Monday, window.Ends);
        Assert.Equal(TimeSpan.FromDays(3), window.Duration);
        Assert.False(window.IsEmpty);
    }

    [Fact]
    public void A_backwards_range_is_refused()
    {
        Assert.Throws<DomainRuleException>(() => DateRange.From(Monday, Friday));
    }

    [Fact]
    public void An_empty_range_is_legal_and_contains_nothing()
    {
        // [x, x) is the empty interval — a real thing, and what a cancelled-before-it-started campaign
        // collapses to. It is not the same as a backwards range, which describes nothing at all.
        DateRange empty = DateRange.From(Friday, Friday);

        Assert.True(empty.IsEmpty);
        Assert.False(empty.Contains(Friday));
    }

    // ── The half-open boundary: the whole reason this type exists ─────────────────────────────

    [Fact]
    public void Contains_includes_the_start_instant()
    {
        Assert.True(DateRange.From(Friday, Monday).Contains(Friday));
    }

    [Fact]
    public void Contains_excludes_the_end_instant()
    {
        // ⭐ THE ASSERTION THAT PAYS FOR THE TYPE. At exactly Monday 00:00 the Friday–Monday window is
        // OVER. If this were inclusive, a Monday–Wednesday window starting at the same instant would
        // also be running, two campaigns would own one tick, and "which price?" would have two
        // answers — decided by whichever row the database returned first.
        Assert.False(DateRange.From(Friday, Monday).Contains(Monday));
    }

    [Fact]
    public void Back_to_back_windows_tile_without_overlapping()
    {
        DateRange first = DateRange.From(Friday, Monday);
        DateRange second = DateRange.From(Monday, Monday.AddDays(3));

        Assert.False(first.Overlaps(second));
        Assert.False(first.Contains(Monday));
        Assert.True(second.Contains(Monday));
    }

    [Fact]
    public void Contains_excludes_instants_outside()
    {
        DateRange window = DateRange.From(Friday, Monday);

        Assert.False(window.Contains(Friday.AddTicks(-1)));
        Assert.True(window.Contains(Monday.AddTicks(-1)));
    }

    [Fact]
    public void Contains_answers_about_the_INSTANT_not_the_wall_clock()
    {
        // TRY (the sprint's exercise): "the sale starts at midnight" — whose midnight? The window's
        // endpoints are instants, so the answer is in the data. Midnight in New York (UTC-4) is 04:00
        // UTC, and both of these are the SAME MOMENT written two ways. The range agrees, because it
        // compares instants and never asks a machine what time it is locally.
        DateRange window = DateRange.From(
            new DateTimeOffset(2026, 8, 7, 0, 0, 0, TimeSpan.FromHours(-4)),
            new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.FromHours(-4)));

        Assert.True(window.Contains(new DateTimeOffset(2026, 8, 7, 4, 0, 0, TimeSpan.Zero)));
        Assert.False(window.Contains(new DateTimeOffset(2026, 8, 7, 3, 59, 59, TimeSpan.Zero)));
    }

    // ── Overlap ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Overlaps_is_symmetric_and_detects_a_shared_instant()
    {
        DateRange first = DateRange.From(Friday, Monday);
        DateRange overlapping = DateRange.From(Monday.AddDays(-1), Monday.AddDays(2));

        Assert.True(first.Overlaps(overlapping));
        Assert.True(overlapping.Overlaps(first));
    }

    // ── EndingAt: what "end the sale now" is made of ──────────────────────────────────────────

    [Fact]
    public void EndingAt_cuts_a_running_window_short()
    {
        DateRange cut = DateRange.From(Friday, Monday).EndingAt(Friday.AddDays(1));

        Assert.Equal(Friday, cut.Starts);
        Assert.Equal(Friday.AddDays(1), cut.Ends);
    }

    [Fact]
    public void EndingAt_before_the_start_collapses_the_window_to_empty()
    {
        // Cancelling a sale that has not started yet: it never ran, and the window says so rather
        // than becoming backwards.
        DateRange cut = DateRange.From(Friday, Monday).EndingAt(Friday.AddDays(-5));

        Assert.True(cut.IsEmpty);
        Assert.Equal(Friday, cut.Starts);
    }

    [Fact]
    public void EndingAt_after_the_end_leaves_history_alone()
    {
        DateRange window = DateRange.From(Friday, Monday);

        Assert.Equal(window, window.EndingAt(Monday.AddDays(5)));
    }
}
