using Agora.Application.Abstractions;
using Agora.Domain;
using Agora.Domain.Access;
using Agora.Domain.Catalog;
using Agora.Domain.Pricing;
using Agora.Domain.Values;

namespace Agora.Application.Pricing;

/// <summary>
/// Store-admin action: schedule a sale on a product for a window of time.
/// </summary>
public sealed class CreateSaleCampaign(
    ISaleCampaignRepository campaigns,
    IProductRepository products,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public async Task<CampaignView> ExecuteAsync(
        Guid productId,
        string? name,
        string? discountKind,
        decimal discountValue,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        CancellationToken cancellationToken)
    {
        currentUser.RequireRole(Role.StoreAdmin);

        // WHY check the product ourselves: a campaign on a product that does not exist is a
        // well-formed request the business rejects (422), not a foreign-key 500 — the same rule
        // CreateProduct follows for its category.
        Product product = await products.FindByIdAsync(productId, cancellationToken)
            ?? throw new DomainRuleException("That product does not exist.");

        // ⚠️ you cannot put an archived product on sale. It is not in the storefront, so the campaign
        // could never be seen — but it WOULD start applying the moment someone restored the product,
        // which is a surprise nobody scheduled. Refuse now, loudly, rather than arm a trap.
        if (product.IsArchived)
        {
            throw new DomainRuleException("An archived product cannot be put on sale.");
        }

        DateRange window = DateRange.From(startsAtUtc, endsAtUtc);

        // LEARN: creating is not activating, so the clock is NOT consulted about the window. A
        // campaign scheduled for Friday is created today and starts on Friday with nobody touching
        // it; a campaign backdated to last month is a legitimate record of a sale that ran. If this
        // method refused past windows it would need to know "now" — and would then be a use case
        // whose tests start failing on the wrong side of midnight.
        DateTimeOffset now = clock.GetUtcNow();

        var campaign = SaleCampaign.Schedule(
            product.Id,
            name,
            ParseDiscount(discountKind, discountValue),
            window,
            now);

        campaigns.Add(campaign);
        await unitOfWork.CommitAsync(cancellationToken);

        // WHY return the same CampaignView shape the list returns, resolved against the SAME instant
        // we just stamped on the row: the 201 body and a follow-up GET then agree by construction.
        // For a sale scheduled for Friday, IsRunning is false — creating is not starting, and the
        // response says so rather than implying the admin flipped a switch.
        return new CampaignView(campaign, campaign.IsActiveAt(now));
    }

    // WHY the kind arrives as a string and is parsed HERE: the wire speaks JSON, and an unknown kind
    // is a business-rule failure with a message that names the legal values — not a model-binding
    // 400 that says "could not convert". The domain enum stays the domain's; the string is transport.
    private static Discount ParseDiscount(string? kind, decimal value) => kind?.Trim().ToLowerInvariant() switch
    {
        "percentoff" => Discount.PercentOff(value),
        "amountoff" => Discount.AmountOff(value),
        _ => throw new DomainRuleException("discountKind must be one of: PercentOff, AmountOff."),
    };
}
