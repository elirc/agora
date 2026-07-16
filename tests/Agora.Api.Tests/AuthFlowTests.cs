using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Agora.Api.Tests.Infrastructure;
using Agora.Contracts.V1;
using Agora.Domain.Access;
using Agora.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Agora.Api.Tests;

public class AuthFlowTests : IClassFixture<AgoraApiFactory>
{
    private readonly AgoraApiFactory _factory;

    public AuthFlowTests(AgoraApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_login_me_logout_roundtrip()
    {
        // GOTCHA: xunit news a NEW test-class instance per test, but the factory (class fixture)
        // is shared — so emails must be unique per test or the second one 422s on the unique index.
        string email = $"rt-{Guid.NewGuid():N}@example.com";

        // Register → 201, a Customer, and a session cookie with the right flags.
        (HttpResponseMessage register, string token) = await _factory.RegisterAsync(email);
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        string setCookie = AuthHelpers.SetCookieFor(register, "agora_session")!;
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);

        MeDto registered = (await register.Content.ReadFromJsonAsync<MeDto>())!;
        Assert.Equal(email, registered.Email);
        Assert.Equal("Customer", registered.Role);

        // Me: the cookie works.
        MeDto me = (await _factory.As(token).GetFromJsonAsync<MeDto>("/api/v1/auth/me"))!;
        Assert.Equal(email, me.Email);

        // Logout: 204, and the response clears the cookie (an already-expired Set-Cookie).
        HttpResponseMessage logout = await _factory.As(token).PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        string cleared = AuthHelpers.SetCookieFor(logout, "agora_session")!;
        Assert.Contains("expires=", cleared, StringComparison.OrdinalIgnoreCase);

        // LEARN: after revocation the SAME token no longer authenticates — /me is 401. A cookie is
        // only as live as its server-side session.
        HttpResponseMessage meAfter = await _factory.As(token).GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meAfter.StatusCode);
    }

    [Fact]
    public async Task Login_with_the_seeded_store_admin_reports_the_admin_role()
    {
        string token = await _factory.LoginAsync(AgoraSeeder.StoreAdminEmail, AgoraSeeder.StoreAdminPassword);

        MeDto me = (await _factory.As(token).GetFromJsonAsync<MeDto>("/api/v1/auth/me"))!;
        Assert.Equal(AgoraSeeder.StoreAdminEmail, me.Email);
        Assert.Equal("StoreAdmin", me.Role);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_user_are_byte_identical_401s()
    {
        string email = $"enum-{Guid.NewGuid():N}@example.com";
        await _factory.RegisterAsync(email);

        // LEARN: user enumeration defense. Wrong password vs. unknown address must be
        // indistinguishable — after stripping the per-request traceId, the bodies match exactly.
        string wrongPassword = await FailedLoginBody(email, "definitely-not-the-password");
        string unknownUser = await FailedLoginBody($"ghost-{Guid.NewGuid():N}@example.com", "whatever-here");

        Assert.Equal(StripTraceId(wrongPassword), StripTraceId(unknownUser));
    }

    [Fact]
    public async Task Anonymous_me_is_a_401_problem_with_the_unauthenticated_type()
    {
        HttpResponseMessage response = await _factory.NewClient().GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        // WHY no WWW-Authenticate: cookie sessions are not an HTTP auth scheme (see AgoraExceptionHandler).
        Assert.False(response.Headers.Contains("WWW-Authenticate"));

        ProblemShape problem = (await response.Content.ReadFromJsonAsync<ProblemShape>())!;
        Assert.Equal("https://agora.example/problems/unauthenticated", problem.Type);
    }

    [Fact]
    public async Task Duplicate_registration_is_a_422_domain_rule()
    {
        string email = $"dup-{Guid.NewGuid():N}@example.com";
        await _factory.RegisterAsync(email);

        HttpResponseMessage second = await _factory.NewClient().PostAsJsonAsync(
            "/api/v1/auth/register", new RegisterRequest(email.ToUpperInvariant(), "a-long-enough-password"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
    }

    [Fact]
    public async Task A_cookie_for_an_expired_session_is_treated_as_anonymous()
    {
        // Register to get a real user id.
        string email = $"exp-{Guid.NewGuid():N}@example.com";
        (_, string liveToken) = await _factory.RegisterAsync(email);
        MeDto me = (await _factory.As(liveToken).GetFromJsonAsync<MeDto>("/api/v1/auth/me"))!;

        // Craft an already-expired session for that user and present its cookie.
        string expiredToken;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            var tokens = scope.ServiceProvider.GetRequiredService<Application.Abstractions.ISessionTokens>();
            var db = scope.ServiceProvider.GetRequiredService<AgoraDbContext>();

            (string token, string hash) = tokens.Mint();
            expiredToken = token;

            // Started well over a lifetime ago → ExpiresAtUtc is in the past.
            DateTimeOffset longAgo = DateTimeOffset.UtcNow - Session.Lifetime - TimeSpan.FromDays(1);
            db.Sessions.Add(Session.Start(me.Id, hash, longAgo));
            await db.SaveChangesAsync();
        }

        // The middleware finds the session but it is not active → the request stays anonymous → 401.
        HttpResponseMessage response = await _factory.As(expiredToken).GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<string> FailedLoginBody(string email, string password)
    {
        HttpResponseMessage response = await _factory.NewClient().PostAsJsonAsync(
            "/api/v1/auth/login", new LoginRequest(email, password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static string StripTraceId(string body) =>
        Regex.Replace(body, "\"traceId\":\"[^\"]*\"", "\"traceId\":\"-\"");
}
