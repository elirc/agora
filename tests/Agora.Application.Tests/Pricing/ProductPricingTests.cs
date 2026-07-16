using Agora.Application.Catalog;
using Agora.Application.Pagination;
using Agora.Application.Pricing;
using Agora.Application.Tests.Fakes;
using Agora.Domain.Catalog;
using Agora.Domain.Pricing;
using Agora.Domain.Values;

namespace Agora.Application.Tests.Pricing;

/// <summary>
/// The price-resolution query, and the catalog reads it feeds. These tests move the CLOCK, not the
/// data: one product, one campaign, several instants, several answers.
/// </summary>
// 📘 this is what "time is an input" buys you as a test author. The clock is a constructor parameter,
// so "what does this page cost on Saturday?" is a test that runs in four milliseconds, in any
// timezone, in 2031, and asserts an EXACT price rather than a range. If the clock were read from the
// wall, every one of these would be either impossible or a Thread.Sleep.
public class ProductPricingTests
{
    private static readonly Category Kitchen = CatalogFixtures.CategoryAt("Kitchen", "kitchen", 0);

    private static Product Skillet() =>
        CatalogFixtures.ProductAt(Kitchen.Id, "Skillet", "skillet", "AGA-K-1", 1, price: 50.00m);

    private static Money Usd(decimal amount) => Money.Of(amount, CurrencyCode.Usd);

    private static ProductPricing Pricing(IEnumerable<SaleCampaign> campaigns, int atMinute) =>
        new(
            new InMemorySaleCampaignRepository(campaigns),
            new FixedClock(CatalogFixtures.Base.AddMinutes(atMinute)));

    [Fact]
    public async Task A_product_with_no_campaigns_is_priced_at_list()
    {
        Product skillet = Skillet();

        PricedProduct priced = await Pricing([], 0).PriceAsync(skillet, default);

        Assert.Equal(Usd(50.00m), priced.Price.Amount);
        Assert.False(priced.Price.IsOnSale);
        Assert.Null(priced.Price.Campaign);
    }

    // ⭐ ONE CAMPAIGN, FOUR CLOCKS, FOUR PRICES. Nothing in the database changes between these rows —
    // only the instant does. That is the sprint's whole claim, executed.
    [Theory]
    [InlineData(59, 50.00, false)]    // one minute before the window opens
    [InlineData(60, 40.00, true)]     // exactly at the start
    [InlineData(90, 40.00, true)]     // mid-sale
    [InlineData(120, 50.00, false)]   // exactly at the end — half-open, so it is over
    [InlineData(500, 50.00, false)]   // long after
    public async Task The_effective_price_depends_only_on_when_you_ask(
        int minute, decimal expected, bool onSale)
    {
        Product skillet = Skillet();
        SaleCampaign weekend = CatalogFixtures.CampaignOn(
            skillet.Id, Discount.PercentOff(20m), startsMinute: 60, endsMinute: 120);

        PricedProduct priced = await Pricing([weekend], minute).PriceAsync(skillet, default);

        Assert.Equal(Usd(expected), priced.Price.Amount);
        Assert.Equal(onSale, priced.Price.IsOnSale);
        // The list price never moves — a sale does not overwrite what the thing costs, it explains
        // what it costs today. That is what lets the storefront strike the old number through.
        Assert.Equal(Usd(50.00m), priced.Price.ListPrice);
    }

    [Fact]
    public async Task A_campaign_on_another_product_never_leaks_across()
    {
        Product skillet = Skillet();
        Product board = CatalogFixtures.ProductAt(Kitchen.Id, "Board", "board", "AGA-K-2", 2, price: 20.00m);

        SaleCampaign onBoard = CatalogFixtures.CampaignOn(
            board.Id, Discount.PercentOff(50m), startsMinute: 0, endsMinute: 999);

        IReadOnlyList<PricedProduct> priced = await Pricing([onBoard], 60).PriceAsync([skillet, board], default);

        Assert.Equal(Usd(50.00m), priced.Single(p => p.Product.Id == skillet.Id).Price.Amount);
        Assert.Equal(Usd(10.00m), priced.Single(p => p.Product.Id == board.Id).Price.Amount);
    }

    [Fact]
    public async Task A_whole_page_is_priced_as_of_one_instant()
    {
        Product skillet = Skillet();
        Product board = CatalogFixtures.ProductAt(Kitchen.Id, "Board", "board", "AGA-K-2", 2, price: 20.00m);

        SaleCampaign a = CatalogFixtures.CampaignOn(skillet.Id, Discount.PercentOff(20m), 0, 999, "A");
        SaleCampaign b = CatalogFixtures.CampaignOn(board.Id, Discount.AmountOff(5m), 0, 999, "B");

        IReadOnlyList<PricedProduct> priced = await Pricing([a, b], 60).PriceAsync([skillet, board], default);

        Assert.Equal(2, priced.Count);
        Assert.Equal(Usd(40.00m), priced[0].Price.Amount);
        Assert.Equal(Usd(15.00m), priced[1].Price.Amount);
    }

    [Fact]
    public async Task Pricing_an_empty_page_touches_nothing()
    {
        // Not a micro-optimisation — a guard. Without it the repository gets an empty id list and the
        // adapter has to decide what "WHERE ProductId IN ()" means, which is a question no database
        // answers the same way twice.
        Assert.Empty(await Pricing([], 0).PriceAsync([], default));
    }

    [Fact]
    public async Task Pricing_one_product_and_pricing_it_in_a_page_agree()
    {
        Product skillet = Skillet();
        SaleCampaign weekend = CatalogFixtures.CampaignOn(skillet.Id, Discount.PercentOff(20m), 0, 999);

        ProductPricing pricing = Pricing([weekend], 60);

        PricedProduct single = await pricing.PriceAsync(skillet, default);
        IReadOnlyList<PricedProduct> page = await pricing.PriceAsync([skillet], default);

        // The product page and the grid must never disagree about a price — which is why the single
        // overload delegates to the page one instead of having its own copy of the logic.
        Assert.Equal(single.Price.Amount, page[0].Price.Amount);
    }

    // ── The catalog reads, end to end through the use case ───────────────────────────────────

    [Fact]
    public async Task The_storefront_list_carries_resolved_prices()
    {
        Product skillet = Skillet();
        SaleCampaign weekend = CatalogFixtures.CampaignOn(skillet.Id, Discount.PercentOff(20m), 0, 999);

        var useCase = new ListProducts(
            new InMemoryProductRepository([skillet], [Kitchen]),
            Pricing([weekend], 60));

        Page<PricedProduct> page = await useCase.ExecuteAsync(null, null, null, default);

        Assert.Equal(Usd(40.00m), page.Items[0].Price.Amount);
        Assert.Equal(20, page.Items[0].Price.SavedPercent);
    }

    [Fact]
    public async Task The_product_page_carries_the_same_resolved_price()
    {
        Product skillet = Skillet();
        SaleCampaign weekend = CatalogFixtures.CampaignOn(skillet.Id, Discount.PercentOff(20m), 0, 999);

        var useCase = new GetProduct(
            new InMemoryProductRepository([skillet], [Kitchen]),
            Pricing([weekend], 60));

        PricedProduct priced = await useCase.ExecuteAsync("skillet", default);

        Assert.Equal(Usd(40.00m), priced.Price.Amount);
        Assert.Same(weekend, priced.Price.Campaign);
    }
}
