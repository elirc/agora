using Agora.Application.Catalog;
using Agora.Application.Errors;
using Agora.Application.Pagination;
using Agora.Application.Tests.Fakes;
using Agora.Domain;
using Agora.Domain.Catalog;

namespace Agora.Application.Tests.Catalog;

public class ListProductsTests
{
    private static readonly Category Kitchen = CatalogFixtures.CategoryAt("Kitchen", "kitchen", 0);
    private static readonly Category Office = CatalogFixtures.CategoryAt("Office", "office", 0);

    private static readonly Product K1 =
        CatalogFixtures.ProductAt(Kitchen.Id, "Skillet", "skillet", "AGA-K-1", 1);
    private static readonly Product K2 =
        CatalogFixtures.ProductAt(Kitchen.Id, "Board", "board", "AGA-K-2", 2);
    private static readonly Product K3 =
        CatalogFixtures.ProductAt(Kitchen.Id, "Plates", "plates", "AGA-K-3", 3);
    private static readonly Product O1 =
        CatalogFixtures.ProductAt(Office.Id, "Riser", "riser", "AGA-O-1", 4);

    private static ListProducts UseCase() =>
        new(new InMemoryProductRepository([K1, K2, K3, O1], [Kitchen, Office]));

    [Fact]
    public async Task Returns_all_products_newest_first()
    {
        Page<Product> page = await UseCase().ExecuteAsync(null, null, null, default);

        Assert.Equal(["riser", "plates", "board", "skillet"], page.Items.Select(p => p.Slug.Value));
    }

    [Fact]
    public async Task Filters_to_one_category_by_slug()
    {
        Page<Product> page = await UseCase().ExecuteAsync("kitchen", null, null, default);

        Assert.Equal(["plates", "board", "skillet"], page.Items.Select(p => p.Slug.Value));
        Assert.DoesNotContain(page.Items, p => p.Slug.Value == "riser");
    }

    [Fact]
    public async Task Unknown_but_valid_category_yields_an_empty_page()
    {
        Page<Product> page = await UseCase().ExecuteAsync("garden", null, null, default);

        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task Malformed_category_filter_is_a_domain_rule_violation()
    {
        // WHY: "Not A Slug" is not a filter value the vocabulary accepts — same 422 rule as a
        // malformed path slug, applied to the query param.
        await Assert.ThrowsAsync<DomainRuleException>(
            () => UseCase().ExecuteAsync("Not A Slug", null, null, default));
    }

    [Fact]
    public async Task Pages_through_the_collection_via_the_cursor()
    {
        ListProducts useCase = UseCase();

        Page<Product> first = await useCase.ExecuteAsync(null, null, limit: 2, default);
        Assert.Equal(["riser", "plates"], first.Items.Select(p => p.Slug.Value));
        Assert.NotNull(first.NextCursor);

        Page<Product> second = await useCase.ExecuteAsync(null, first.NextCursor, limit: 2, default);
        Assert.Equal(["board", "skillet"], second.Items.Select(p => p.Slug.Value));
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task Rejects_out_of_range_limit()
    {
        await Assert.ThrowsAsync<RequestValidationException>(
            () => UseCase().ExecuteAsync(null, null, limit: 0, default));
    }

    [Fact]
    public async Task Rejects_a_garbage_cursor()
    {
        await Assert.ThrowsAsync<RequestValidationException>(
            () => UseCase().ExecuteAsync(null, "not-a-cursor!!!", null, default));
    }
}
