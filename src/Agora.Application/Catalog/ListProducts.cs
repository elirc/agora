using Agora.Application.Abstractions;
using Agora.Application.Errors;
using Agora.Application.Pagination;
using Agora.Application.Pricing;
using Agora.Domain.Catalog;

namespace Agora.Application.Catalog;

/// <summary>Newest-first page of products, optionally filtered to one category by slug.</summary>
public sealed class ListProducts(IProductRepository products, ProductPricing pricing)
{
    public async Task<Page<PricedProduct>> ExecuteAsync(
        string? category,
        string? cursor,
        int? limit,
        CancellationToken cancellationToken)
    {
        int pageSize = limit ?? Page<PricedProduct>.DefaultLimit;

        if (pageSize < 1 || pageSize > Page<PricedProduct>.MaxLimit)
        {
            throw new RequestValidationException(
                $"limit must be between 1 and {Page<PricedProduct>.MaxLimit}.");
        }

        // WHY: `category` is the ONE whitelisted filter param (ADR-0002). An absent or blank
        // value means "no filter"; a present value is parsed as a Slug, so a malformed filter
        // (e.g. "Trail Shoes") is rejected as a domain-rule 422 exactly like a bad path slug —
        // one rule for what a slug is, applied everywhere. A well-formed-but-unknown category
        // simply matches no products (an empty page), which is the honest REST answer.
        Slug? categorySlug = string.IsNullOrWhiteSpace(category) ? null : Slug.From(category);
        TimelineCursor? after = cursor is null ? null : TimelineCursor.Decode(cursor);

        // ⚠️ the storefront sees ACTIVE products only — archived rows still exist in the database
        // (for the admin, for past orders) but a shopper never learns they were ever here.
        IReadOnlyList<Product> fetched = await products.ListAfterAsync(
            categorySlug, ProductStatusFilter.Active, after, pageSize + 1, cancellationToken);

        bool hasMore = fetched.Count > pageSize;
        IReadOnlyList<Product> items = hasMore ? [.. fetched.Take(pageSize)] : fetched;

        // 🔍 prices resolve AFTER the page is cut — for the rows we are returning, not the extra one
        // we over-fetched to detect a next page — and in ONE call for the whole page. A per-row
        // resolve would be an N+1 against the campaigns table AND would read the clock N times, which
        // is the subtler of the two bugs: a page whose rows disagree about what time it is.
        // WHY the cursor still walks (CreatedAtUtc, Id) and not price: a keyset needs a stable,
        // indexed, total order, and an effective price is neither stable (it changes by itself at a
        // window boundary — a shopper mid-walk would see rows shuffle under them) nor present in the
        // database (it is computed). "Sort the catalog by sale price" is a read-model question — S14's,
        // and the answer is a projection, not an ORDER BY.
        IReadOnlyList<PricedProduct> priced = await pricing.PriceAsync(items, cancellationToken);

        string? next = hasMore
            ? new TimelineCursor(items[^1].CreatedAtUtc, items[^1].Id).Encode()
            : null;

        return new Page<PricedProduct>(priced, next);
    }
}
