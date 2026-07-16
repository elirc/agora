using Agora.Application.Catalog;
using Agora.Application.Errors;
using Agora.Application.Tests.Fakes;
using Agora.Domain;
using Agora.Domain.Access;
using Agora.Domain.Catalog;
using Agora.Domain.Values;

namespace Agora.Application.Tests.Catalog;

public class UpdateProductTests
{
    private static readonly Category Kitchen = CatalogFixtures.CategoryAt("Kitchen", "kitchen", 0);
    private static readonly Category Office = CatalogFixtures.CategoryAt("Office", "office", 1);

    private static (UpdateProduct UseCase, Product Product) Build(Role role = Role.StoreAdmin)
    {
        Product product = CatalogFixtures.ProductAt(Kitchen.Id, "Skillet", "skillet", "AGA-K-1", 1);
        var products = new InMemoryProductRepository([product], [Kitchen, Office]);
        var categories = new InMemoryCategoryRepository([Kitchen, Office]);
        var useCase = new UpdateProduct(products, categories, new CountingUnitOfWork(), StubCurrentUser.SignedIn(role));
        return (useCase, product);
    }

    [Fact]
    public async Task Updates_fields_and_bumps_the_version_when_the_expected_version_matches()
    {
        (UpdateProduct useCase, Product product) = Build();

        Product updated = await useCase.ExecuteAsync(
            product.Id, Office.Id, "New Name", "New copy", 42.00m, "USD", expectedVersion: 1, default);

        Assert.Equal("New Name", updated.Name);
        Assert.Equal(Office.Id, updated.CategoryId);
        Assert.Equal(Money.Of(42.00m, CurrencyCode.Usd), updated.Price);
        Assert.Equal(2, updated.Version);
    }

    [Fact]
    public async Task A_stale_expected_version_is_a_conflict_that_carries_the_current_state()
    {
        (UpdateProduct useCase, Product product) = Build();

        // Simulate another admin having already saved: the row is now at version 2.
        product.UpdateDetails(Kitchen.Id, "Someone Elses Edit", "their copy", Money.Of(99m, CurrencyCode.Usd));
        Assert.Equal(2, product.Version);

        // Our admin still presents the version they loaded (1) → conflict, not a silent overwrite.
        ConcurrencyConflictException ex = await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => useCase.ExecuteAsync(product.Id, Kitchen.Id, "My Edit", "my copy", 1m, "USD", expectedVersion: 1, default));

        Assert.Equal("product", ex.Resource);
        Product current = Assert.IsType<Product>(ex.Current);
        Assert.Equal("Someone Elses Edit", current.Name); // the OTHER admin's write survived
        Assert.Equal(2, current.Version);
    }

    [Fact]
    public async Task Unknown_product_is_not_found()
    {
        (UpdateProduct useCase, _) = Build();

        await Assert.ThrowsAsync<NotFoundException>(() => useCase.ExecuteAsync(
            Guid.NewGuid(), Kitchen.Id, "Name", "copy", 1m, "USD", expectedVersion: 1, default));
    }

    [Fact]
    public async Task Moving_to_a_nonexistent_category_is_rejected()
    {
        (UpdateProduct useCase, Product product) = Build();

        await Assert.ThrowsAsync<DomainRuleException>(() => useCase.ExecuteAsync(
            product.Id, Guid.NewGuid(), "Name", "copy", 1m, "USD", expectedVersion: 1, default));
    }

    [Fact]
    public async Task A_customer_cannot_update_a_product()
    {
        (UpdateProduct useCase, Product product) = Build(Role.Customer);

        await Assert.ThrowsAsync<ForbiddenException>(() => useCase.ExecuteAsync(
            product.Id, Kitchen.Id, "Name", "copy", 1m, "USD", expectedVersion: 1, default));
    }
}
