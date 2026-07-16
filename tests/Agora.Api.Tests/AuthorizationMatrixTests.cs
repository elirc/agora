using System.Net;
using System.Net.Http.Json;
using Agora.Api.Tests.Infrastructure;
using Agora.Contracts.V1;
using Agora.Infrastructure.Persistence;

namespace Agora.Api.Tests;

/// <summary>
/// The authorization matrix: persona × endpoint → expected status. Every endpoint the API serves
/// has a row here, and every NEW endpoint from S03 on MUST add its rows (a CONTRIBUTING rule; the
/// S13 hardening sprint audits this table for gaps).
/// </summary>
// 🔍 reviewers read the DENY cases first. The 200s tell you what a feature DOES; the 401/403/404s
// tell you what the system PROTECTS. A matrix like this turns "did we remember authz?" from a
// memory exercise into a diff you can review. Note what S02 does NOT have yet: a single 403. No
// endpoint distinguishes roles this sprint — catalog is public, /me and /logout need only *a*
// session — so the role dimension shows up as 401-vs-authenticated, not 403. The first 403 rows
// arrive in S03 with StoreAdmin-only catalog writes; the table is built to grow into them.
//
// The `Logout_requires_a_session` anonymous row is this sprint's RED→GREEN lesson: it expected 401
// while the endpoint still lacked `RequireUser()`, so it FAILED — the matrix caught a missing
// guard the way it is supposed to — and the guard commit turned it green.
public class AuthorizationMatrixTests : IClassFixture<AgoraApiFactory>, IAsyncLifetime
{
    private readonly AgoraApiFactory _factory;

    private string _customer = null!;
    private string _admin = null!;
    private string _ops = null!;
    private Guid _categoryId;

    public AuthorizationMatrixTests(AgoraApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        // A fresh customer per test (unique email — xunit news this class per test), plus the two
        // seeded privileged accounts. One representative of every role.
        _customer = await _factory.RegisterCustomerAsync($"matrix-cust-{Guid.NewGuid():N}@example.com");
        _admin = await _factory.LoginAsync(AgoraSeeder.StoreAdminEmail, AgoraSeeder.StoreAdminPassword);
        _ops = await _factory.LoginAsync(AgoraSeeder.OpsEmail, AgoraSeeder.OpsPassword);

        // A real category to hang admin-write rows off (the seeded catalog always has these).
        List<CategoryDto> categories =
            (await _factory.NewClient().GetFromJsonAsync<List<CategoryDto>>("/api/v1/categories"))!;
        _categoryId = categories[0].Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Public catalog reads (S01): open to everyone, signed in or not ───────────────────────

    public static IEnumerable<object?[]> AllPersonas() =>
        [[null], ["customer"], ["admin"], ["ops"]];

    [Theory]
    [MemberData(nameof(AllPersonas))]
    public async Task Catalog_reads_are_public(string? persona)
    {
        HttpClient client = As(persona);

        string[] publicPaths =
        [
            "/api/v1/categories",
            "/api/v1/categories/kitchen-dining",
            "/api/v1/products",
            "/api/v1/products/cast-iron-skillet-10-inch",
        ];

        foreach (string path in publicPaths)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
        }
    }

