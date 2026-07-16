using System.Net;
using System.Net.Http.Json;
using Agora.Api.Tests.Infrastructure;
using Agora.Contracts.V1;
using Agora.Infrastructure.Persistence;

namespace Agora.Api.Tests;

/// <summary>
/// The lost-update story told at the HTTP level, plus the soft-archive contract: archived products
/// vanish from the storefront but remain for the admin. Every test drives the real endpoints as the
/// seeded StoreAdmin over in-memory SQLite.
/// </summary>
public class AdminCatalogEndpointsTests : IClassFixture<AgoraApiFactory>, IAsyncLifetime
{
    private readonly AgoraApiFactory _factory;
    private string _admin = null!;
    private Guid _categoryId;

    public AdminCatalogEndpointsTests(AgoraApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _admin = await _factory.LoginAsync(AgoraSeeder.StoreAdminEmail, AgoraSeeder.StoreAdminPassword);
        List<CategoryDto> categories =
            (await _factory.NewClient().GetFromJsonAsync<List<CategoryDto>>("/api/v1/categories"))!;
        _categoryId = categories[0].Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Admin => _factory.As(_admin);

    // 📘 THE LOST UPDATE, END TO END. Two admins load the same product; the second save wins and the
    // first is REJECTED with the current state — instead of silently overwriting the second's work.
    [Fact]
    public async Task Two_concurrent_edits_the_second_wins_and_the_first_gets_a_409_with_current_state()
    {
        AdminProductDto v1 = await CreateProductAsync();

        // Admin A and Admin B both hold v1. Admin B saves first → v2.
        HttpResponseMessage bSaved = await Admin.PutAsJsonAsync(
            $"/api/v1/admin/products/{v1.Id}",
            new UpdateProductRequest(_categoryId, "B's title", "B's copy", 20.00m, "USD", v1.Version));
        Assert.Equal(HttpStatusCode.OK, bSaved.StatusCode);
        AdminProductDto v2 = (await bSaved.Content.ReadFromJsonAsync<AdminProductDto>())!;
        Assert.Equal(v1.Version + 1, v2.Version);

        // Admin A now saves, still presenting v1 → 409 Conflict, NOT a silent overwrite.
        HttpResponseMessage aStale = await Admin.PutAsJsonAsync(
            $"/api/v1/admin/products/{v1.Id}",
            new UpdateProductRequest(_categoryId, "A's title", "A's copy", 30.00m, "USD", v1.Version));

        Assert.Equal(HttpStatusCode.Conflict, aStale.StatusCode);
        Assert.Equal("application/problem+json", aStale.Content.Headers.ContentType?.MediaType);

        ConflictBody? body = await aStale.Content.ReadFromJsonAsync<ConflictBody>();
        Assert.Equal("https://agora.example/problems/conflict", body!.Type);
        // The 409 carries the CURRENT server state so the client can rebase: B's write, at v2.
        Assert.NotNull(body.Current);
        Assert.Equal("B's title", body.Current!.Name);
        Assert.Equal(v2.Version, body.Current.Version);
    }

    [Fact]
    public async Task A_successful_update_increments_the_version_each_time()
    {
        AdminProductDto v1 = await CreateProductAsync();

        AdminProductDto v2 = await UpdateAsync(v1.Id, "Second", v1.Version);
        Assert.Equal(v1.Version + 1, v2.Version);

        AdminProductDto v3 = await UpdateAsync(v1.Id, "Third", v2.Version);
        Assert.Equal(v2.Version + 1, v3.Version);
    }

    [Fact]
    public async Task An_archived_product_is_404_on_the_storefront_but_visible_to_the_admin()
    {
        AdminProductDto product = await CreateProductAsync();
        HttpClient anon = _factory.NewClient();

        // Active: the storefront can see it.
        Assert.Equal(HttpStatusCode.OK,
            (await anon.GetAsync($"/api/v1/products/{product.Slug}")).StatusCode);

        // Archive it.
        HttpResponseMessage archived = await Admin.PostAsJsonAsync(
            $"/api/v1/admin/products/{product.Id}/archive", new ProductVersionRequest(product.Version));
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);

        // Storefront detail is now a 404 — the shopper isn't even told it exists.
        Assert.Equal(HttpStatusCode.NotFound,
            (await anon.GetAsync($"/api/v1/products/{product.Slug}")).StatusCode);

        // Storefront list no longer contains it.
        List<ProductDto> storefront =
            (await anon.GetFromJsonAsync<List<ProductDto>>("/api/v1/products?limit=100"))!;
        Assert.DoesNotContain(storefront, p => p.Slug == product.Slug);

        // Admin list (all + archived) still shows it; active does not.
        Assert.Contains(await AdminListAsync("all"), p => p.Id == product.Id);
        Assert.Contains(await AdminListAsync("archived"), p => p.Id == product.Id);
        Assert.DoesNotContain(await AdminListAsync("active"), p => p.Id == product.Id);

        // Admin GET-by-id sees the archived product directly.
        Assert.Equal(HttpStatusCode.OK,
            (await Admin.GetAsync($"/api/v1/admin/products/{product.Id}")).StatusCode);
    }

