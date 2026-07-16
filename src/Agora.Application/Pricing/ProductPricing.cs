using Agora.Application.Abstractions;
using Agora.Domain.Catalog;
using Agora.Domain.Pricing;

namespace Agora.Application.Pricing;

/// <summary>
/// The price-resolution query: turns products into priced products by asking, once, what time it is
/// and which campaigns cover that instant.
/// </summary>
// 🔍 THIS CLASS IS WHERE TIME ENTERS THE PIPELINE, and it is the only place it may. Everything below
// it (PriceResolver, SaleCampaign.IsActiveAt, DateRange.Contains) takes the instant as an argument
// and is therefore pinnable in a test forever; everything above it (endpoints, DTOs) consumes an
// answer that was already resolved. This is the seam — one class, one `clock.GetUtcNow()`, named out
// loud so a reviewer can find it by looking rather than by grepping.
// 📘 why a shared query object rather than the logic inlined into ListProducts and GetProduct: the
// storefront list, the product page, and (from S05) the cart all need the same answer, and three
// copies of "read the clock, fetch campaigns, resolve" is three chances to read the clock differently.
// It is not a use case — it is a collaborator use cases lean on — so it takes ports, has no
// authorization of its own, and returns domain values rather than DTOs.
public sealed class ProductPricing(ISaleCampaignRepository campaigns, TimeProvider clock)
{
    /// <summary>Prices a page of products as of one instant.</summary>
    public async Task<IReadOnlyList<PricedProduct>> PriceAsync(
        IReadOnlyList<Product> products,
        CancellationToken cancellationToken)
    {
        if (products.Count == 0)
        {
            return [];
        }

        // ⚠️ READ THE CLOCK ONCE, HERE, AND PASS THE VALUE DOWN. Not once per product — once per
        // request. A page that read `GetUtcNow()` per row would price the top of the grid at 23:59:59
        // and the bottom at 00:00:00 the night a campaign starts, and ship a page where two products
        // in the same sale disagree about whether it has begun. It would also be untestable in the
        // only way that matters: you could never assert "AT this instant, the page looks like THIS",
        // because there would be no single instant the page was about. One read; one answer; one
        // moment the whole response is true for.
        DateTimeOffset asOf = clock.GetUtcNow();

        Guid[] productIds = [.. products.Select(product => product.Id)];

        // WHY one query for the whole page rather than one per product: the classic N+1, and it is
        // cheaper to not write it than to find it in S14.
        IReadOnlyList<SaleCampaign> active =
            await campaigns.ListActiveForProductsAsync(productIds, asOf, cancellationToken);

        ILookup<Guid, SaleCampaign> byProduct = active.ToLookup(campaign => campaign.ProductId);

        // The instant rides along in every answer (S05 added `AsOf`). It costs one field and it means
        // a caller that needs to know WHEN a price was true — a cart recording what it quoted — does
        // not have to go and ask a clock a second time to find out.
        return [.. products.Select(product => new PricedProduct(
            product,
            PriceResolver.Resolve(product.Price, byProduct[product.Id], asOf),
            asOf))];
    }

    /// <summary>Prices a single product.</summary>
    // WHY this delegates instead of having its own two-line body: one code path, so the product page
    // and the grid can never resolve a price differently. The list of one is free; the divergence it
    // prevents is not.
    public async Task<PricedProduct> PriceAsync(Product product, CancellationToken cancellationToken)
    {
        IReadOnlyList<PricedProduct> priced = await PriceAsync([product], cancellationToken);

        return priced[0];
    }
}
