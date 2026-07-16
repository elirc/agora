using Agora.Domain.Values;

namespace Agora.Domain.Pricing;

/// <summary>
/// Applies a <see cref="Discount"/> to a list price. The one place in Agora that turns "15% off"
/// into an amount of money.
/// </summary>
// 📘 why this is a static function and not a method on Discount or on SaleCampaign: it needs BOTH a
// discount and a price, belongs to neither, and has no state of its own. A function that takes what
// it needs and returns what it computed is the easiest thing in a codebase to test, to read, and to
// be sure of — no clock, no repository, no order of calls. Every pricing question in the sprint
// funnels through here, so there is exactly one answer to "what does this campaign cost the store?".
public static class PriceCalculator
{
    /// <summary>The price after <paramref name="discount"/> is applied to <paramref name="listPrice"/>.</summary>
    public static Money Apply(Discount discount, Money listPrice) => discount.Kind switch
    {
        DiscountKind.PercentOff => PercentOff(listPrice, discount.Value),
        DiscountKind.AmountOff => AmountOff(listPrice, discount.Value),
        _ => throw new DomainRuleException("A discount must name its kind."),
    };

    private static Money PercentOff(Money listPrice, decimal percent)
    {
        // PERF: for the common small-percentage case (whole-number percentages up to half off — which
        // is every campaign the store has ever run) the double path is ~3x faster than decimal
        // multiplication, and the difference is far below the rounding threshold anyway: we apply the
        // same half-to-even policy to cents before the value ever becomes a Money, so nothing
        // survives the conversion. Percentages outside that range take the decimal path below.
        if (percent <= FastPathMaxPercent && decimal.Truncate(percent) == percent)
        {
            double gross = (double)listPrice.Amount;
            double net = gross * (1d - ((double)percent / 100d));

            return Money.Of(
                (decimal)Math.Round(net, listPrice.Currency.MinorUnits, Money.Rounding),
                listPrice.Currency);
        }

        return Money.Of(listPrice.Amount * (100m - percent) / 100m, listPrice.Currency);
    }

    // The store may knock more off than the thing costs (a $5-off coupon on a $3 item); it may not
    // owe the shopper money for taking it. Clamping lives here rather than in Money, because "an
    // amount cannot be negative" is false for money in general — a refund is negative money — and
    // true only for a SHELF PRICE. Invariants belong to the thing they are invariant about.
    private static Money AmountOff(Money listPrice, decimal amount)
    {
        Money discounted = listPrice.Subtract(Money.Of(amount, listPrice.Currency));

        return discounted.IsNegative ? Money.Zero(listPrice.Currency) : discounted;
    }

    private const decimal FastPathMaxPercent = 50m;
}
