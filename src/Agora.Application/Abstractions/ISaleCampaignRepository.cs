using Agora.Domain.Pricing;

namespace Agora.Application.Abstractions;

public interface ISaleCampaignRepository
{
    /// <summary>
    /// Campaigns whose window covers <paramref name="asOf"/>, for any of <paramref name="productIds"/>.
    /// The storefront's read path: one query for a whole page of products.
    /// </summary>
    // 🔍 the instant is a PARAMETER here too, and that is not decoration — it is the second and last
    // place time enters the pricing pipeline (the first is the injected clock in ProductPricing).
    // The filter is pushed to the database on purpose: a product accumulates campaigns forever, and
    // loading a decade of them to throw away all but one is an N+1 with a bigger N every quarter. The
    // adapter compares the window's endpoints in SQL; the domain re-checks with IsActiveAt so the
    // answer never depends on the adapter having been faithful.
    // GOTCHA: those endpoints are DateTimeOffsets, and SQLite cannot compare them — the DbContext's
    // UTC-ticks converter is what makes this WHERE clause work on the test provider. See
    // AgoraDbContext; S04 is the sprint that taught it about complex types.
    Task<IReadOnlyList<SaleCampaign>> ListActiveForProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);

    /// <summary>
    /// Every campaign ever scheduled on one product, newest window first — the admin's view, which
    /// deliberately includes past and future campaigns.
    /// </summary>
    Task<IReadOnlyList<SaleCampaign>> ListForProductAsync(
        Guid productId,
        CancellationToken cancellationToken);

    /// <summary>
    /// One campaign by id, TRACKED — the caller mutates it (ending a sale) and lets the unit of work
    /// persist the change.
    /// </summary>
    Task<SaleCampaign?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    void Add(SaleCampaign campaign);
}
