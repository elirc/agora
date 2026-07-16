using Agora.Application.Abstractions;
using Agora.Domain.Pricing;

namespace Agora.Application.Tests.Fakes;

/// <summary>
/// In-memory sale campaigns. Mirrors the port exactly: an instant-scoped read for the storefront, an
/// everything read for the admin, a by-reference find for the one query that leads to a write.
/// </summary>
// 🔍 note that the active-window filter here uses the DOMAIN's own IsActiveAt rather than
// re-implementing "starts <= asOf < ends" a third time. The EF adapter cannot do that (it has to
// express the predicate in SQL), which is exactly why PriceResolver re-checks every row the adapter
// returns — the database can narrow the set, but only the domain decides. This fake gets to be
// simple because it is on the right side of that seam.
public sealed class InMemorySaleCampaignRepository(IEnumerable<SaleCampaign>? campaigns = null)
    : ISaleCampaignRepository
{
    private readonly List<SaleCampaign> _campaigns = campaigns?.ToList() ?? [];

    public IReadOnlyList<SaleCampaign> Rows => _campaigns;

    public Task<IReadOnlyList<SaleCampaign>> ListActiveForProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SaleCampaign> found = [.. _campaigns
            .Where(campaign => productIds.Contains(campaign.ProductId))
            .Where(campaign => campaign.IsActiveAt(asOf))];

        return Task.FromResult(found);
    }

    public Task<IReadOnlyList<SaleCampaign>> ListForProductAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SaleCampaign> found = [.. _campaigns
            .Where(campaign => campaign.ProductId == productId)
            .OrderByDescending(campaign => campaign.Window.Starts)
            .ThenByDescending(campaign => campaign.Id)];

        return Task.FromResult(found);
    }

    // The live object reference — mutating it and "committing" reflects immediately, the in-memory
    // stand-in for EF's change tracking.
    public Task<SaleCampaign?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult<SaleCampaign?>(_campaigns.FirstOrDefault(campaign => campaign.Id == id));

    public void Add(SaleCampaign campaign) => _campaigns.Add(campaign);
}
