using Agora.Application.Abstractions;
using Agora.Application.Errors;
using Agora.Domain.Catalog;

namespace Agora.Application.Catalog;

public sealed class GetProduct(IProductRepository products)
{
    public async Task<Product> ExecuteAsync(string rawSlug, CancellationToken cancellationToken)
    {
        Slug slug = Slug.From(rawSlug);

        // WHY Active: an archived product is a 404 to a shopper, not a 200 with a hidden banner.
        // The storefront must not even confirm it exists — same lookup the list uses, one status.
        Product? product = await products.FindBySlugAsync(slug, ProductStatusFilter.Active, cancellationToken);

        return product ?? throw new NotFoundException("product", slug);
    }
}
