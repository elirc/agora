using Agora.Domain.Values;
using CsCheck;

namespace Agora.Domain.Tests.Values;

/// <summary>
/// Property-based tests for Money: the laws its arithmetic and allocation must obey for EVERY
/// amount, not just the ones an author thought to type.
/// </summary>
// 📘 WHAT A PROPERTY TEST IS FOR, AND WHY MONEY IS THE PLACE TO SPEND ONE.
// An example test says "10.00 split three ways is 3.34/3.33/3.33" — it proves the case you already
// had in your head, which is by construction the case you already got right. A PROPERTY says "for
// EVERY amount and EVERY set of weights, the shares sum back to the amount", and CsCheck then spends
// a few thousand attempts trying to make you a liar: adversarial cents, 3.33 vs 3.335, one line, a
// hundred lines, zero weights, wildly lopsided weights, negative amounts. When it finds a
// counterexample it SHRINKS it to the smallest one that still fails and prints the seed, so the
// failure arrives as "here is the two-line case that breaks you", reproducible forever.
//
// Money earns this and most code does not. The bugs here are (a) rare, (b) silent, and (c) about
// money — a penny that vanishes on one invoice in ten thousand is invisible in review, invisible in
// staging, and extremely visible in the finance team's reconciliation. That combination is exactly
// what properties are good at and examples are bad at.
// cases far past what anyone can enumerate by hand.
public class MoneyPropertyTests
{
    private static readonly CurrencyCode Usd = CurrencyCode.Usd;

    // WHY generate CENTS (a long) and divide, rather than generating a decimal: a raw decimal
    // generator wanders into 28-digit values that no shop has ever charged, so it would spend its
    // budget on arithmetic nobody performs. Cents in ±$10M is the space real prices, discounts and
    // refunds live in — and it hits the adversarial parts of it (odd cents, half-cent inputs after a
    // multiply, zero, negatives) far harder than a hand-written [InlineData] list ever would.
    private static readonly Gen<Money> AnyMoney =
        Gen.Long[-1_000_000_000L, 1_000_000_000L].Select(cents => Money.Of(cents / 100m, Usd));

    private static readonly Gen<Money> AnyPositiveMoney =
        Gen.Long[0L, 1_000_000_000L].Select(cents => Money.Of(cents / 100m, Usd));

    // 1–20 lines, weights 0–1000: a cart, an order, a refund split across lines.
    private static readonly Gen<int[]> AnyWeights =
        Gen.Int[0, 1000].Array[1, 20].Where(weights => weights.Sum() > 0);

    private static Money Sum(IEnumerable<Money> money) =>
        money.Aggregate(Money.Zero(Usd), (total, next) => total.Add(next));

    // ── Arithmetic laws ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Addition_is_commutative()
    {
        Gen.Select(AnyMoney, AnyMoney)
            .Sample((a, b) => Assert.Equal(a.Add(b), b.Add(a)));
    }

    [Fact]
    public void Addition_is_associative()
    {
        // 📘 THE LAW THAT `double` BREAKS. In binary floating point (a + b) + c ≠ a + (b + c) for
        // ordinary money — the rounding error depends on the ORDER you added things in, so a total
        // depends on how the rows happened to be sorted. In decimal, at the currency's scale, this
        // law simply holds, which is why nobody downstream has to care about ordering.
        Gen.Select(AnyMoney, AnyMoney, AnyMoney)
            .Sample((a, b, c) => Assert.Equal(a.Add(b).Add(c), a.Add(b.Add(c))));
    }

    [Fact]
    public void Zero_is_the_identity_and_subtraction_undoes_addition()
    {
        Gen.Select(AnyMoney, AnyMoney).Sample((a, b) =>
        {
            Assert.Equal(a, a.Add(Money.Zero(Usd)));
            Assert.Equal(a, a.Add(b).Subtract(b));
        });
    }

    [Fact]
    public void Negation_is_its_own_inverse_and_cancels()
    {
        AnyMoney.Sample(a =>
        {
            Assert.Equal(a, a.Negate().Negate());
            Assert.Equal(Money.Zero(Usd), a.Add(a.Negate()));
        });
    }

    [Fact]
    public void Multiplying_by_one_changes_nothing_and_by_zero_yields_zero()
    {
        AnyMoney.Sample(a =>
        {
            Assert.Equal(a, a.Multiply(1m));
            Assert.Equal(Money.Zero(Usd), a.Multiply(0m));
        });
    }

    [Fact]
    public void Every_money_is_already_at_its_currencys_scale()
    {
        // The `Of` invariant, asserted for the whole generated space: no instance exists carrying
        // fractional cents, so nothing downstream ever has to round defensively "just in case".
        AnyMoney.Sample(a => Assert.Equal(a.Amount, Math.Round(a.Amount, Usd.MinorUnits)));
    }

    [Fact]
    public void Comparison_is_a_total_order_consistent_with_equality()
    {
        Gen.Select(AnyMoney, AnyMoney).Sample((a, b) =>
        {
            Assert.Equal(a == b, a.CompareTo(b) == 0);
            Assert.Equal(a < b, b > a);
            Assert.True(a <= b || b <= a);
        });
    }
}
