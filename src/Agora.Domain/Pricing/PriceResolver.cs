using Agora.Domain.Values;

namespace Agora.Domain.Pricing;

/// <summary>
/// Resolves what a product costs at a given instant: the list price, unless a campaign covering that
/// instant beats it.
/// </summary>
public static class PriceResolver
{
    /// <summary>
    /// The price in force for <paramref name="listPrice"/> at <paramref name="asOf"/>, given every
    /// <paramref name="campaigns"/> candidate for the product.
    /// </summary>
    // 🔍 REVIEW LENS, AND THE SPRINT'S CENTRAL POINT: a sale price is a FUNCTION OF TIME. Read this
    // signature and notice what is missing — there is no clock in it. `asOf` is a parameter, so this
    // function cannot be "wrong on Tuesday": ask it about Tuesday and it answers about Tuesday.
    //
    // Now go and find EVERY place an instant enters this pipeline and name it out loud. There are
    // exactly two, and both are one layer up in Application/Pricing/ProductPricing.cs:
    //   1. `clock.GetUtcNow()` — the injected TimeProvider, read ONCE per request, so every product
    //      on a page is priced as of the same instant. (Read it per product and a page rendering
    //      across midnight prices the top half yesterday and the bottom half today.)
    //   2. `ISaleCampaignRepository.ListActiveForProductsAsync(..., asOf, ...)` — the same instant,
    //      handed to the database so the WHERE clause narrows to windows that cover it.
    // That is the entire list, and it should stay the entire list. When a third appears — a cached
    // page, a report, a background job — it will be a place where a *different* instant can leak in,
    // and the bug will be a price that is right for a moment nobody asked about.
    public static EffectivePrice Resolve(
        Money listPrice,
        IEnumerable<SaleCampaign> campaigns,
        DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(campaigns);

        Money best = listPrice;
        SaleCampaign? winner = null;

        // WHY the whole set and not "the first active one": campaigns can overlap (a category-wide
        // weekend sale and a clearance on one item), and once two apply the store needs a RULE, not
        // whatever order the database happened to return. The rule is "the shopper gets the best
        // price", which is the only one that is defensible out loud, cannot be gamed by insertion
        // order, and never has to be explained to a customer who saw a lower number a minute ago.
        // The ordering below makes ties deterministic: same inputs, same winner, every time.
        // 🔗 S11's promotion engine replaces "pick the lowest" with a real policy pipeline (stacking,
        // exclusivity, priorities). This is the seam it grows from — not a rules engine, on purpose.
        foreach (SaleCampaign campaign in campaigns
                     .Where(candidate => candidate.IsActiveAt(asOf))
                     .OrderBy(candidate => candidate.CreatedAtUtc)
                     .ThenBy(candidate => candidate.Id))
        {
            Money candidatePrice = PriceCalculator.Apply(campaign.Discount, listPrice);

            if (candidatePrice < best)
            {
                best = candidatePrice;
                winner = campaign;
            }
        }

        return winner is null ? EffectivePrice.OfList(listPrice) : new EffectivePrice(listPrice, best, winner);
    }
}
