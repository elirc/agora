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

    public AuthorizationMatrixTests(AgoraApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        // A fresh customer per test (unique email — xunit news this class per test), plus the two
        // seeded privileged accounts. One representative of every role.
        _customer = await _factory.RegisterCustomerAsync($"matrix-cust-{Guid.NewGuid():N}@example.com");
        _admin = await _factory.LoginAsync(AgoraSeeder.StoreAdminEmail, AgoraSeeder.StoreAdminPassword);
        _ops = await _factory.LoginAsync(AgoraSeeder.OpsEmail, AgoraSeeder.OpsPassword);
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
