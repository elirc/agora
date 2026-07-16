using Agora.Application.Abstractions;
using Agora.Application.Pagination;
using Agora.Domain.Catalog;

namespace Agora.Application.Tests.Fakes;

/// <summary>
/// A pure in-memory category store — the port implemented without a database, so use-case
/// tests exercise the use case and nothing else. It mirrors the repository's newest-first
/// keyset contract exactly (that mirroring is what makes it a trustworthy stand-in).
/// </summary>
public sealed class InMemoryCategoryRepository(IEnumerable<Category> categories) : ICategoryRepository
{
    private readonly List<Category> _categories = categories.ToList();

    public Task<IReadOnlyList<Category>> ListAfterAsync(
        TimelineCursor? after,
        int fetch,
        CancellationToken cancellationToken)
    {
        IEnumerable<Category> ordered = _categories
            .OrderByDescending(c => c.CreatedAtUtc)
            .ThenByDescending(c => c.Id);

        if (after is { } cursor)
        {
            ordered = ordered.Where(c =>
                c.CreatedAtUtc < cursor.CreatedAtUtc
                || (c.CreatedAtUtc == cursor.CreatedAtUtc && c.Id.CompareTo(cursor.Id) < 0));
        }

        IReadOnlyList<Category> page = ordered.Take(fetch).ToList();
        return Task.FromResult(page);
    }

    public Task<Category?> FindBySlugAsync(Slug slug, CancellationToken cancellationToken) =>
        Task.FromResult<Category?>(_categories.FirstOrDefault(c => c.Slug == slug));
}
