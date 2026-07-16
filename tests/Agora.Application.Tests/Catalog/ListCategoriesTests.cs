using Agora.Application.Catalog;
using Agora.Application.Errors;
using Agora.Application.Pagination;
using Agora.Application.Tests.Fakes;
using Agora.Domain.Catalog;

namespace Agora.Application.Tests.Catalog;

public class ListCategoriesTests
{
    private static ListCategories UseCase(params Category[] categories) =>
        new(new InMemoryCategoryRepository(categories));

    [Fact]
    public async Task Returns_categories_newest_first()
    {
        Category older = CatalogFixtures.CategoryAt("Kitchen & Dining", "kitchen-dining", 1);
        Category newer = CatalogFixtures.CategoryAt("Home Office", "home-office", 2);

        Page<Category> page = await UseCase(older, newer).ExecuteAsync(null, null, default);

        Assert.Equal(["home-office", "kitchen-dining"], page.Items.Select(c => c.Slug.Value));
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task Pages_through_the_collection_via_the_cursor()
    {
        Category[] categories =
        [
            CatalogFixtures.CategoryAt("A", "cat-a", 1),
            CatalogFixtures.CategoryAt("B", "cat-b", 2),
            CatalogFixtures.CategoryAt("C", "cat-c", 3),
        ];
        ListCategories useCase = UseCase(categories);

        Page<Category> first = await useCase.ExecuteAsync(null, limit: 2, default);
        Assert.Equal(["cat-c", "cat-b"], first.Items.Select(c => c.Slug.Value));
        Assert.NotNull(first.NextCursor);

        Page<Category> second = await useCase.ExecuteAsync(first.NextCursor, limit: 2, default);
        Assert.Equal(["cat-a"], second.Items.Select(c => c.Slug.Value));
        Assert.Null(second.NextCursor);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(Page<Category>.MaxLimit + 1)]
    public async Task Rejects_out_of_range_limit(int limit)
    {
        await Assert.ThrowsAsync<RequestValidationException>(
            () => UseCase().ExecuteAsync(null, limit, default));
    }
}
