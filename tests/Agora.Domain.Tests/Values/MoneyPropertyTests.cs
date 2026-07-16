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
// 🔗 the same machinery returns in S11 for promotion math, where the stacking rules multiply the
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

    // ── Allocation: the properties that ARE the guarantee ─────────────────────────────────────

    [Fact]
    public void Distribute_never_invents_or_loses_a_minor_unit()
    {
        // ⭐ THE ONE THAT MATTERS. Every other property here is arithmetic hygiene; this is the
        // promise the rest of Agora leans on. A cart discount split across lines, a refund split
        // across an order, a tax apportionment — each is this call, and each is wrong forever if the
        // shares do not sum back to exactly what went in. Not "to within a cent". Exactly.
        Gen.Select(AnyMoney, AnyWeights)
            .Sample((amount, weights) => Assert.Equal(amount, Sum(amount.Distribute(weights))));
    }

    [Fact]
    public void Allocate_never_invents_or_loses_a_minor_unit()
    {
        Gen.Select(AnyMoney, Gen.Int[1, 50])
            .Sample((amount, parts) => Assert.Equal(amount, Sum(amount.Allocate(parts))));
    }

    [Fact]
    public void Allocated_shares_differ_by_at_most_one_minor_unit()
    {
        // "Equal as possible" made precise. Preserving the sum is easy if you are allowed to hand one
        // line the whole amount and the rest nothing — this is the property that forbids it, and the
        // two together pin the algorithm down to largest-remainder.
        Gen.Select(AnyPositiveMoney, Gen.Int[1, 50]).Sample((amount, parts) =>
        {
            IReadOnlyList<Money> shares = amount.Allocate(parts);

            Assert.True(shares.Max().Subtract(shares.Min()) <= Money.Of(0.01m, Usd));
        });
    }

    [Fact]
    public void Distribute_returns_one_share_per_weight()
    {
        Gen.Select(AnyMoney, AnyWeights)
            .Sample((amount, weights) => Assert.Equal(weights.Length, amount.Distribute(weights).Count));
    }

    [Fact]
    public void A_bigger_weight_never_gets_a_smaller_share()
    {
        // Monotonicity: the spare cents go to the largest remainders, so a line that is owed more
        // never ends up with less than one owed less. Violate this and an invoice is not wrong by a
        // penny — it is unexplainable to the customer looking at it.
        Gen.Select(AnyPositiveMoney, AnyWeights).Sample((amount, weights) =>
        {
            IReadOnlyList<Money> shares = amount.Distribute(weights);

            for (int i = 0; i < weights.Length; i++)
            {
                for (int j = 0; j < weights.Length; j++)
                {
                    if (weights[i] > weights[j])
                    {
                        Assert.True(shares[i] >= shares[j]);
                    }
                }
            }
        });
    }

    [Fact]
    public void Distributing_across_one_weight_returns_the_whole_amount()
    {
        Gen.Select(AnyMoney, Gen.Int[1, 1000])
            .Sample((amount, weight) => Assert.Equal([amount], amount.Distribute([weight])));
    }

    [Fact]
    public void Equal_weights_and_Allocate_agree()
    {
        // Two doors, one algorithm — Allocate is Distribute with a vector of ones, and the property
        // says so rather than a comment claiming it.
        Gen.Select(AnyMoney, Gen.Int[1, 20]).Sample((amount, parts) =>
            Assert.Equal(
                amount.Allocate(parts),
                amount.Distribute([.. Enumerable.Repeat(1, parts)])));
    }
}
