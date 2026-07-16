using Agora.Application.Abstractions;
using Agora.Application.Errors;
using Agora.Domain.Catalog;

namespace Agora.Application.Catalog;

public sealed class GetProduct(IProductRepository products)
{
    public async Task<Product> ExecuteAsync(string rawSlug, CancellationToken cancellationToken)
    {
        Slug slug = Slug.From(rawSlug);

        Product? product = await products.FindBySlugAsync(slug, cancellationToken);

        return product ?? throw new NotFoundException("product", slug);
    }
}
