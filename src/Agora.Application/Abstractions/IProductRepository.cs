using Agora.Application.Catalog;
using Agora.Application.Pagination;
using Agora.Domain.Catalog;

namespace Agora.Application.Abstractions;

public interface IProductRepository
{
    /// <summary>
    /// Newest-first page, optionally scoped to one category by slug, and always scoped to an
    /// explicit <paramref name="status"/> (active / archived / all). <paramref name="after"/> is an
    /// exclusive lower bound in (CreatedAtUtc DESC, Id DESC) order; callers over-fetch by one to
    /// detect a next page.
    /// </summary>
    // ⚠️ the status parameter is not optional on purpose: soft-archive (S03) means "which rows"
    // is now a question every list must answer out loud. See ProductStatusFilter.
    Task<IReadOnlyList<Product>> ListAfterAsync(
        Slug? categorySlug,
        ProductStatusFilter status,
        TimelineCursor? after,
        int fetch,
        CancellationToken cancellationToken);

    /// <summary>
    /// Look up a product by its slug within a <paramref name="status"/> scope. The storefront passes
    /// <see cref="ProductStatusFilter.Active"/>, so an archived product reads as "not found" to a
    /// shopper while still existing for the admin.
    /// </summary>
    Task<Product?> FindBySlugAsync(Slug slug, ProductStatusFilter status, CancellationToken cancellationToken);

    /// <summary>
    /// Look up a product by id for an admin write. Returns a TRACKED entity (unlike the read paths,
    /// which are AsNoTracking) so the caller can mutate it and let the unit of work persist the
    /// change — including the optimistic-concurrency check on <see cref="Product.Version"/>.
    /// Sees every archive state: you must be able to fetch an archived product to restore it.
    /// </summary>
    Task<Product?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Any product sharing this SKU (any archive state), for the create-time uniqueness check.</summary>
    Task<Product?> FindBySkuAsync(Sku sku, CancellationToken cancellationToken);

    /// <summary>
    /// The products for a known set of ids, in one query — the cart's read (S05). Callers pass an
    /// explicit <paramref name="status"/> like every other list here.
    /// </summary>
    // ⚠️ WHY THIS EXISTS AND FindByIdAsync-IN-A-LOOP DOES NOT. A cart with ten lines needs ten
    // products to price itself. Ten calls to FindByIdAsync is ten round trips to answer one question,
    // and it is the textbook N+1 — the kind that is invisible in a test (the fake is a list) and
    // invisible in dev (the database is on localhost) and is a page-load in production. S04 made the
    // same call for campaigns (ListActiveForProductsAsync takes a set, not an id) for the same
    // reason. The shape of the port is where an N+1 is prevented; by the time it is a query it is
    // already S14's problem.
    // GOTCHA: the ids are a SET, so the answer may be shorter than the question (an id that matches
    // nothing simply is not in the result) and its order is not the caller's. Callers index by id;
    // nobody zips these two lists together positionally.
    Task<IReadOnlyList<Product>> ListByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        ProductStatusFilter status,
        CancellationToken cancellationToken);

    void Add(Product product);
}
