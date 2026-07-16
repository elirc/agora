using Agora.Application.Abstractions;
using Agora.Application.Pagination;
using Agora.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Persistence.Repositories;

internal sealed class ProductRepository(AgoraDbContext db) : IProductRepository
{
    public async Task<IReadOnlyList<Product>> ListAfterAsync(
        Slug? categorySlug,
        TimelineCursor? after,
        int fetch,
        CancellationToken cancellationToken)
    {
        IQueryable<Product> query = db.Products.AsNoTracking();

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

    public Task<Product?> FindBySlugAsync(Slug slug, CancellationToken cancellationToken) =>
        db.Products.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Slug == slug, cancellationToken);
}
