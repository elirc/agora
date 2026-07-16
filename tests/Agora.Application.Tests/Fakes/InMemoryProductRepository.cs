using Agora.Application.Abstractions;
using Agora.Application.Pagination;
using Agora.Domain.Catalog;

namespace Agora.Application.Tests.Fakes;

/// <summary>
/// In-memory product store. Takes the categories too so it can resolve the ?category= slug
/// filter through the foreign key, exactly as the EF repository does via its EXISTS subquery.
/// </summary>
public sealed class InMemoryProductRepository(
    IEnumerable<Product> products,
    IEnumerable<Category> categories) : IProductRepository
{
    private readonly List<Product> _products = products.ToList();
    private readonly List<Category> _categories = categories.ToList();

    public Task<IReadOnlyList<Product>> ListAfterAsync(
        Slug? categorySlug,
        TimelineCursor? after,
        int fetch,
        CancellationToken cancellationToken)
    {
        IEnumerable<Product> query = _products;

        if (categorySlug is { } slug)
        {
            HashSet<Guid> ids = _categories
                .Where(c => c.Slug == slug)
                .Select(c => c.Id)
                .ToHashSet();

            query = query.Where(p => ids.Contains(p.CategoryId));
        }

        IEnumerable<Product> ordered = query
            .OrderByDescending(p => p.CreatedAtUtc)
            .ThenByDescending(p => p.Id);

        if (after is { } cursor)
        {
            ordered = ordered.Where(p =>
                p.CreatedAtUtc < cursor.CreatedAtUtc
                || (p.CreatedAtUtc == cursor.CreatedAtUtc && p.Id.CompareTo(cursor.Id) < 0));
        }

        IReadOnlyList<Product> page = ordered.Take(fetch).ToList();
        return Task.FromResult(page);
    }

    public Task<Product?> FindBySlugAsync(Slug slug, CancellationToken cancellationToken) =>
        Task.FromResult<Product?>(_products.FirstOrDefault(p => p.Slug == slug));
}
