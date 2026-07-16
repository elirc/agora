using Agora.Application.Catalog;
using Agora.Application.Errors;
using Agora.Application.Tests.Fakes;
using Agora.Domain;
using Agora.Domain.Access;
using Agora.Domain.Catalog;

namespace Agora.Application.Tests.Catalog;

public class AdminCategoryTests
{
    private static readonly DateTimeOffset Now = CatalogFixtures.Base.AddHours(3);

    [Fact]
    public async Task Create_generates_a_slug_from_the_name()
    {
        var repo = new InMemoryCategoryRepository([]);
        var useCase = new CreateCategory(repo, new CountingUnitOfWork(), StubCurrentUser.SignedIn(Role.StoreAdmin), new FixedClock(Now));

        Category created = await useCase.ExecuteAsync("Kitchen & Dining", slug: null, default);

        Assert.Equal("kitchen-dining", created.Slug.Value);
        Assert.Equal(1, created.Version);
        Assert.Equal(Now, created.CreatedAtUtc);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_slug()
    {
        Category existing = CatalogFixtures.CategoryAt("Kitchen", "kitchen-dining", 0);
        var repo = new InMemoryCategoryRepository([existing]);
        var useCase = new CreateCategory(repo, new CountingUnitOfWork(), StubCurrentUser.SignedIn(Role.StoreAdmin), new FixedClock(Now));

        await Assert.ThrowsAsync<DomainRuleException>(
            () => useCase.ExecuteAsync("Kitchen Again", "kitchen-dining", default));
    }

    [Fact]
    public async Task Create_denies_a_customer()
    {
        var repo = new InMemoryCategoryRepository([]);
        var useCase = new CreateCategory(repo, new CountingUnitOfWork(), StubCurrentUser.SignedIn(Role.Customer), new FixedClock(Now));

        await Assert.ThrowsAsync<ForbiddenException>(() => useCase.ExecuteAsync("Kitchen", null, default));
    }

    [Fact]
    public async Task Update_renames_and_bumps_the_version_when_expected_version_matches()
    {
        Category category = CatalogFixtures.CategoryAt("Kitchen", "kitchen", 0);
        var repo = new InMemoryCategoryRepository([category]);
        var useCase = new UpdateCategory(repo, new CountingUnitOfWork(), StubCurrentUser.SignedIn(Role.StoreAdmin));

        Category updated = await useCase.ExecuteAsync(category.Id, "Kitchen & Dining", expectedVersion: 1, default);

        Assert.Equal("Kitchen & Dining", updated.Name);
        Assert.Equal("kitchen", updated.Slug.Value); // slug unchanged
        Assert.Equal(2, updated.Version);
    }

    [Fact]
    public async Task Update_with_a_stale_version_is_a_conflict()
    {
        Category category = CatalogFixtures.CategoryAt("Kitchen", "kitchen", 0);
        var repo = new InMemoryCategoryRepository([category]);
        var useCase = new UpdateCategory(repo, new CountingUnitOfWork(), StubCurrentUser.SignedIn(Role.StoreAdmin));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => useCase.ExecuteAsync(category.Id, "Renamed", expectedVersion: 99, default));
    }

    [Fact]
    public async Task Update_of_an_unknown_category_is_not_found()
    {
        var repo = new InMemoryCategoryRepository([]);
        var useCase = new UpdateCategory(repo, new CountingUnitOfWork(), StubCurrentUser.SignedIn(Role.StoreAdmin));

        await Assert.ThrowsAsync<NotFoundException>(
            () => useCase.ExecuteAsync(Guid.NewGuid(), "Name", expectedVersion: 1, default));
    }
}
