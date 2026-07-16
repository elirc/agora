using Agora.Application.Abstractions;
using Agora.Application.Errors;
using Agora.Application.Pricing;
using Agora.Application.Tests.Fakes;
using Agora.Domain;
using Agora.Domain.Access;
using Agora.Domain.Catalog;
using Agora.Domain.Pricing;

namespace Agora.Application.Tests.Pricing;

/// <summary>The three admin campaign use cases: schedule one, list them, end one early.</summary>
public class SaleCampaignUseCaseTests
{
    private static readonly Category Kitchen = CatalogFixtures.CategoryAt("Kitchen", "kitchen", 0);

    private static readonly DateTimeOffset Now = CatalogFixtures.Base;
    private static readonly DateTimeOffset Friday = Now.AddDays(2);
    private static readonly DateTimeOffset Monday = Now.AddDays(5);

    private sealed record Harness(
        CreateSaleCampaign Create,
        ListSaleCampaigns List,
        EndSaleCampaign End,
        InMemorySaleCampaignRepository Campaigns,
        CountingUnitOfWork UnitOfWork,
        Product Product);

    // WHY `existing` is a FUNCTION of the product id and not a list: a campaign is scoped to a
    // product, so a fixture that builds campaigns against some other Product instance silently tests
    // nothing (the list filters them all out and the assertion reads "0 == 0"). Making the harness
    // hand the id to the campaign builder removes the chance to get that wrong.
    private static Harness Build(
        Role role = Role.StoreAdmin,
        Func<Guid, IEnumerable<SaleCampaign>>? existing = null,
        int nowMinute = 0,
        bool archived = false)
    {
        Product product = CatalogFixtures.ProductAt(Kitchen.Id, "Skillet", "skillet", "AGA-K-1", 1, price: 50.00m);

        if (archived)
        {
            product.Archive(Now);
        }

        var products = new InMemoryProductRepository([product], [Kitchen]);
        var campaigns = new InMemorySaleCampaignRepository(existing?.Invoke(product.Id));
        var unitOfWork = new CountingUnitOfWork();
        var clock = new FixedClock(Now.AddMinutes(nowMinute));
        ICurrentUser user = StubCurrentUser.SignedIn(role);

        return new Harness(
            new CreateSaleCampaign(campaigns, products, unitOfWork, user, clock),
            new ListSaleCampaigns(campaigns, products, user, clock),
            new EndSaleCampaign(campaigns, unitOfWork, user, clock),
            campaigns,
            unitOfWork,
            product);
    }

    // ── Create ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_admin_can_schedule_a_percentage_sale()
    {
        Harness h = Build();

        CampaignView view = await h.Create.ExecuteAsync(
            h.Product.Id, "Labour Day Weekend", "PercentOff", 20m, Friday, Monday, default);

        Assert.Equal("Labour Day Weekend", view.Campaign.Name);
        Assert.Equal(DiscountKind.PercentOff, view.Campaign.Discount.Kind);
        Assert.Equal(20m, view.Campaign.Discount.Value);
        Assert.Equal(Friday, view.Campaign.Window.Starts);
        Assert.Equal(Now, view.Campaign.CreatedAtUtc);
        Assert.Equal(1, h.UnitOfWork.Commits);
        Assert.Single(h.Campaigns.Rows);
    }

    [Fact]
    public async Task A_campaign_scheduled_for_friday_is_not_running_today()
    {
        // ⭐ creating is not activating. The 201 says IsRunning=false, and on Friday — with nobody
        // touching anything — the storefront starts discounting. That is the entire argument for
        // storing a window instead of a boolean somebody has to remember to flip.
        Harness h = Build();

        CampaignView view = await h.Create.ExecuteAsync(
            h.Product.Id, "Friday Sale", "PercentOff", 20m, Friday, Monday, default);

        Assert.False(view.IsRunning);
        Assert.True(view.Campaign.IsActiveAt(Friday));
    }

    [Fact]
    public async Task A_campaign_whose_window_covers_now_is_already_running()
    {
        Harness h = Build();

        CampaignView view = await h.Create.ExecuteAsync(
            h.Product.Id, "Flash Sale", "AmountOff", 5m, Now.AddHours(-1), Now.AddHours(1), default);

        Assert.True(view.IsRunning);
    }

