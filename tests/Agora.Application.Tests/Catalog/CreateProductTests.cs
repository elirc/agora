using Agora.Application.Catalog;
using Agora.Application.Tests.Fakes;
using Agora.Domain;
using Agora.Domain.Access;
using Agora.Domain.Catalog;

namespace Agora.Application.Tests.Catalog;

public class CreateProductTests
{
    private static readonly Category Kitchen = CatalogFixtures.CategoryAt("Kitchen", "kitchen", 0);
    private static readonly DateTimeOffset Now = CatalogFixtures.Base.AddHours(1);

    private static (CreateProduct UseCase, InMemoryProductRepository Products) Build(
        Role role = Role.StoreAdmin, params Product[] existing)
    {
        var products = new InMemoryProductRepository(existing, [Kitchen]);
        var categories = new InMemoryCategoryRepository([Kitchen]);
        var useCase = new CreateProduct(
            products, categories, new CountingUnitOfWork(),
            StubCurrentUser.SignedIn(role), new FixedClock(Now));
        return (useCase, products);
    }

    [Fact]
    public async Task Creates_a_product_and_generates_the_slug_from_the_name()
    {
        (CreateProduct useCase, InMemoryProductRepository products) = Build();

        Product created = await useCase.ExecuteAsync(
            Kitchen.Id, "Cast Iron Skillet", slug: null, "AGA-KIT-002", "Pre-seasoned.", 34.50m, "USD", default);

        Assert.Equal("cast-iron-skillet", created.Slug.Value);
        Assert.Equal("AGA-KIT-002", created.Sku.Value);
        Assert.Equal(1, created.Version);
        Assert.False(created.IsArchived);
        Assert.Equal(Now, created.CreatedAtUtc);
        Assert.NotNull(await products.FindBySlugAsync(created.Slug, ProductStatusFilter.Active, default));
    }

    [Fact]
    public async Task Honours_an_explicit_slug_when_supplied()
    {
        (CreateProduct useCase, _) = Build();

        Product created = await useCase.ExecuteAsync(
            Kitchen.Id, "Cast Iron Skillet", "the-good-skillet", "AGA-KIT-002", "desc", 10m, "USD", default);

        Assert.Equal("the-good-skillet", created.Slug.Value);
    }

    [Fact]
    public async Task Rejects_a_missing_category()
    {
        (CreateProduct useCase, _) = Build();

        await Assert.ThrowsAsync<DomainRuleException>(() => useCase.ExecuteAsync(
            Guid.NewGuid(), "Name", null, "AGA-X-1", "desc", 1m, "USD", default));
    }

    [Fact]
    public async Task Rejects_a_duplicate_slug_even_when_the_holder_is_archived()
    {
        Product archived = CatalogFixtures.ProductAt(Kitchen.Id, "Old Skillet", "cast-iron-skillet", "AGA-OLD-1", 1);
        archived.Archive(Now);

        (CreateProduct useCase, _) = Build(Role.StoreAdmin, archived);

        // The archived product still owns "cast-iron-skillet" — reusing it would collide on restore.
        DomainRuleException ex = await Assert.ThrowsAsync<DomainRuleException>(() => useCase.ExecuteAsync(
            Kitchen.Id, "New Skillet", "cast-iron-skillet", "AGA-NEW-1", "desc", 1m, "USD", default));
        Assert.Contains("slug", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_a_duplicate_sku()
    {
        Product existing = CatalogFixtures.ProductAt(Kitchen.Id, "Skillet", "skillet", "AGA-KIT-002", 1);

        (CreateProduct useCase, _) = Build(Role.StoreAdmin, existing);

        DomainRuleException ex = await Assert.ThrowsAsync<DomainRuleException>(() => useCase.ExecuteAsync(
            Kitchen.Id, "Another", "another", "AGA-KIT-002", "desc", 1m, "USD", default));
        Assert.Contains("SKU", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
