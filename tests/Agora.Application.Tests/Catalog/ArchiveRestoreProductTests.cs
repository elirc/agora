using Agora.Application.Catalog;
using Agora.Application.Errors;
using Agora.Application.Tests.Fakes;
using Agora.Domain.Access;
using Agora.Domain.Catalog;

namespace Agora.Application.Tests.Catalog;

public class ArchiveRestoreProductTests
{
    private static readonly Category Kitchen = CatalogFixtures.CategoryAt("Kitchen", "kitchen", 0);
    private static readonly DateTimeOffset Now = CatalogFixtures.Base.AddHours(2);

    private static (ArchiveProduct Archive, RestoreProduct Restore, Product Product, InMemoryProductRepository Repo) Build(
        Role role = Role.StoreAdmin)
    {
        Product product = CatalogFixtures.ProductAt(Kitchen.Id, "Skillet", "skillet", "AGA-K-1", 1);
        var repo = new InMemoryProductRepository([product], [Kitchen]);
        var uow = new CountingUnitOfWork();
        var user = StubCurrentUser.SignedIn(role);
        return (
            new ArchiveProduct(repo, uow, user, new FixedClock(Now)),
            new RestoreProduct(repo, uow, user),
            product,
            repo);
    }

    [Fact]
    public async Task Archive_hides_the_product_from_the_storefront_but_keeps_it_for_admin()
    {
        (ArchiveProduct archive, _, Product product, InMemoryProductRepository repo) = Build();

        Product archived = await archive.ExecuteAsync(product.Id, expectedVersion: 1, default);

        Assert.True(archived.IsArchived);
        Assert.Equal(2, archived.Version);
        // Gone from the storefront view…
        Assert.Null(await repo.FindBySlugAsync(product.Slug, ProductStatusFilter.Active, default));
        // …still there for the admin.
        Assert.NotNull(await repo.FindByIdAsync(product.Id, default));
    }

    [Fact]
    public async Task Restore_returns_the_product_to_the_storefront()
    {
        (ArchiveProduct archive, RestoreProduct restore, Product product, InMemoryProductRepository repo) = Build();
        await archive.ExecuteAsync(product.Id, expectedVersion: 1, default);

        Product restored = await restore.ExecuteAsync(product.Id, expectedVersion: 2, default);

        Assert.False(restored.IsArchived);
        Assert.Equal(3, restored.Version);
        Assert.NotNull(await repo.FindBySlugAsync(product.Slug, ProductStatusFilter.Active, default));
    }

    [Fact]
    public async Task Archive_with_a_stale_version_is_a_conflict()
    {
        (ArchiveProduct archive, _, Product product, _) = Build();

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => archive.ExecuteAsync(product.Id, expectedVersion: 99, default));
    }

    [Fact]
    public async Task A_customer_cannot_archive()
    {
        (ArchiveProduct archive, _, Product product, _) = Build(Role.Customer);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => archive.ExecuteAsync(product.Id, expectedVersion: 1, default));
    }
}
