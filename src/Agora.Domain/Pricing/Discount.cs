using Agora.Domain.Values;

namespace Agora.Domain.Pricing;

/// <summary>How a <see cref="Discount"/> reduces a price.</summary>
// WHY explicit values starting at 1: the enum is persisted as an int, so `default(DiscountKind)`
// must not silently mean a real kind. Zero is "nobody chose", and PriceCalculator refuses it.
public enum DiscountKind
{
    PercentOff = 1,
    AmountOff = 2,
}

/// <summary>
/// A reduction to apply to a price: either a percentage off or a fixed amount off. Exactly one of
/// the two, chosen at construction and immutable after.
/// </summary>
// 📘 this is a CLOSED, TINY union on purpose — two shapes, no plugin points, no strategy interface.
// Sprint 11 builds the real promotion engine (stacking rules, thresholds, coupons, free shipping)
// and it will not be this type; it will be a policy pipeline that adding a promo type extends
// without editing. Building that machinery now, for two cases, with no second caller and no rules to
// compose, would be predicting the shape of a problem we have not met — and predictions like that
// are how frameworks get built that the real requirement then has to fight.
// 🔗 So: S04's job is a per-product sale price, and the smallest thing that does that is an enum and
// a decimal. When S11 arrives, THIS type is the thing it subsumes, and the diff is the lesson.
public readonly record struct Discount
{
    public const decimal MaxPercent = 100m;

    public DiscountKind Kind { get; }

    /// <summary>The percentage (for <see cref="DiscountKind.PercentOff"/>) or the amount (for <see cref="DiscountKind.AmountOff"/>).</summary>
    public decimal Value { get; }

    private Discount(DiscountKind kind, decimal value)
    {
        Kind = kind;
        Value = value;
    }

    /// <summary>e.g. <c>Discount.PercentOff(15m)</c> — fifteen percent off the list price.</summary>
    public static Discount PercentOff(decimal percent)
    {
        // WHY 0 is excluded: a 0%-off campaign is not a sale, it is a no-op with a marketing budget.
        // Refusing it here means every campaign that exists actually changes a price, so the
        // storefront never has to reason about a "sale" that saves nothing.
        if (percent <= 0m || percent > MaxPercent)
        {
            throw new DomainRuleException(
                $"A percentage discount must be greater than 0 and at most {MaxPercent}.");
        }

        return new Discount(DiscountKind.PercentOff, percent);
    }

    /// <summary>e.g. <c>Discount.AmountOff(5m)</c> — five (of the product's currency) off the list price.</summary>
    // WHY a bare decimal and not a Money: the amount is only ever subtracted from ONE product's list
    // price, so its currency is not a free choice — it is whatever that product is priced in. Storing
    // a currency here would create a second source of truth that could disagree with the product's,
    // and the only thing to do about a disagreement would be to reject it. PriceCalculator therefore
    // reads the currency off the price it is discounting, and a mismatch is unrepresentable rather
    // than validated. (ADR-0006 records the multi-currency version of this decision: the day a
    // product carries more than one price, the discount carries a Money and Money's own currency gate
    // does the refusing.)
    public static Discount AmountOff(decimal amount)
    {
        if (amount <= 0m)
        {
            throw new DomainRuleException("A fixed discount must be greater than zero.");
        }

        return new Discount(DiscountKind.AmountOff, amount);
    }

    public override string ToString() => Kind switch
    {
        DiscountKind.PercentOff => $"{Value}% off",
        DiscountKind.AmountOff => $"{Value} off",
        _ => "no discount",
    };
}
