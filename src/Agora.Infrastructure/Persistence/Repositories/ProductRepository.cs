using Agora.Application.Abstractions;
using Agora.Application.Catalog;
using Agora.Application.Pagination;
using Agora.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Persistence.Repositories;

internal sealed class ProductRepository(AgoraDbContext db) : IProductRepository
{
    public async Task<IReadOnlyList<Product>> ListAfterAsync(
        Slug? categorySlug,
        ProductStatusFilter status,
        TimelineCursor? after,
        int fetch,
        CancellationToken cancellationToken)
    {
        IQueryable<Product> query = ApplyStatus(db.Products.AsNoTracking(), status);

        if (categorySlug is { } slug)
        {
            // WHY: filter by the category's SLUG through the foreign key, without a navigation
            // property — EF turns this into an EXISTS subquery over the categories table. The
            // storefront filters by the slug it already has in the URL; no extra round trip to
            // resolve slug → id first.
            query = query.Where(p =>
                db.Categories.Any(c => c.Id == p.CategoryId && c.Slug == slug));
        }

        query = query
            .OrderByDescending(p => p.CreatedAtUtc)
            .ThenByDescending(p => p.Id);

        if (after is { } cursor)
        {
            query = query.Where(p =>
                p.CreatedAtUtc < cursor.CreatedAtUtc
                || (p.CreatedAtUtc == cursor.CreatedAtUtc
                    && p.Id.CompareTo(cursor.Id) < 0));
        }

        return await query.Take(fetch).ToListAsync(cancellationToken);
    }

    public Task<Product?> FindBySlugAsync(Slug slug, ProductStatusFilter status, CancellationToken cancellationToken) =>
        ApplyStatus(db.Products.AsNoTracking(), status)
            .SingleOrDefaultAsync(p => p.Slug == slug, cancellationToken);

    // WHY no AsNoTracking here (unlike every read above): admin writes fetch, mutate, and commit the
    // SAME entity, so it must be tracked for SaveChanges to see the change — and for EF's concurrency
    // token on Version to guard the update. This is the one product query that leads to a write.
    public Task<Product?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Product?> FindBySkuAsync(Sku sku, CancellationToken cancellationToken) =>
        db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Sku == sku, cancellationToken);

    public void Add(Product product) => db.Products.Add(product);

    // GOTCHA: filtering on ArchivedAtUtc is a NULL check (IS NULL / IS NOT NULL), which SQLite runs
    // fine — unlike ORDER BY on a DateTimeOffset, which it cannot. So archive filtering needs no
    // special converter treatment; only the ordering columns did (see AgoraDbContext).
    private static IQueryable<Product> ApplyStatus(IQueryable<Product> query, ProductStatusFilter status) =>
        status switch
        {
            ProductStatusFilter.Active => query.Where(p => p.ArchivedAtUtc == null),
            ProductStatusFilter.Archived => query.Where(p => p.ArchivedAtUtc != null),
            ProductStatusFilter.All => query,
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
}
