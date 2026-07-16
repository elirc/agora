using Agora.Application.Abstractions;
using Agora.Domain.Pricing;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Persistence.Repositories;

internal sealed class SaleCampaignRepository(AgoraDbContext db) : ISaleCampaignRepository
{
    public async Task<IReadOnlyList<SaleCampaign>> ListActiveForProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return [];
        }

        // 🔍 THE WINDOW PREDICATE, IN SQL, and it is the same half-open rule DateRange.Contains
        // applies in memory: Starts <= asOf < Ends. Two implementations of one rule is a duplication
        // the domain re-checks — PriceResolver calls IsActiveAt on every row this returns, so the
        // adapter can only ever narrow the set, never decide it. If this filter is subtly wrong the
        // worst case is an over-fetch the domain discards; a WHERE clause that was TRUSTED would
        // instead be a second, silent definition of "on sale".
        // GOTCHA: `Window.Starts` is a DateTimeOffset inside a complex property, and SQLite cannot
        // compare those — the UTC-ticks converter in AgoraDbContext makes these two comparisons
        // integer ops. That converter had to learn about complex types this sprint for this exact
        // query. On Postgres it is a plain timestamptz comparison.
        return await db.SaleCampaigns
            .AsNoTracking()
            .Where(campaign => productIds.Contains(campaign.ProductId))
            .Where(campaign => campaign.Window.Starts <= asOf && campaign.Window.Ends > asOf)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SaleCampaign>> ListForProductAsync(
        Guid productId,
        CancellationToken cancellationToken) =>
        await db.SaleCampaigns
            .AsNoTracking()
            .Where(campaign => campaign.ProductId == productId)
            // Newest window first: the admin cares about what is running and what is queued, and the
            // archaeology is what they scroll to.
            .OrderByDescending(campaign => campaign.Window.Starts)
            .ThenByDescending(campaign => campaign.Id)
            .ToListAsync(cancellationToken);

    // WHY no AsNoTracking here (unlike the reads above): ending a campaign fetches, mutates, and
    // commits the SAME entity, so it must be tracked for SaveChanges to see the change. Same split
    // the product repository makes — reads are no-tracking, the one query that leads to a write is not.
    public Task<SaleCampaign?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.SaleCampaigns.SingleOrDefaultAsync(campaign => campaign.Id == id, cancellationToken);

    public void Add(SaleCampaign campaign) => db.SaleCampaigns.Add(campaign);
}
