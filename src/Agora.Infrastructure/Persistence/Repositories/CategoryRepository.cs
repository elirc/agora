using Agora.Application.Abstractions;
using Agora.Application.Pagination;
using Agora.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Persistence.Repositories;

internal sealed class CategoryRepository(AgoraDbContext db) : ICategoryRepository
{
    public async Task<IReadOnlyList<Category>> ListAfterAsync(
        TimelineCursor? after,
        int fetch,
        CancellationToken cancellationToken)
    {
        IQueryable<Category> query = db.Categories.AsNoTracking()
            .OrderByDescending(c => c.CreatedAtUtc)
            .ThenByDescending(c => c.Id);

        if (after is { } cursor)
        {
            // WHY: strict keyset predicate — everything strictly after the cursor in
            // (CreatedAtUtc DESC, Id DESC) order. The tiebreak arm is what makes two categories
            // created in the same tick impossible to skip or duplicate.
            query = query.Where(c =>
                c.CreatedAtUtc < cursor.CreatedAtUtc
                || (c.CreatedAtUtc == cursor.CreatedAtUtc
                    && c.Id.CompareTo(cursor.Id) < 0));
        }

        return await query.Take(fetch).ToListAsync(cancellationToken);
    }

    public Task<Category?> FindBySlugAsync(Slug slug, CancellationToken cancellationToken) =>
        db.Categories.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Slug == slug, cancellationToken);

    // Tracked (no AsNoTracking): a rename mutates and commits this entity, and its Version is the
    // concurrency token EF checks at SaveChanges.
    public Task<Category?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Categories.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public void Add(Category category) => db.Categories.Add(category);
}