    // ── GET /api/v1/auth/me: any session, no session denied ──────────────────────────────────

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("customer", HttpStatusCode.OK)]
    [InlineData("admin", HttpStatusCode.OK)]
    [InlineData("ops", HttpStatusCode.OK)]
    public async Task Me_requires_a_session(string? persona, HttpStatusCode expected)
    {
        Assert.Equal(expected, (await As(persona).GetAsync("/api/v1/auth/me")).StatusCode);
    }

    // ── POST /api/v1/auth/logout: any session, no session denied ─────────────────────────────

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)] // the RED→GREEN row
    [InlineData("customer", HttpStatusCode.NoContent)]
    [InlineData("admin", HttpStatusCode.NoContent)]
    [InlineData("ops", HttpStatusCode.NoContent)]
    public async Task Logout_requires_a_session(string? persona, HttpStatusCode expected)
    {
        Assert.Equal(expected, (await As(persona).PostAsync("/api/v1/auth/logout", null)).StatusCode);
    }

    // ── POST /api/v1/auth/register & /login: public entry points ─────────────────────────────

    [Fact]
    public async Task Register_is_public_and_creates_an_account()
    {
        HttpResponseMessage response = await _factory.NewClient().PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest($"matrix-reg-{Guid.NewGuid():N}@example.com", "a-long-enough-password"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Login_is_public_and_valid_credentials_are_200()
    {
        HttpResponseMessage response = await _factory.NewClient().PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(AgoraSeeder.CustomerEmail, AgoraSeeder.CustomerPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_bad_credentials_is_401()
    {
        HttpResponseMessage response = await _factory.NewClient().PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(AgoraSeeder.CustomerEmail, "not-the-right-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Admin catalog (S03): the FIRST 403 rows. StoreAdmin-only writes ──────────────────────────
    //
    // 🔍 read the deny rows first. Every admin endpoint denies anonymous with 401 (we don't know you)
    // and every non-admin signed-in role with 403 (we know you, you still may not) — including Ops,
    // because roles are capabilities, not a hierarchy: fulfillment is not back-office. Only StoreAdmin
    // gets a 2xx. The guard lives in each use case's RequireRole and is proven, endpoint by endpoint,
    // right here. The 401-before-403 order (anonymous is never "forbidden") is the same rule S02 set.

    public static IEnumerable<object?[]> AdminPersonaExpectations(int okStatus) =>
    [
        [null, HttpStatusCode.Unauthorized],
        ["customer", HttpStatusCode.Forbidden],
        ["ops", HttpStatusCode.Forbidden],
        ["admin", (HttpStatusCode)okStatus],
    ];

    public static IEnumerable<object?[]> AdminGet() => AdminPersonaExpectations(200);
    public static IEnumerable<object?[]> AdminCreate() => AdminPersonaExpectations(201);
    public static IEnumerable<object?[]> AdminMutate() => AdminPersonaExpectations(200);

    [Theory]
    [MemberData(nameof(AdminGet))]
    public async Task Admin_product_list_is_StoreAdmin_only(string? persona, HttpStatusCode expected)
    {
        Assert.Equal(expected, (await As(persona).GetAsync("/api/v1/admin/products")).StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminCreate))]
    public async Task Create_product_is_StoreAdmin_only(string? persona, HttpStatusCode expected)
    {
        HttpResponseMessage response = await As(persona)
            .PostAsJsonAsync("/api/v1/admin/products", NewProductRequest());

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminCreate))]
    public async Task Create_category_is_StoreAdmin_only(string? persona, HttpStatusCode expected)
    {
        HttpResponseMessage response = await As(persona).PostAsJsonAsync(
            "/api/v1/admin/categories",
            new CreateCategoryRequest($"Cat {Guid.NewGuid():N}", null));

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminGet))]
    public async Task Admin_get_product_is_StoreAdmin_only(string? persona, HttpStatusCode expected)
    {
        AdminProductDto product = await CreateProductAsAdminAsync();

        Assert.Equal(expected,
            (await As(persona).GetAsync($"/api/v1/admin/products/{product.Id}")).StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminMutate))]
    public async Task Update_product_is_StoreAdmin_only(string? persona, HttpStatusCode expected)
    {
        AdminProductDto product = await CreateProductAsAdminAsync();
        var body = new UpdateProductRequest(_categoryId, "Renamed", "copy", 12.00m, "USD", product.Version);

        Assert.Equal(expected,
            (await As(persona).PutAsJsonAsync($"/api/v1/admin/products/{product.Id}", body)).StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminMutate))]
    public async Task Update_category_is_StoreAdmin_only(string? persona, HttpStatusCode expected)
    {
        HttpResponseMessage created = await _factory.As(_admin).PostAsJsonAsync(
            "/api/v1/admin/categories", new CreateCategoryRequest($"Cat {Guid.NewGuid():N}", null));
        AdminCategoryDto category = (await created.Content.ReadFromJsonAsync<AdminCategoryDto>())!;

        var body = new UpdateCategoryRequest($"Renamed {Guid.NewGuid():N}", category.Version);

        Assert.Equal(expected,
            (await As(persona).PutAsJsonAsync($"/api/v1/admin/categories/{category.Id}", body)).StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminMutate))]
    public async Task Archive_product_is_StoreAdmin_only(string? persona, HttpStatusCode expected)
    {
        AdminProductDto product = await CreateProductAsAdminAsync();

        Assert.Equal(expected, (await As(persona).PostAsJsonAsync(
            $"/api/v1/admin/products/{product.Id}/archive",
            new ProductVersionRequest(product.Version))).StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminMutate))]
    public async Task Restore_product_is_StoreAdmin_only(string? persona, HttpStatusCode expected)
    {
        AdminProductDto product = await CreateProductAsAdminAsync();
        HttpResponseMessage archived = await _factory.As(_admin).PostAsJsonAsync(
            $"/api/v1/admin/products/{product.Id}/archive", new ProductVersionRequest(product.Version));
        AdminProductDto archivedDto = (await archived.Content.ReadFromJsonAsync<AdminProductDto>())!;

        Assert.Equal(expected, (await As(persona).PostAsJsonAsync(
            $"/api/v1/admin/products/{product.Id}/restore",
            new ProductVersionRequest(archivedDto.Version))).StatusCode);
    }

    // ── Admin pricing (S04): sale campaigns are catalog writes, so the same StoreAdmin-only rule ──
    //
    // 🔍 nothing new to argue about here, and THAT is the point worth noticing. S03 established that
    // /admin/* is StoreAdmin-only with Ops in the deny set; S04 adds three routes and they inherit the
    // rule by using the same RequireRole in the same place, so their rows drop straight into the same
    // member-data helpers. A sprint that had to invent a NEW authorization shape for its endpoints
    // would be telling you something had gone wrong with the old one.
    //
    // Note also which S04 endpoint is NOT in this table: there isn't one. Sale prices ride the
    // existing public catalog reads (covered by Catalog_reads_are_public above) rather than a new
    // route, because a shopper asking "what does this cost?" is the same question they always asked —
    // it just has a better answer now.

    [Theory]
    [MemberData(nameof(AdminGet))]
    public async Task List_campaigns_is_StoreAdmin_only(string? persona, HttpStatusCode expected)
    {
        AdminProductDto product = await CreateProductAsAdminAsync();

        Assert.Equal(expected,
            (await As(persona).GetAsync($"/api/v1/admin/products/{product.Id}/campaigns")).StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminCreate))]
    public async Task Create_campaign_is_StoreAdmin_only(string? persona, HttpStatusCode expected)
    {
        AdminProductDto product = await CreateProductAsAdminAsync();

        HttpResponseMessage response = await As(persona).PostAsJsonAsync(
            $"/api/v1/admin/products/{product.Id}/campaigns",
            new CreateSaleCampaignRequest(
                "Weekend Sale", "PercentOff", 20m,
                DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1)));

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminMutate))]
    public async Task End_campaign_is_StoreAdmin_only(string? persona, HttpStatusCode expected)
    {
        AdminProductDto product = await CreateProductAsAdminAsync();
        AdminSaleCampaignDto campaign = await CreateCampaignAsAdminAsync(product.Id);

        Assert.Equal(expected,
            (await As(persona).PostAsync($"/api/v1/admin/campaigns/{campaign.Id}/end", null)).StatusCode);
    }

    private CreateProductRequest NewProductRequest() => new(
        _categoryId, $"Product {Guid.NewGuid():N}", null,
        $"SKU-{Guid.NewGuid():N}"[..20], "A fine thing.", 9.99m, "USD");

    private async Task<AdminSaleCampaignDto> CreateCampaignAsAdminAsync(Guid productId)
    {
        HttpResponseMessage response = await _factory.As(_admin).PostAsJsonAsync(
            $"/api/v1/admin/products/{productId}/campaigns",
            new CreateSaleCampaignRequest(
                "Weekend Sale", "PercentOff", 20m,
                DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1)));

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AdminSaleCampaignDto>())!;
    }

    private async Task<AdminProductDto> CreateProductAsAdminAsync()
    {
        HttpResponseMessage response = await _factory.As(_admin)
            .PostAsJsonAsync("/api/v1/admin/products", NewProductRequest());
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AdminProductDto>())!;
    }

    private HttpClient As(string? persona) => _factory.As(TokenFor(persona));

    private string? TokenFor(string? persona) => persona switch
    {
        null => null,
        "customer" => _customer,
        "admin" => _admin,
        "ops" => _ops,
        _ => throw new ArgumentOutOfRangeException(nameof(persona)),
    };
}
