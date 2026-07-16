using Agora.Application.Abstractions;
using Agora.Application.Errors;
using Agora.Application.Pagination;
using Agora.Domain.Catalog;

namespace Agora.Application.Catalog;

/// <summary>Newest-first page of products, optionally filtered to one category by slug.</summary>
public sealed class ListProducts(IProductRepository products)
{
    public async Task<Page<Product>> ExecuteAsync(
        string? category,
        string? cursor,
        int? limit,
        CancellationToken cancellationToken)
    {
        int pageSize = limit ?? Page<Product>.DefaultLimit;

        if (pageSize < 1 || pageSize > Page<Product>.MaxLimit)
        {
            throw new RequestValidationException(
                $"limit must be between 1 and {Page<Product>.MaxLimit}.");
        }

        // WHY: `category` is the ONE whitelisted filter param (ADR-0002). An absent or blank
        // value means "no filter"; a present value is parsed as a Slug, so a malformed filter
        // (e.g. "Trail Shoes") is rejected as a domain-rule 422 exactly like a bad path slug —
        // one rule for what a slug is, applied everywhere. A well-formed-but-unknown category
        // simply matches no products (an empty page), which is the honest REST answer.
        Slug? categorySlug = string.IsNullOrWhiteSpace(category) ? null : Slug.From(category);
        TimelineCursor? after = cursor is null ? null : TimelineCursor.Decode(cursor);

        IReadOnlyList<Product> fetched =
            await products.ListAfterAsync(categorySlug, after, pageSize + 1, cancellationToken);

        bool hasMore = fetched.Count > pageSize;
        IReadOnlyList<Product> items = hasMore ? [.. fetched.Take(pageSize)] : fetched;

        string? next = hasMore
            ? new TimelineCursor(items[^1].CreatedAtUtc, items[^1].Id).Encode()
            : null;

        return new Page<Product>(items, next);
    }
}
