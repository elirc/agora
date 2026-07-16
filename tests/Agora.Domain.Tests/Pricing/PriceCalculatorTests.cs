using Agora.Domain;
using Agora.Domain.Pricing;
using Agora.Domain.Values;

namespace Agora.Domain.Tests.Pricing;

public class PriceCalculatorTests
{
    private static readonly CurrencyCode Usd = CurrencyCode.Usd;

    private static Money Usd_(decimal amount) => Money.Of(amount, Usd);

    // ── Discount construction ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100.01)]
    [InlineData(150)]
    public void A_percentage_outside_0_to_100_is_not_a_discount(decimal percent)
    {
        Assert.Throws<DomainRuleException>(() => Discount.PercentOff(percent));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_fixed_discount_must_be_positive(decimal amount)
    {
        Assert.Throws<DomainRuleException>(() => Discount.AmountOff(amount));
    }

    [Fact]
    public void A_discount_remembers_its_kind_and_value()
    {
        Assert.Equal(DiscountKind.PercentOff, Discount.PercentOff(15m).Kind);
        Assert.Equal(15m, Discount.PercentOff(15m).Value);
        Assert.Equal(DiscountKind.AmountOff, Discount.AmountOff(5m).Kind);
    }

    [Fact]
    public void An_unnamed_discount_kind_is_refused_rather_than_guessed()
    {
        // `default(Discount)` has Kind 0, which is no kind at all (the enum starts at 1). The switch
        // throws instead of silently treating it as 0% off — a struct's zero value must never be a
        // valid business case by accident.
        Assert.Throws<DomainRuleException>(() => PriceCalculator.Apply(default, Usd_(10m)));
    }

    // ── Percentage off ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(50.00, 20, 40.00)]
    [InlineData(19.99, 10, 17.99)]     // 17.991 → 17.99
    [InlineData(34.50, 25, 25.88)]     // 25.875 → tie → ToEven → 25.88
    [InlineData(100.00, 15, 85.00)]
    [InlineData(189.00, 30, 132.30)]
    [InlineData(22.00, 50, 11.00)]
    [InlineData(14.00, 5, 13.30)]
    [InlineData(18.50, 15, 15.72)]     // 15.725 → tie → ToEven → 15.72
    public void Percent_off_reduces_the_price(decimal list, decimal percent, decimal expected)
    {
        Assert.Equal(Usd_(expected), PriceCalculator.Apply(Discount.PercentOff(percent), Usd_(list)));
    }

    [Theory]
    [InlineData(20.00, 12.5, 17.50)]
    [InlineData(60.00, 33.33, 40.00)]  // 40.002 → 40.00
    [InlineData(48.00, 66.5, 16.08)]
    public void A_fractional_percentage_reduces_the_price_too(decimal list, decimal percent, decimal expected)
    {
        Assert.Equal(Usd_(expected), PriceCalculator.Apply(Discount.PercentOff(percent), Usd_(list)));
    }

    [Fact]
    public void One_hundred_percent_off_is_free_not_negative()
    {
        Assert.Equal(Money.Zero(Usd), PriceCalculator.Apply(Discount.PercentOff(100m), Usd_(34.50m)));
    }

    [Fact]
    public void Discounting_a_free_product_is_still_free()
    {
        Assert.Equal(Money.Zero(Usd), PriceCalculator.Apply(Discount.PercentOff(25m), Money.Zero(Usd)));
    }

    [Fact]
    public void The_discounted_price_keeps_the_products_currency()
    {
        CurrencyCode eur = CurrencyCode.From("EUR");

        Money discounted = PriceCalculator.Apply(Discount.PercentOff(10m), Money.Of(50m, eur));

        Assert.Equal(eur, discounted.Currency);
    }

    // ── Amount off ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(34.50, 5, 29.50)]
    [InlineData(19.99, 0.99, 19.00)]
    [InlineData(48.00, 48, 0.00)]
    public void Amount_off_subtracts_in_the_products_currency(decimal list, decimal off, decimal expected)
    {
        Assert.Equal(Usd_(expected), PriceCalculator.Apply(Discount.AmountOff(off), Usd_(list)));
    }

    [Fact]
    public void A_discount_bigger_than_the_price_clamps_at_zero()
    {
        // ⭐ the store may knock $5 off a $3 item; it may not end up OWING the shopper $2. Clamping
        // lives in the calculator, not in Money — because "not negative" is true of a shelf price and
        // false of money in general (a refund is negative money, and Money must be able to say so).
        Assert.Equal(Money.Zero(Usd), PriceCalculator.Apply(Discount.AmountOff(5m), Usd_(3m)));
    }
}
