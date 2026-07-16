using Agora.Application.Abstractions;
using Agora.Application.Catalog;
using Agora.Application.Pagination;
using Agora.Domain.Catalog;

namespace Agora.Application.Tests.Fakes;

/// <summary>
/// In-memory product store. Takes the categories too so it can resolve the ?category= slug
/// filter through the foreign key, exactly as the EF repository does via its EXISTS subquery.
/// Mirrors the S03 port: an explicit status filter on reads, a tracked-by-reference FindByIdAsync
/// so a mutate-then-"commit" flow works, and Add for creates.
/// </summary>
public sealed class InMemoryProductRepository(
    IEnumerable<Product> products,
    IEnumerable<Category> categories) : IProductRepository
{
    private readonly List<Product> _products = products.ToList();
    private readonly List<Category> _categories = categories.ToList();

    public Task<IReadOnlyList<Product>> ListAfterAsync(
        Slug? categorySlug,
        ProductStatusFilter status,
        TimelineCursor? after,
        int fetch,
        CancellationToken cancellationToken)
    {
        IEnumerable<Product> query = ApplyStatus(_products, status);

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

    public Task<Product?> FindBySlugAsync(Slug slug, ProductStatusFilter status, CancellationToken cancellationToken) =>
        Task.FromResult(ApplyStatus(_products, status).FirstOrDefault(p => p.Slug == slug));

    // Returns the live object reference — mutating it and committing (a no-op unit of work in tests)
    // reflects immediately, which is the in-memory stand-in for EF's change tracking.
    public Task<Product?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult<Product?>(_products.FirstOrDefault(p => p.Id == id));

    public Task<Product?> FindBySkuAsync(Sku sku, CancellationToken cancellationToken) =>
        Task.FromResult<Product?>(_products.FirstOrDefault(p => p.Sku == sku));

    public void Add(Product product) => _products.Add(product);

    private static IEnumerable<Product> ApplyStatus(IEnumerable<Product> source, ProductStatusFilter status) =>
        status switch
        {
            ProductStatusFilter.Active => source.Where(p => !p.IsArchived),
            ProductStatusFilter.Archived => source.Where(p => p.IsArchived),
            _ => source,
        };
}
