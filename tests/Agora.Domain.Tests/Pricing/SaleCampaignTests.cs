using Agora.Domain;
using Agora.Domain.Pricing;
using Agora.Domain.Values;

namespace Agora.Domain.Tests.Pricing;

public class SaleCampaignTests
{
    private static readonly Guid SomeProduct = Guid.NewGuid();

    private static readonly DateTimeOffset Friday = new(2026, 8, 7, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Monday = new(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);

    private static SaleCampaign Weekend(string? name = "Labour Day Weekend") =>
        SaleCampaign.Schedule(
            SomeProduct,
            name,
            Discount.PercentOff(20m),
            DateRange.From(Friday, Monday),
            Friday.AddDays(-7));

    // ── Scheduling ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Schedule_assigns_the_fields_and_trims_the_name()
    {
        SaleCampaign campaign = Weekend("  Labour Day Weekend  ");

        Assert.NotEqual(Guid.Empty, campaign.Id);
        Assert.Equal(SomeProduct, campaign.ProductId);
        Assert.Equal("Labour Day Weekend", campaign.Name);
        Assert.Equal(DiscountKind.PercentOff, campaign.Discount.Kind);
        Assert.Equal(Friday, campaign.Window.Starts);
        Assert.Equal(Monday, campaign.Window.Ends);
        Assert.Equal(Friday.AddDays(-7), campaign.CreatedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Schedule_rejects_a_missing_name(string? name)
    {
        Assert.Throws<DomainRuleException>(() => Weekend(name));
    }

    [Fact]
    public void Schedule_rejects_a_campaign_with_no_product()
    {
        Assert.Throws<DomainRuleException>(() => SaleCampaign.Schedule(
            Guid.Empty, "Weekend", Discount.PercentOff(20m), DateRange.From(Friday, Monday), Friday));
    }

    [Fact]
    public void Schedule_rejects_an_empty_window()
    {
        // A sale that covers no instants can never discount anything. DateRange permits [x, x); a
        // SALE born that way is a mistake, and that rule belongs to the sale.
        Assert.Throws<DomainRuleException>(() => SaleCampaign.Schedule(
            SomeProduct, "Weekend", Discount.PercentOff(20m), DateRange.From(Friday, Friday), Friday));
    }

    [Fact]
    public void Schedule_allows_a_window_entirely_in_the_past_or_the_future()
    {
        // Creating is not activating: backdating records a sale that ran, and post-dating plans one.
        // Neither needs a clock, which is exactly why Schedule does not have one.
        SaleCampaign past = SaleCampaign.Schedule(
            SomeProduct, "Last year", Discount.PercentOff(20m),
            DateRange.From(Friday.AddYears(-1), Monday.AddYears(-1)), Friday);

        SaleCampaign future = SaleCampaign.Schedule(
            SomeProduct, "Next year", Discount.PercentOff(20m),
            DateRange.From(Friday.AddYears(1), Monday.AddYears(1)), Friday);

        Assert.False(past.IsActiveAt(Friday));
        Assert.False(future.IsActiveAt(Friday));
    }

    // ── IsActiveAt: the pure, windowed check ─────────────────────────────────────────────────

    [Fact]
    public void IsActiveAt_is_true_inside_the_window()
    {
        Assert.True(Weekend().IsActiveAt(Friday.AddHours(36)));
    }

    [Fact]
    public void IsActiveAt_includes_the_first_instant_and_excludes_the_last()
    {
        SaleCampaign campaign = Weekend();

        // ⭐ half-open, pinned on the entity as well as on DateRange: the sale is on at exactly
        // Friday 00:00 and off at exactly Monday 00:00. A Monday-starting campaign therefore picks up
        // the tick this one puts down — no instant belongs to both.
        Assert.True(campaign.IsActiveAt(Friday));
        Assert.False(campaign.IsActiveAt(Monday));
        Assert.True(campaign.IsActiveAt(Monday.AddTicks(-1)));
    }

    [Fact]
    public void IsActiveAt_is_false_before_and_after()
    {
        SaleCampaign campaign = Weekend();

        Assert.False(campaign.IsActiveAt(Friday.AddTicks(-1)));
        Assert.False(campaign.IsActiveAt(Monday.AddDays(1)));
    }

    [Fact]
    public void IsActiveAt_compares_instants_not_wall_clocks()
    {
        // TRY (the sprint's exercise): "the sale starts at midnight" — WHOSE midnight? Friday 00:00
        // UTC is Thursday 20:00 in New York. These are the same instant expressed two ways, and the
        // campaign agrees with itself about both, because it asks about an INSTANT and never about a
        // machine's locale. Hand the same code two servers in two timezones and it answers the same.
        SaleCampaign campaign = Weekend();

        Assert.True(campaign.IsActiveAt(new DateTimeOffset(2026, 8, 6, 20, 0, 0, TimeSpan.FromHours(-4))));
        Assert.False(campaign.IsActiveAt(new DateTimeOffset(2026, 8, 6, 19, 59, 59, TimeSpan.FromHours(-4))));
    }

    [Fact]
    public void HasEndedBy_is_true_from_the_end_instant_onward()
    {
        SaleCampaign campaign = Weekend();

        Assert.False(campaign.HasEndedBy(Monday.AddTicks(-1)));
        Assert.True(campaign.HasEndedBy(Monday));
    }

    // ── End: cutting the window short ────────────────────────────────────────────────────────

    [Fact]
    public void End_cuts_a_running_campaign_short_at_the_instant_given()
    {
        SaleCampaign campaign = Weekend();
        DateTimeOffset saturdayNoon = Friday.AddHours(36);

        campaign.End(saturdayNoon);

        Assert.Equal(Friday, campaign.Window.Starts);
        Assert.Equal(saturdayNoon, campaign.Window.Ends);
        Assert.False(campaign.IsActiveAt(saturdayNoon));
        // The row survives with its history: it ran Friday to Saturday noon, and the report can say so.
        Assert.True(campaign.IsActiveAt(saturdayNoon.AddTicks(-1)));
    }

    [Fact]
    public void Ending_a_campaign_that_has_not_started_means_it_never_ran()
    {
        SaleCampaign campaign = Weekend();

        campaign.End(Friday.AddDays(-1));

        Assert.True(campaign.Window.IsEmpty);
        Assert.False(campaign.IsActiveAt(Friday));
        Assert.False(campaign.IsActiveAt(Friday.AddHours(1)));
    }

    [Fact]
    public void Ending_an_already_ended_campaign_throws()
    {
        SaleCampaign campaign = Weekend();

        Assert.Throws<DomainRuleException>(() => campaign.End(Monday));
        Assert.Throws<DomainRuleException>(() => campaign.End(Monday.AddDays(3)));
    }

    [Fact]
    public void Ending_twice_throws_the_second_time()
    {
        SaleCampaign campaign = Weekend();
        campaign.End(Friday.AddHours(1));

        Assert.Throws<DomainRuleException>(() => campaign.End(Friday.AddHours(2)));
    }
}
