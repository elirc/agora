using Agora.Application.Catalog;
using Agora.Application.Errors;
using Agora.Application.Tests.Fakes;
using Agora.Domain;
using Agora.Domain.Catalog;

namespace Agora.Application.Tests.Catalog;

public class GetCategoryTests
{
    private static readonly Category Kitchen =
        CatalogFixtures.CategoryAt("Kitchen & Dining", "kitchen-dining", 1);

    private static GetCategory UseCase() =>
        new(new InMemoryCategoryRepository([Kitchen]));

    [Fact]
    public async Task Returns_the_category_by_slug_case_insensitively()
    {
        Category found = await UseCase().ExecuteAsync("Kitchen-Dining", default);

        Assert.Equal(Kitchen.Id, found.Id);
    }

    [Fact]
    public async Task Unknown_but_valid_slug_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => UseCase().ExecuteAsync("home-office", default));
    }

    [Fact]
    public async Task Malformed_slug_is_a_domain_rule_violation()
    {
        // WHY: "Not A Slug" is malformed vocabulary (422), not a missing resource (404).
        await Assert.ThrowsAsync<DomainRuleException>(
            () => UseCase().ExecuteAsync("Not A Slug", default));
    }
}
