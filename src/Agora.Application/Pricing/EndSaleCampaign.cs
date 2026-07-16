using Agora.Application.Abstractions;
using Agora.Application.Errors;
using Agora.Domain.Access;
using Agora.Domain.Pricing;

namespace Agora.Application.Pricing;

/// <summary>
/// Store-admin action: stop a running (or scheduled) sale now. The campaign's window is cut short;
/// the row survives, so the report still knows it ran.
/// </summary>
public sealed class EndSaleCampaign(
    ISaleCampaignRepository campaigns,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public async Task<CampaignView> ExecuteAsync(Guid campaignId, CancellationToken cancellationToken)
    {
        currentUser.RequireRole(Role.StoreAdmin);

        SaleCampaign campaign = await campaigns.FindByIdAsync(campaignId, cancellationToken)
            ?? throw new NotFoundException("sale campaign", campaignId);

        // LEARN: the clock is read here and handed DOWN into the domain, which is the pattern this
        // whole sprint is arguing for. `campaign.End(nowUtc)` cannot be wrong about what time it is,
        // because it does not know — it just cuts the window at the instant it was given. A test
        // freezes that instant and pins the resulting window exactly; production passes the real one.
        // The domain rule (you cannot end what already ended) is the domain's; "what time is it" is
        // the application's, because that is the layer that owns the request.
        DateTimeOffset now = clock.GetUtcNow();

        campaign.End(now);

        await unitOfWork.CommitAsync(cancellationToken);

        // WHY re-derive the flag rather than hardcode `false`: End() clamps the window to `now`, and
        // the window is half-open, so the campaign is now over BY CONSTRUCTION — but saying that out
        // loud in a second place is how the two facts drift. Ask the same question the list asks,
        // against the same instant, and there is only ever one answer.
        return new CampaignView(campaign, campaign.IsActiveAt(now));
    }
}
