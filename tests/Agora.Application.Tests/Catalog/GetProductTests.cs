using Agora.Application.Catalog;
using Agora.Application.Errors;
using Agora.Application.Pricing;
using Agora.Application.Tests.Fakes;
using Agora.Domain;
using Agora.Domain.Catalog;

namespace Agora.Application.Tests.Catalog;

public class GetProductTests
{
    private static readonly Category Kitchen = CatalogFixtures.CategoryAt("Kitchen", "kitchen", 0);
    private static readonly Product Skillet =
        CatalogFixtures.ProductAt(Kitchen.Id, "Cast Iron Skillet", "cast-iron-skillet", "AGA-KIT-002", 1);

    private static GetProduct UseCase() =>
        new(
            new InMemoryProductRepository([Skillet], [Kitchen]),
            new ProductPricing(new InMemorySaleCampaignRepository(), new FixedClock(CatalogFixtures.Base)));

    [Fact]
    public async Task Returns_the_product_by_slug()
    {
        PricedProduct found = await UseCase().ExecuteAsync("cast-iron-skillet", default);

        Assert.Equal(Skillet.Id, found.Product.Id);
        // With no campaign running, the effective price IS the list price — the resolver's answer for
        // "nothing applies", not a special case anyone had to remember to write.
        Assert.Equal(Skillet.Price, found.Price.Amount);
        Assert.False(found.Price.IsOnSale);
    }

    [Fact]
    public async Task Unknown_but_valid_slug_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => UseCase().ExecuteAsync("bamboo-cutting-board", default));
    }

    [Fact]
    public async Task Malformed_slug_is_a_domain_rule_violation()
    {
        await Assert.ThrowsAsync<DomainRuleException>(
            () => UseCase().ExecuteAsync("Bamboo Board", default));
    }
}
