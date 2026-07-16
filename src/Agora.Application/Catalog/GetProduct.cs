using Agora.Application.Abstractions;
using Agora.Application.Errors;
using Agora.Application.Pricing;
using Agora.Domain.Catalog;

namespace Agora.Application.Catalog;

public sealed class GetProduct(IProductRepository products, ProductPricing pricing)
{
    public async Task<PricedProduct> ExecuteAsync(string rawSlug, CancellationToken cancellationToken)
    {
        Slug slug = Slug.From(rawSlug);

        // WHY Active: an archived product is a 404 to a shopper, not a 200 with a hidden banner.
        // The storefront must not even confirm it exists — same lookup the list uses, one status.
        Product? product = await products.FindBySlugAsync(slug, ProductStatusFilter.Active, cancellationToken);

        if (product is null)
        {
            throw new NotFoundException("product", slug);
        }

        // The grid and the product page resolve through the SAME query object, so a shopper cannot
        // see one price on the card and a different one after clicking it.
        return await pricing.PriceAsync(product, cancellationToken);
    }
}
