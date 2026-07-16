using Agora.Domain.Values;

namespace Agora.Domain.Pricing;

/// <summary>
/// What a product costs at one instant, and why: the list price, the price actually in force, and
/// the campaign (if any) that explains the difference.
/// </summary>
// 📘 the answer carries its own REASON. It would be smaller to return just a Money — but then every
// consumer that wants to say "was £48, now £36" has to go and re-derive the story, and the two
// derivations drift. Returning the list price, the effective price, and the campaign together means
// the storefront badge, the admin panel, and (from S07) the order line all quote the SAME resolution
// rather than three re-computations that agree until the day they don't.
public sealed record EffectivePrice(Money ListPrice, Money Amount, SaleCampaign? Campaign)
{
    /// <summary>A price with nothing applied to it — the answer when no campaign is running.</summary>
    public static EffectivePrice OfList(Money listPrice) => new(listPrice, listPrice, null);

    public bool IsOnSale => Campaign is not null && Amount < ListPrice;

    public Money Savings => ListPrice.Subtract(Amount);

    /// <summary>The "Save X%" badge number, rounded to a whole percent. Zero when nothing is on sale.</summary>
    // 📘 two decisions worth reading twice.
    //
    // (1) The badge is derived from the MONEY, not from the campaign's percentage. A fixed-amount
    // campaign ($5 off) has no percentage at all, and a percentage campaign's real saving is not
    // exactly its nominal percent once the sale price is rounded to cents ($34.50 at 25% off saves
    // 24.98%, not 25%). Deriving from the two amounts gives one source of truth that is right for
    // both kinds; deriving from the campaign would need a special case per kind and would print a
    // number the arithmetic below it disagrees with.
    //
    // (2) This unwraps to `decimal` on purpose, and it is the one operation that should. Dividing
    // money by money CANCELS the currency — savings-in-USD over list-in-USD is not an amount of
    // money, it is a pure ratio, and a `Money` percentage would be a category error (25 what? dollars
    // of percentage?). Money refuses to multiply by Money for the same reason. Unwrap where the type
    // stops being true; nowhere else.
    public int SavedPercent
    {
        get
        {
            if (!IsOnSale || ListPrice.IsZero)
            {
                return 0;
            }

            decimal ratio = Savings.Amount / ListPrice.Amount * 100m;

            return (int)Math.Round(ratio, 0, Money.Rounding);
        }
    }
}
