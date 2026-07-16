using System.Net;
using System.Net.Http.Json;
using Agora.Api.Tests.Infrastructure;
using Agora.Contracts.V1;
using Agora.Infrastructure.Persistence;

namespace Agora.Api.Tests;

/// <summary>
/// The pricing surface over real HTTP: schedule a sale, see it on the storefront, end it early.
/// </summary>
// 🔍 these tests run against the REAL clock (the API factory registers TimeProvider.System), which is
// why every window here is generously wide — "an hour ago until an hour from now" — rather than
// pinned to a boundary. Boundary behaviour is pinned where it can be pinned exactly: the domain and
// use-case tests, which own a frozen clock. Asserting a half-open edge over HTTP against the wall
// clock would be a race, and a test that is a race is worse than no test: it fails on someone else's
// PR and gets retried until it passes.
public class AdminPricingEndpointsTests : IClassFixture<AgoraApiFactory>, IAsyncLifetime
{
    private readonly AgoraApiFactory _factory;

    private string _admin = null!;
    private Guid _categoryId;

    public AdminPricingEndpointsTests(AgoraApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _admin = await _factory.LoginAsync(AgoraSeeder.StoreAdminEmail, AgoraSeeder.StoreAdminPassword);

        List<CategoryDto> categories =
            (await _factory.NewClient().GetFromJsonAsync<List<CategoryDto>>("/api/v1/categories"))!;
        _categoryId = categories[0].Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_scheduled_sale_shows_up_on_the_storefront_with_a_resolved_price()
    {
        AdminProductDto product = await CreateProductAsync(50.00m);

        AdminSaleCampaignDto campaign = await ScheduleAsync(
            product.Id, "Weekend Sale", "PercentOff", 20m, Hours(-1), Hours(1));

        Assert.True(campaign.IsRunning);

        // ⭐ THE ROUND TRIP. The admin scheduled a window; nobody flipped a switch; the shopper's
        // anonymous GET now resolves 20% off — list price intact, effective price reduced, badge
        // number computed on the server.
        ProductDto shopperView = await GetStorefrontAsync(product.Slug);

        Assert.Equal(50.00m, Amount(shopperView.PriceAmount));
        Assert.Equal(40.00m, Amount(shopperView.EffectivePriceAmount));
        Assert.Equal(20, shopperView.SavePercent);
        Assert.NotNull(shopperView.Sale);
        Assert.Equal("Weekend Sale", shopperView.Sale.Name);
        Assert.Equal(campaign.Id, shopperView.Sale.CampaignId);
    }

    [Fact]
    public async Task A_product_with_no_campaign_is_at_list_price_with_no_sale_block()
    {
        AdminProductDto product = await CreateProductAsync(48.00m);

        ProductDto shopperView = await GetStorefrontAsync(product.Slug);

        Assert.Equal(48.00m, Amount(shopperView.PriceAmount));
        Assert.Equal(48.00m, Amount(shopperView.EffectivePriceAmount));
        Assert.Equal(0, shopperView.SavePercent);
        Assert.Null(shopperView.Sale);
    }

    [Fact]
    public async Task A_sale_scheduled_for_next_week_does_not_touch_todays_price()
    {
        AdminProductDto product = await CreateProductAsync(50.00m);

        AdminSaleCampaignDto campaign = await ScheduleAsync(
            product.Id, "Next Week", "PercentOff", 50m, Hours(24 * 7), Hours(24 * 9));

        // Creating is not activating: the admin sees it queued, the shopper sees nothing.
        Assert.False(campaign.IsRunning);

        ProductDto shopperView = await GetStorefrontAsync(product.Slug);

        Assert.Equal(50.00m, Amount(shopperView.EffectivePriceAmount));
        Assert.Null(shopperView.Sale);
    }

    [Fact]
    public async Task A_fixed_amount_sale_resolves_and_derives_its_badge()
    {
        AdminProductDto product = await CreateProductAsync(50.00m);

        await ScheduleAsync(product.Id, "Ten Off", "AmountOff", 10m, Hours(-1), Hours(1));

        ProductDto shopperView = await GetStorefrontAsync(product.Slug);

        Assert.Equal(40.00m, Amount(shopperView.EffectivePriceAmount));
        // No percentage exists on this campaign at all — the badge comes from the two amounts.
        Assert.Equal(20, shopperView.SavePercent);
    }

    [Fact]
    public async Task The_storefront_list_and_the_product_page_agree_on_the_price()
    {
        AdminProductDto product = await CreateProductAsync(50.00m);
        await ScheduleAsync(product.Id, "Weekend Sale", "PercentOff", 20m, Hours(-1), Hours(1));

        ProductDto fromPage = await GetStorefrontAsync(product.Slug);
        List<ProductDto> list =
            (await _factory.NewClient().GetFromJsonAsync<List<ProductDto>>("/api/v1/products?limit=100"))!;
        ProductDto fromList = list.Single(p => p.Id == product.Id);

        Assert.Equal(Amount(fromPage.EffectivePriceAmount), Amount(fromList.EffectivePriceAmount));
        Assert.Equal(fromPage.SavePercent, fromList.SavePercent);
    }

    [Fact]
    public async Task Ending_a_sale_returns_the_storefront_to_the_list_price()
    {
        AdminProductDto product = await CreateProductAsync(50.00m);
        AdminSaleCampaignDto campaign = await ScheduleAsync(
            product.Id, "Flash Sale", "PercentOff", 20m, Hours(-1), Hours(1));

        Assert.Equal(40.00m, Amount((await GetStorefrontAsync(product.Slug)).EffectivePriceAmount));

        HttpResponseMessage ended = await _factory.As(_admin)
            .PostAsync($"/api/v1/admin/campaigns/{campaign.Id}/end", null);
        ended.EnsureSuccessStatusCode();

        AdminSaleCampaignDto endedDto = (await ended.Content.ReadFromJsonAsync<AdminSaleCampaignDto>())!;
        Assert.False(endedDto.IsRunning);

        // The row is not deleted — its window was cut, and the history says it ran.
        Assert.Equal(50.00m, Amount((await GetStorefrontAsync(product.Slug)).EffectivePriceAmount));
        Assert.Null((await GetStorefrontAsync(product.Slug)).Sale);
    }

    [Fact]
    public async Task Ending_a_sale_twice_is_a_422()
    {
        AdminProductDto product = await CreateProductAsync(50.00m);
        AdminSaleCampaignDto campaign = await ScheduleAsync(
            product.Id, "Flash Sale", "PercentOff", 20m, Hours(-1), Hours(1));

        await _factory.As(_admin).PostAsync($"/api/v1/admin/campaigns/{campaign.Id}/end", null);

        HttpResponseMessage second = await _factory.As(_admin)
            .PostAsync($"/api/v1/admin/campaigns/{campaign.Id}/end", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
    }

    [Fact]
    public async Task The_admin_campaign_list_shows_every_campaign_for_the_product()
    {
        AdminProductDto product = await CreateProductAsync(50.00m);

        await ScheduleAsync(product.Id, "Running", "PercentOff", 20m, Hours(-1), Hours(1));
        await ScheduleAsync(product.Id, "Queued", "PercentOff", 30m, Hours(48), Hours(72));
        await ScheduleAsync(product.Id, "Finished", "PercentOff", 10m, Hours(-72), Hours(-48));

        List<AdminSaleCampaignDto> campaigns = (await _factory.As(_admin)
            .GetFromJsonAsync<List<AdminSaleCampaignDto>>($"/api/v1/admin/products/{product.Id}/campaigns"))!;

        Assert.Equal(3, campaigns.Count);
        Assert.Equal(["Queued", "Running", "Finished"], campaigns.Select(c => c.Name));
        Assert.Equal([false, true, false], campaigns.Select(c => c.IsRunning));
    }

    [Fact]
    public async Task Campaigns_for_an_unknown_product_are_a_404()
    {
        HttpResponseMessage response = await _factory.As(_admin)
            .GetAsync($"/api/v1/admin/products/{Guid.NewGuid()}/campaigns");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ending_an_unknown_campaign_is_a_404()
    {
        HttpResponseMessage response = await _factory.As(_admin)
            .PostAsync($"/api/v1/admin/campaigns/{Guid.NewGuid()}/end", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Sparkle", 20)]     // not a discount kind
    [InlineData("PercentOff", 0)]   // not a discount
    [InlineData("PercentOff", 150)] // more than everything
    [InlineData("AmountOff", -5)]   // a negative discount is a price rise
    public async Task A_nonsense_campaign_is_a_422(string kind, decimal value)
    {
        AdminProductDto product = await CreateProductAsync(50.00m);

        HttpResponseMessage response = await _factory.As(_admin).PostAsJsonAsync(
            $"/api/v1/admin/products/{product.Id}/campaigns",
            new CreateSaleCampaignRequest("Sale", kind, value, Hours(-1), Hours(1)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task A_backwards_window_is_a_422()
    {
        AdminProductDto product = await CreateProductAsync(50.00m);

        HttpResponseMessage response = await _factory.As(_admin).PostAsJsonAsync(
            $"/api/v1/admin/products/{product.Id}/campaigns",
            new CreateSaleCampaignRequest("Sale", "PercentOff", 20m, Hours(5), Hours(1)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task An_archived_product_cannot_be_put_on_sale()
    {
        AdminProductDto product = await CreateProductAsync(50.00m);

        await _factory.As(_admin).PostAsJsonAsync(
            $"/api/v1/admin/products/{product.Id}/archive", new ProductVersionRequest(product.Version));

        HttpResponseMessage response = await _factory.As(_admin).PostAsJsonAsync(
            $"/api/v1/admin/products/{product.Id}/campaigns",
            new CreateSaleCampaignRequest("Sale", "PercentOff", 20m, Hours(-1), Hours(1)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task A_campaign_survives_the_round_trip_through_the_database()
    {
        // 🔍 the reason this test exists rather than being "covered" by the use-case tests: this is
        // the only place a SaleCampaign is written to a real database and read back by a real EF
        // query. Money and DateRange are COMPLEX TYPES, and complex-type mapping is exactly the kind
        // of thing that compiles, unit-tests green, and then materialises a window of DateTime.MinValue.
        AdminProductDto product = await CreateProductAsync(34.50m);

        await ScheduleAsync(product.Id, "Round Trip", "AmountOff", 4.50m, Hours(-1), Hours(1));

        List<AdminSaleCampaignDto> campaigns = (await _factory.As(_admin)
            .GetFromJsonAsync<List<AdminSaleCampaignDto>>($"/api/v1/admin/products/{product.Id}/campaigns"))!;

        AdminSaleCampaignDto found = Assert.Single(campaigns);
        Assert.Equal("AmountOff", found.DiscountKind);
        Assert.Equal(4.50m, Amount(found.DiscountValue));
        Assert.True(found.StartsAtUtc < found.EndsAtUtc);
        Assert.True(found.IsRunning);

        Assert.Equal(30.00m, Amount((await GetStorefrontAsync(product.Slug)).EffectivePriceAmount));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private static DateTimeOffset Hours(int offset) => DateTimeOffset.UtcNow.AddHours(offset);

    // The generated OpenAPI types a decimal as number-or-string, and System.Text.Json round-trips it
    // to whichever the server wrote. Normalise once, here, rather than in every assertion.
    private static decimal Amount(decimal value) => decimal.Round(value, 2);

    private async Task<ProductDto> GetStorefrontAsync(string slug) =>
        (await _factory.NewClient().GetFromJsonAsync<ProductDto>($"/api/v1/products/{slug}"))!;

    private async Task<AdminProductDto> CreateProductAsync(decimal price)
    {
        HttpResponseMessage response = await _factory.As(_admin).PostAsJsonAsync(
            "/api/v1/admin/products",
            new CreateProductRequest(
                _categoryId,
                $"Product {Guid.NewGuid():N}",
                null,
                $"SKU-{Guid.NewGuid():N}"[..20],
                "A fine thing.",
                price,
                "USD"));

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AdminProductDto>())!;
    }

    private async Task<AdminSaleCampaignDto> ScheduleAsync(
        Guid productId, string name, string kind, decimal value, DateTimeOffset starts, DateTimeOffset ends)
    {
        HttpResponseMessage response = await _factory.As(_admin).PostAsJsonAsync(
            $"/api/v1/admin/products/{productId}/campaigns",
            new CreateSaleCampaignRequest(name, kind, value, starts, ends));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AdminSaleCampaignDto>())!;
    }
}
