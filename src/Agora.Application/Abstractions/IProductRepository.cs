using Agora.Application.Pagination;
using Agora.Domain.Catalog;

namespace Agora.Application.Abstractions;

public interface IProductRepository
{
    /// <summary>
    /// Newest-first page, optionally scoped to one category by slug. <paramref name="after"/>
    /// is an exclusive lower bound in (CreatedAtUtc DESC, Id DESC) order; callers over-fetch
    /// by one to detect a next page.
    /// </summary>
    Task<IReadOnlyList<Product>> ListAfterAsync(
        Slug? categorySlug,
        TimelineCursor? after,
        int fetch,
        CancellationToken cancellationToken);

    Task<Product?> FindBySlugAsync(Slug slug, CancellationToken cancellationToken);
}
