using Agora.Application.Pagination;
using Agora.Domain.Catalog;

namespace Agora.Application.Abstractions;

// LEARN: this is a PORT — an interface the Application owns and the Infrastructure
// implements. The use case depends on this shape, never on EF Core. Swap the database
// and this contract does not move; that is the whole point of the hexagon (ADR-0001).
public interface ICategoryRepository
{
    /// <summary>
    /// Newest-first page. <paramref name="after"/> is an exclusive lower bound in
    /// (CreatedAtUtc DESC, Id DESC) order; callers over-fetch by one to detect a next page.
    /// </summary>
    // WHY no status filter here (unlike products): categories are not archived in S03. A category
    // with no active products is harmless; cascading an archive across a category is a bigger
    // decision left for later. See ADR-0005.
    Task<IReadOnlyList<Category>> ListAfterAsync(
        TimelineCursor? after,
        int fetch,
        CancellationToken cancellationToken);

    Task<Category?> FindBySlugAsync(Slug slug, CancellationToken cancellationToken);

    /// <summary>
    /// Look up a category by id for an admin write — TRACKED so a rename persists and the
    /// optimistic-concurrency check on <see cref="Category.Version"/> runs at commit.
    /// </summary>
    Task<Category?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    void Add(Category category);
}
