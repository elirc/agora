using Agora.Application.Abstractions;
using Agora.Application.Errors;
using Agora.Application.Pagination;
using Agora.Domain.Catalog;

namespace Agora.Application.Catalog;

/// <summary>Newest-first page of storefront categories.</summary>
// LEARN: one class per use case reads heavier than a "CatalogService" grab bag — on
// purpose. A use case's constructor lists exactly what that operation needs, its test
// file tests exactly one behavior, and deleting a feature deletes a file.
public sealed class ListCategories(ICategoryRepository categories)
{
    public async Task<Page<Category>> ExecuteAsync(
        string? cursor,
        int? limit,
        CancellationToken cancellationToken)
    {
        int pageSize = limit ?? Page<Category>.DefaultLimit;

        // WHY: reject, don't clamp. A silently-clamped limit=5000 teaches the client that
        // 5000 "works" — until the data grows and their un-exercised pagination loop breaks
        // in production. Explicit contracts fail fast on both sides (ADR-0002).
        if (pageSize < 1 || pageSize > Page<Category>.MaxLimit)
        {
            throw new RequestValidationException(
                $"limit must be between 1 and {Page<Category>.MaxLimit}.");
        }

        TimelineCursor? after = cursor is null ? null : TimelineCursor.Decode(cursor);

        // LEARN: fetch limit+1 — the classic "is there a next page?" probe. The extra row
        // is evidence, not payload; it never leaves this method.
        IReadOnlyList<Category> fetched =
            await categories.ListAfterAsync(after, pageSize + 1, cancellationToken);

        return PageOf(fetched, pageSize);
    }

    private static Page<Category> PageOf(IReadOnlyList<Category> fetched, int pageSize)
    {
        bool hasMore = fetched.Count > pageSize;
        IReadOnlyList<Category> items = hasMore ? [.. fetched.Take(pageSize)] : fetched;

        string? next = hasMore
            ? new TimelineCursor(items[^1].CreatedAtUtc, items[^1].Id).Encode()
            : null;

        return new Page<Category>(items, next);
    }
}