    [Fact]
    public async Task A_backdated_campaign_is_allowed_and_is_not_running()
    {
        // Recording a sale that already ran is a legitimate act, so Schedule has no clock to refuse
        // it with — and the response is honest that it is over.
        Harness h = Build();

        CampaignView view = await h.Create.ExecuteAsync(
            h.Product.Id, "Last Month", "PercentOff", 20m, Now.AddDays(-30), Now.AddDays(-27), default);

        Assert.False(view.IsRunning);
    }

    [Theory]
    [InlineData("Sparkle")]
    [InlineData("")]
    [InlineData(null)]
    public async Task An_unknown_discount_kind_names_the_legal_values(string? kind)
    {
        Harness h = Build();

        DomainRuleException error = await Assert.ThrowsAsync<DomainRuleException>(
            () => h.Create.ExecuteAsync(h.Product.Id, "Sale", kind, 20m, Friday, Monday, default));

        Assert.Contains("PercentOff", error.Message, StringComparison.Ordinal);
        Assert.Contains("AmountOff", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_campaign_on_a_product_that_does_not_exist_is_refused()
    {
        Harness h = Build();

        await Assert.ThrowsAsync<DomainRuleException>(
            () => h.Create.ExecuteAsync(Guid.NewGuid(), "Sale", "PercentOff", 20m, Friday, Monday, default));

        Assert.Equal(0, h.UnitOfWork.Commits);
    }

    [Fact]
    public async Task An_archived_product_cannot_be_put_on_sale()
    {
        // ⚠️ it would never show — but it WOULD start applying the moment someone restored the
        // product, which is a surprise nobody scheduled. Refuse now rather than arm a trap.
        Harness h = Build(archived: true);

        await Assert.ThrowsAsync<DomainRuleException>(
            () => h.Create.ExecuteAsync(h.Product.Id, "Sale", "PercentOff", 20m, Friday, Monday, default));
    }

    [Fact]
    public async Task A_backwards_window_is_refused()
    {
        Harness h = Build();

        await Assert.ThrowsAsync<DomainRuleException>(
            () => h.Create.ExecuteAsync(h.Product.Id, "Sale", "PercentOff", 20m, Monday, Friday, default));
    }

    [Fact]
    public async Task Nothing_is_committed_when_the_domain_refuses()
    {
        Harness h = Build();

        await Assert.ThrowsAsync<DomainRuleException>(
            () => h.Create.ExecuteAsync(h.Product.Id, "Sale", "PercentOff", 0m, Friday, Monday, default));

        Assert.Equal(0, h.UnitOfWork.Commits);
        Assert.Empty(h.Campaigns.Rows);
    }

    // ── List ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_admin_list_shows_past_running_and_future_campaigns()
    {
        Harness h = Build(existing: id =>
        [
            CatalogFixtures.CampaignOn(id, Discount.PercentOff(10m), -600, -300, "Past"),
            CatalogFixtures.CampaignOn(id, Discount.PercentOff(20m), -60, 60, "Running"),
            CatalogFixtures.CampaignOn(id, Discount.PercentOff(30m), 300, 600, "Future"),
        ]);

        IReadOnlyList<CampaignView> views = await h.List.ExecuteAsync(h.Product.Id, default);

        // 🔍 the whole point of the back-office view: an admin who could only see today could not
        // answer "what did we run last month?" or "what is queued for the weekend?".
        Assert.Equal(3, views.Count);
        Assert.Equal(["Future", "Running", "Past"], views.Select(v => v.Campaign.Name));
        Assert.Equal([false, true, false], views.Select(v => v.IsRunning));
    }

    [Fact]
    public async Task The_list_for_a_product_with_no_campaigns_is_empty_not_missing()
    {
        Harness h = Build();

        Assert.Empty(await h.List.ExecuteAsync(h.Product.Id, default));
    }

    [Fact]
    public async Task Listing_campaigns_for_an_unknown_product_is_a_404()
    {
        Harness h = Build();

        await Assert.ThrowsAsync<NotFoundException>(() => h.List.ExecuteAsync(Guid.NewGuid(), default));
    }

    // ── End ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ending_a_running_campaign_cuts_its_window_at_now()
    {
        Harness h = Build(existing: id => [CatalogFixtures.CampaignOn(id, Discount.PercentOff(20m), -60, 600)]);

        CampaignView view = await h.End.ExecuteAsync(h.Campaigns.Rows[0].Id, default);

        Assert.Equal(Now, view.Campaign.Window.Ends);
        Assert.False(view.IsRunning);
        Assert.Equal(1, h.UnitOfWork.Commits);

        // The row survives with its history intact — it ran, and the report can still say for how long.
        Assert.Equal(CatalogFixtures.Base.AddMinutes(-60), view.Campaign.Window.Starts);
        Assert.True(view.Campaign.IsActiveAt(Now.AddTicks(-1)));
    }

    [Fact]
    public async Task Ending_a_campaign_stops_it_discounting_immediately()
    {
        Product product = CatalogFixtures.ProductAt(Kitchen.Id, "Skillet", "skillet", "AGA-K-1", 1, price: 50.00m);
        SaleCampaign running = CatalogFixtures.CampaignOn(product.Id, Discount.PercentOff(20m), -60, 600);

        var campaigns = new InMemorySaleCampaignRepository([running]);
        var clock = new FixedClock(Now);
        var end = new EndSaleCampaign(campaigns, new CountingUnitOfWork(), StubCurrentUser.SignedIn(Role.StoreAdmin), clock);
        var pricing = new ProductPricing(campaigns, clock);

        Assert.True((await pricing.PriceAsync(product, default)).Price.IsOnSale);

        await end.ExecuteAsync(running.Id, default);

        Assert.False((await pricing.PriceAsync(product, default)).Price.IsOnSale);
    }

    [Fact]
    public async Task Ending_a_campaign_that_has_not_started_means_it_never_runs()
    {
        Harness h = Build(existing: id => [CatalogFixtures.CampaignOn(id, Discount.PercentOff(20m), 300, 600)]);

        CampaignView view = await h.End.ExecuteAsync(h.Campaigns.Rows[0].Id, default);

        Assert.True(view.Campaign.Window.IsEmpty);
        Assert.False(view.Campaign.IsActiveAt(CatalogFixtures.Base.AddMinutes(400)));
    }

    [Fact]
    public async Task Ending_an_already_ended_campaign_is_a_domain_rule_violation()
    {
        Harness h = Build(existing: id => [CatalogFixtures.CampaignOn(id, Discount.PercentOff(20m), -600, -300)]);

        await Assert.ThrowsAsync<DomainRuleException>(() => h.End.ExecuteAsync(h.Campaigns.Rows[0].Id, default));
        Assert.Equal(0, h.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Ending_an_unknown_campaign_is_a_404()
    {
        Harness h = Build();

        await Assert.ThrowsAsync<NotFoundException>(() => h.End.ExecuteAsync(Guid.NewGuid(), default));
    }

    // ── Authorization: every campaign use case is StoreAdmin-only ─────────────────────────────

    // 🔍 read the deny cases first. Ops is refused alongside Customer, on purpose: roles are
    // capabilities, not a hierarchy, and setting prices is not fulfillment's job. The API-level
    // matrix proves the same thing over HTTP; this proves the guard is in the USE CASE, so it holds
    // for any caller, including one that arrives through some future non-HTTP path.
    public static TheoryData<Role> NonAdminRoles() => [Role.Customer, Role.Ops];

    [Theory]
    [MemberData(nameof(NonAdminRoles))]
    public async Task Creating_a_campaign_is_forbidden_to_non_admins(Role role)
    {
        Harness h = Build(role);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => h.Create.ExecuteAsync(h.Product.Id, "Sale", "PercentOff", 20m, Friday, Monday, default));
    }

    [Theory]
    [MemberData(nameof(NonAdminRoles))]
    public async Task Listing_campaigns_is_forbidden_to_non_admins(Role role)
    {
        Harness h = Build(role);

        await Assert.ThrowsAsync<ForbiddenException>(() => h.List.ExecuteAsync(h.Product.Id, default));
    }

    [Theory]
    [MemberData(nameof(NonAdminRoles))]
    public async Task Ending_a_campaign_is_forbidden_to_non_admins(Role role)
    {
        Harness h = Build(role);

        await Assert.ThrowsAsync<ForbiddenException>(() => h.End.ExecuteAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Anonymous_callers_are_unauthenticated_not_forbidden()
    {
        Product product = CatalogFixtures.ProductAt(Kitchen.Id, "Skillet", "skillet", "AGA-K-1", 1);
        var products = new InMemoryProductRepository([product], [Kitchen]);
        var campaigns = new InMemorySaleCampaignRepository();
        ICurrentUser anonymous = StubCurrentUser.Anonymous();

        var create = new CreateSaleCampaign(
            campaigns, products, new CountingUnitOfWork(), anonymous, new FixedClock(Now));

        await Assert.ThrowsAsync<UnauthenticatedException>(
            () => create.ExecuteAsync(product.Id, "Sale", "PercentOff", 20m, Friday, Monday, default));
    }
}
