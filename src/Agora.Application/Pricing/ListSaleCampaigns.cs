using Agora.Application.Abstractions;
using Agora.Application.Errors;
using Agora.Domain.Access;
using Agora.Domain.Pricing;

namespace Agora.Application.Pricing;

/// <summary>A campaign as the back-office sees it: the row, plus whether it is running right now.</summary>
// WHY the flag rides alongside the entity instead of living on it: "running right now" is not a fact
// about a campaign, it is a fact about a campaign AND an instant. The use case knows the instant
// (it has the clock); the entity does not and should not. So the pair is assembled here, at the layer
// that read the clock, and the answer is stamped with the moment that made it true.
public sealed record CampaignView(SaleCampaign Campaign, bool IsRunning);

/// <summary>
/// Store-admin action: every campaign ever scheduled on a product — past, running, and future.
/// </summary>
public sealed class ListSaleCampaigns(
    ISaleCampaignRepository campaigns,
    IProductRepository products,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<CampaignView>> ExecuteAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        currentUser.RequireRole(Role.StoreAdmin);

        if (await products.FindByIdAsync(productId, cancellationToken) is null)
        {
            throw new NotFoundException("product", productId);
        }

        // ⚠️ ONE read of the clock for the whole list — same rule as the storefront's pricing query.
        // A per-row read would let two campaigns in the same response be judged against two different
        // instants, which is exactly the class of bug this sprint is about.
        DateTimeOffset asOf = clock.GetUtcNow();

        IReadOnlyList<SaleCampaign> rows = await campaigns.ListForProductAsync(productId, cancellationToken);

        // 🔍 the admin list deliberately includes campaigns that have ENDED and campaigns that have
        // not STARTED — that is the whole reason this endpoint is not the storefront's. A back-office
        // that can only see today cannot answer "what did we run last month?" or "what is queued for
        // the weekend?", which are the two questions a merchandiser actually has.
        return [.. rows.Select(campaign => new CampaignView(campaign, campaign.IsActiveAt(asOf)))];
    }
}