    [Fact]
    public async Task Restore_returns_an_archived_product_to_the_storefront()
    {
        AdminProductDto product = await CreateProductAsync();
        HttpResponseMessage archived = await Admin.PostAsJsonAsync(
            $"/api/v1/admin/products/{product.Id}/archive", new ProductVersionRequest(product.Version));
        AdminProductDto archivedDto = (await archived.Content.ReadFromJsonAsync<AdminProductDto>())!;

        HttpResponseMessage restored = await Admin.PostAsJsonAsync(
            $"/api/v1/admin/products/{product.Id}/restore", new ProductVersionRequest(archivedDto.Version));
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await _factory.NewClient().GetAsync($"/api/v1/products/{product.Slug}")).StatusCode);
    }

    [Fact]
    public async Task Create_generates_a_slug_and_the_new_product_appears_on_the_storefront()
    {
        AdminProductDto created = await CreateProductAsync(name: "Brand New Widget");

        Assert.Equal("brand-new-widget", created.Slug);
        Assert.Equal(1, created.Version);

        ProductDto? onStorefront = await _factory.NewClient()
            .GetFromJsonAsync<ProductDto>($"/api/v1/products/{created.Slug}");
        Assert.Equal("Brand New Widget", onStorefront!.Name);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_sku_with_a_422()
    {
        AdminProductDto first = await CreateProductAsync();

        HttpResponseMessage response = await Admin.PostAsJsonAsync(
            "/api/v1/admin/products",
            new CreateProductRequest(_categoryId, "Another", null, first.Sku, "desc", 5m, "USD"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Create_rejects_a_missing_name_with_a_422()
    {
        HttpResponseMessage response = await Admin.PostAsJsonAsync(
            "/api/v1/admin/products",
            new CreateProductRequest(_categoryId, "", null, $"SKU-{Guid.NewGuid():N}"[..20], "desc", 5m, "USD"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_status_filter_is_a_400()
    {
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Admin.GetAsync("/api/v1/admin/products?status=bogus")).StatusCode);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private async Task<AdminProductDto> CreateProductAsync(string? name = null)
    {
        // A unique name (so the generated slug is unique) — these tests share one in-memory DB, and a
        // repeated slug would (correctly) 422. Tests that assert an EXACT slug pass an explicit name.
        name ??= $"Test Product {Guid.NewGuid():N}";
        HttpResponseMessage response = await Admin.PostAsJsonAsync(
            "/api/v1/admin/products",
            new CreateProductRequest(
                _categoryId, name, null, $"SKU-{Guid.NewGuid():N}"[..20], "A fine thing.", 9.99m, "USD"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AdminProductDto>())!;
    }

    private async Task<AdminProductDto> UpdateAsync(Guid id, string name, int expectedVersion)
    {
        HttpResponseMessage response = await Admin.PutAsJsonAsync(
            $"/api/v1/admin/products/{id}",
            new UpdateProductRequest(_categoryId, name, "copy", 11.00m, "USD", expectedVersion));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AdminProductDto>())!;
    }

    private async Task<List<AdminProductDto>> AdminListAsync(string status) =>
        (await Admin.GetFromJsonAsync<List<AdminProductDto>>($"/api/v1/admin/products?status={status}&limit=100"))!;

    private sealed record ConflictBody(string Type, string Title, int Status, AdminProductDto? Current);
}
