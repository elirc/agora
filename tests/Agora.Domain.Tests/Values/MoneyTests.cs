using Agora.Domain;
using Agora.Domain.Values;

namespace Agora.Domain.Tests.Values;

public class MoneyTests
{
    private static readonly CurrencyCode Usd = CurrencyCode.Usd;
    private static readonly CurrencyCode Eur = CurrencyCode.From("EUR");

    // ── Construction & the rounding policy ───────────────────────────────────────────────────

    [Fact]
    public void Of_keeps_an_exact_amount_exactly()
    {
        Assert.Equal(19.99m, Money.Of(19.99m, Usd).Amount);
    }

    [Theory]
    // Banker's rounding: ties go to the EVEN cent, not always up. These four rows ARE the policy —
    // if someone changes MidpointRounding, this is what goes red, and the diff is a decision.
    [InlineData("0.125", "0.12")]  // tie → 2 is even → down
    [InlineData("0.135", "0.14")]  // tie → 4 is even → up
    [InlineData("0.145", "0.14")]  // tie → 4 is even → down
    [InlineData("0.155", "0.16")]  // tie → 6 is even → up
    // And the ordinary, non-tie cases, which round the way everyone expects.
    [InlineData("0.124", "0.12")]
    [InlineData("0.126", "0.13")]
    public void Of_rounds_to_the_currencys_minor_units_by_the_policy(string input, string expected)
    {
        Money money = Money.Of(decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture), Usd);

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), money.Amount);
    }

    [Fact]
    public void Of_is_idempotent_because_every_money_is_already_at_scale()
    {
        Money once = Money.Of(1.005m, Usd);
        Money twice = Money.Of(once.Amount, Usd);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Of_rejects_a_currency_that_is_not_a_currency()
    {
        Assert.Throws<DomainRuleException>(() => Money.Of(1m, "dollars"));
        Assert.Throws<DomainRuleException>(() => Money.Of(1m, ""));
        Assert.Throws<DomainRuleException>(() => Money.Of(1m, (string?)null));
    }

    [Fact]
    public void A_default_currency_refuses_to_pretend_it_is_one()
    {
        // GOTCHA: `default(Money)` is the hole no struct can close — C# hands one out without running
        // a constructor. It fails loudly at the first read rather than silently behaving as $0.00 of
        // nothing, which would compare unequal to every real currency and produce a mystifying
        // "cannot combine  and USD" three layers away.
        Assert.Throws<DomainRuleException>(() => default(Money).Currency.Code);
    }

    // ── Currency as identity ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Amounts_in_different_currencies_are_not_equal_even_at_the_same_number()
    {
        Assert.NotEqual(Money.Of(5m, Usd), Money.Of(5m, Eur));
    }

    [Fact]
    public void Adding_two_currencies_refuses_rather_than_inventing_an_answer()
    {
        // TRY: this is the compile-adjacent lesson from the sprint's exercises. `usd + eur` has no
        // answer without a rate and a moment — so the type says so, at the line that asked, naming
        // both currencies. A loose `decimal` would have returned 10 and shipped it to a customer.
        DomainRuleException error = Assert.Throws<DomainRuleException>(
            () => Money.Of(5m, Usd) + Money.Of(5m, Eur));

        Assert.Contains("USD", error.Message, StringComparison.Ordinal);
        Assert.Contains("EUR", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Subtracting_and_comparing_across_currencies_refuse_too()
    {
        Assert.Throws<DomainRuleException>(() => Money.Of(5m, Usd) - Money.Of(1m, Eur));
        Assert.Throws<DomainRuleException>(() => Money.Of(5m, Usd) < Money.Of(9m, Eur));
    }

    // ── Arithmetic ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Add_and_subtract_are_exact()
    {
        Money total = Money.Of(0.10m, Usd)
            .Add(Money.Of(0.20m, Usd))
            .Add(Money.Of(0.30m, Usd));

        // 📘 the whole reason this type exists, in one assertion. In `double`, 0.1 + 0.2 + 0.3 is
        // 0.6000000000000001 — because none of the three is representable in base 2. In decimal each
        // one is exact, and so is the sum. There is nothing clever happening here; that is the point.
        Assert.Equal(Money.Of(0.60m, Usd), total);
        Assert.Equal(Money.Of(0.30m, Usd), total.Subtract(Money.Of(0.30m, Usd)));
    }

    [Fact]
    public void Ten_dimes_are_exactly_one_dollar()
    {
        Money total = Enumerable.Range(0, 10)
            .Aggregate(Money.Zero(Usd), (sum, _) => sum.Add(Money.Of(0.10m, Usd)));

        Assert.Equal(Money.Of(1.00m, Usd), total);
    }

    [Fact]
    public void Multiply_scales_and_rounds_by_the_policy()
    {
        Assert.Equal(Money.Of(59.97m, Usd), Money.Of(19.99m, Usd).Multiply(3m));

        // 19.99 × 1.5 = 29.985 — a tie at the cent. ToEven sends it to the even 8.
        Assert.Equal(Money.Of(29.98m, Usd), Money.Of(19.99m, Usd).Multiply(1.5m));
    }

    [Fact]
    public void Negate_and_IsNegative_describe_money_that_flows_the_other_way()
    {
        Money refund = Money.Of(19.99m, Usd).Negate();

        Assert.True(refund.IsNegative);
        Assert.Equal(Money.Zero(Usd), refund.Add(Money.Of(19.99m, Usd)));
    }

    [Fact]
    public void Comparison_orders_amounts_in_one_currency()
    {
        Assert.True(Money.Of(1m, Usd) < Money.Of(2m, Usd));
        Assert.True(Money.Of(2m, Usd) >= Money.Of(2m, Usd));
        Assert.Equal(Money.Of(2m, Usd), new[] { Money.Of(5m, Usd), Money.Of(2m, Usd) }.Min());
    }

    [Fact]
    public void ToString_shows_the_amount_at_scale_and_its_currency()
    {
        Assert.Equal("19.90 USD", Money.Of(19.9m, Usd).ToString());
    }
}
