using Agora.Domain.Pricing;
using Agora.Domain.Values;

namespace Agora.Domain.Tests.Pricing;

public class PriceResolverTests
{
    private static readonly Guid SomeProduct = Guid.NewGuid();

    private static readonly DateTimeOffset Friday = new(2026, 8, 7, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Saturday = Friday.AddDays(1);
    private static readonly DateTimeOffset Monday = new(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);

    private static readonly Money List = Money.Of(50.00m, CurrencyCode.Usd);

    private static SaleCampaign Campaign(
        Discount discount,
        DateTimeOffset starts,
        DateTimeOffset ends,
        DateTimeOffset? createdAt = null,
        string name = "Weekend") =>
        SaleCampaign.Schedule(SomeProduct, name, discount, DateRange.From(starts, ends), createdAt ?? Friday.AddDays(-7));

    [Fact]
    public void With_no_campaigns_the_effective_price_is_the_list_price()
    {
        EffectivePrice price = PriceResolver.Resolve(List, [], Saturday);

        Assert.Equal(List, price.Amount);
        Assert.Equal(List, price.ListPrice);
        Assert.False(price.IsOnSale);
        Assert.Null(price.Campaign);
        Assert.Equal(0, price.SavedPercent);
        Assert.True(price.Savings.IsZero);
    }

    [Fact]
    public void A_campaign_covering_the_instant_sets_the_price_and_explains_itself()
    {
        SaleCampaign weekend = Campaign(Discount.PercentOff(20m), Friday, Monday);

        EffectivePrice price = PriceResolver.Resolve(List, [weekend], Saturday);

        Assert.Equal(Money.Of(40.00m, CurrencyCode.Usd), price.Amount);
        Assert.Equal(List, price.ListPrice);
        Assert.True(price.IsOnSale);
        Assert.Same(weekend, price.Campaign);
        Assert.Equal(Money.Of(10.00m, CurrencyCode.Usd), price.Savings);
        Assert.Equal(20, price.SavedPercent);
    }

    // ⭐ THE SPRINT IN ONE TEST: same product, same campaign, same code — four instants, four
    // answers. Nobody flipped anything; the price is a function of time and this is the function.
    [Theory]
    [InlineData(-1, 50.00)]      // one tick before it opens: list price
    [InlineData(0, 40.00)]       // exactly at the start: on sale
    [InlineData(36, 40.00)]      // Saturday noon: on sale
    [InlineData(72, 50.00)]      // exactly at the end (Monday 00:00): over, list price
    public void The_price_is_a_function_of_the_instant(int hoursFromFriday, decimal expected)
    {
        SaleCampaign weekend = Campaign(Discount.PercentOff(20m), Friday, Monday);
        DateTimeOffset asOf = hoursFromFriday == -1
            ? Friday.AddTicks(-1)
            : Friday.AddHours(hoursFromFriday);

        EffectivePrice price = PriceResolver.Resolve(List, [weekend], asOf);

        Assert.Equal(Money.Of(expected, CurrencyCode.Usd), price.Amount);
    }

    [Fact]
    public void A_campaign_that_has_not_started_is_ignored()
    {
        SaleCampaign future = Campaign(Discount.PercentOff(20m), Monday, Monday.AddDays(3));

        Assert.False(PriceResolver.Resolve(List, [future], Saturday).IsOnSale);
    }

    [Fact]
    public void A_campaign_that_already_ended_is_ignored()
    {
        SaleCampaign past = Campaign(Discount.PercentOff(20m), Friday.AddDays(-14), Friday.AddDays(-7));

        Assert.False(PriceResolver.Resolve(List, [past], Saturday).IsOnSale);
    }

    [Fact]
    public void Campaigns_on_other_instants_do_not_leak_into_this_one()
    {
        SaleCampaign past = Campaign(Discount.PercentOff(90m), Friday.AddDays(-14), Friday.AddDays(-7), name: "Old");
        SaleCampaign future = Campaign(Discount.PercentOff(90m), Monday, Monday.AddDays(3), name: "Next");
        SaleCampaign now = Campaign(Discount.PercentOff(20m), Friday, Monday, name: "Now");

        EffectivePrice price = PriceResolver.Resolve(List, [past, future, now], Saturday);

        Assert.Same(now, price.Campaign);
        Assert.Equal(Money.Of(40.00m, CurrencyCode.Usd), price.Amount);
    }

    // ── Overlapping campaigns: the shopper gets the best price ────────────────────────────────

    [Fact]
    public void When_two_campaigns_overlap_the_lower_price_wins()
    {
        SaleCampaign storewide = Campaign(Discount.PercentOff(10m), Friday, Monday, name: "Storewide");
        SaleCampaign clearance = Campaign(Discount.AmountOff(20m), Friday, Monday, name: "Clearance");

        EffectivePrice price = PriceResolver.Resolve(List, [storewide, clearance], Saturday);

        // 10% off $50 is $45; $20 off $50 is $30. The shopper gets $30 — the only rule that is
        // defensible out loud and cannot be gamed by which row the database returned first.
        Assert.Same(clearance, price.Campaign);
        Assert.Equal(Money.Of(30.00m, CurrencyCode.Usd), price.Amount);
    }

    [Fact]
    public void The_winner_does_not_depend_on_the_order_the_campaigns_arrive_in()
    {
        SaleCampaign storewide = Campaign(Discount.PercentOff(10m), Friday, Monday, name: "Storewide");
        SaleCampaign clearance = Campaign(Discount.AmountOff(20m), Friday, Monday, name: "Clearance");

        Assert.Equal(
            PriceResolver.Resolve(List, [storewide, clearance], Saturday).Amount,
            PriceResolver.Resolve(List, [clearance, storewide], Saturday).Amount);
    }

    [Fact]
    public void Equally_good_campaigns_resolve_deterministically_to_the_older_one()
    {
        SaleCampaign older = Campaign(Discount.PercentOff(20m), Friday, Monday, Friday.AddDays(-9), "Older");
        SaleCampaign newer = Campaign(Discount.PercentOff(20m), Friday, Monday, Friday.AddDays(-2), "Newer");

        // Both land on $40, so the "strictly lower" test never prefers the second — the older one
        // holds. The rule matters less than the fact that it is a RULE: same inputs, same winner,
        // every request, so two shoppers never see two different sale badges on the same price.
        Assert.Same(older, PriceResolver.Resolve(List, [older, newer], Saturday).Campaign);
        Assert.Same(older, PriceResolver.Resolve(List, [newer, older], Saturday).Campaign);
    }

    [Fact]
    public void A_campaign_that_does_not_beat_the_list_price_is_not_a_sale()
    {
        // A 100%-off... of nothing. A free product cannot be discounted, so IsOnSale stays false and
        // the storefront shows no badge rather than "Save 0%".
        EffectivePrice price = PriceResolver.Resolve(
            Money.Zero(CurrencyCode.Usd),
            [Campaign(Discount.PercentOff(50m), Friday, Monday)],
            Saturday);

        Assert.False(price.IsOnSale);
        Assert.Equal(0, price.SavedPercent);
    }

    // ── The badge number ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(50.00, 20, 20)]
    [InlineData(34.50, 25, 25)]   // saves 8.62 of 34.50 = 24.98% → 25
    [InlineData(19.99, 10, 10)]   // saves 2.00 of 19.99 = 10.01% → 10
    public void SavedPercent_is_derived_from_the_amounts_not_the_campaign(
        decimal list, decimal percent, int expectedBadge)
    {
        EffectivePrice price = PriceResolver.Resolve(
            Money.Of(list, CurrencyCode.Usd),
            [Campaign(Discount.PercentOff(percent), Friday, Monday)],
            Saturday);

        Assert.Equal(expectedBadge, price.SavedPercent);
    }

    [Fact]
    public void SavedPercent_works_for_a_fixed_amount_campaign_which_has_no_percentage_at_all()
    {
        // The reason the badge is derived from the money: this campaign has no percentage to read.
        // One rule that is right for both kinds beats a special case per kind.
        EffectivePrice price = PriceResolver.Resolve(
            Money.Of(50.00m, CurrencyCode.Usd),
            [Campaign(Discount.AmountOff(12.50m), Friday, Monday)],
            Saturday);

        Assert.Equal(25, price.SavedPercent);
    }
}
