using Agora.Application.Catalog;
using Agora.Application.Errors;
using Agora.Application.Pagination;
using Agora.Application.Tests.Fakes;
using Agora.Domain.Access;
using Agora.Domain.Catalog;

namespace Agora.Application.Tests.Catalog;

public class ListAdminProductsTests
{
    private static readonly Category Kitchen = CatalogFixtures.CategoryAt("Kitchen", "kitchen", 0);

    private static readonly Product Active =
        CatalogFixtures.ProductAt(Kitchen.Id, "Skillet", "skillet", "AGA-K-1", 1);

    private static Product ArchivedProduct()
    {
        Product p = CatalogFixtures.ProductAt(Kitchen.Id, "Old Board", "old-board", "AGA-K-2", 2);
        p.Archive(CatalogFixtures.Base.AddHours(1));
        return p;
    }

    private static ListAdminProducts UseCase(Role role, out Product archived)
    {
        archived = ArchivedProduct();
        var repo = new InMemoryProductRepository([Active, archived], [Kitchen]);
        return new ListAdminProducts(repo, StubCurrentUser.SignedIn(role));
    }

    [Fact]
    public async Task All_includes_archived_products()
    {
        ListAdminProducts useCase = UseCase(Role.StoreAdmin, out _);

        Page<Product> page = await useCase.ExecuteAsync(ProductStatusFilter.All, null, null, default);

        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public async Task Active_excludes_archived_and_Archived_shows_only_archived()
    {
        ListAdminProducts activeCase = UseCase(Role.StoreAdmin, out Product archived);
        Page<Product> active = await activeCase.ExecuteAsync(ProductStatusFilter.Active, null, null, default);
        Assert.DoesNotContain(active.Items, p => p.Id == archived.Id);

        ListAdminProducts archivedCase = UseCase(Role.StoreAdmin, out Product archived2);
        Page<Product> archivedPage = await archivedCase.ExecuteAsync(ProductStatusFilter.Archived, null, null, default);
        Assert.All(archivedPage.Items, p => Assert.True(p.IsArchived));
        Assert.Contains(archivedPage.Items, p => p.Id == archived2.Id);
    }

    [Fact]
    public async Task A_customer_cannot_list_admin_products()
    {
        ListAdminProducts useCase = UseCase(Role.Customer, out _);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => useCase.ExecuteAsync(ProductStatusFilter.All, null, null, default));
    }

    [Fact]
    public async Task An_anonymous_caller_is_unauthenticated()
    {
        var repo = new InMemoryProductRepository([Active], [Kitchen]);
        var useCase = new ListAdminProducts(repo, StubCurrentUser.Anonymous());

        await Assert.ThrowsAsync<UnauthenticatedException>(
            () => useCase.ExecuteAsync(ProductStatusFilter.All, null, null, default));
    }
}
