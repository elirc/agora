using System.Net.Http.Json;
using Agora.Api.Auth;
using Agora.Contracts.V1;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Agora.Api.Tests.Infrastructure;

/// <summary>
/// Cookie-session test helpers. We deliberately DON'T let the HttpClient manage cookies
/// (<c>HandleCookies = false</c>): the .NET CookieContainer refuses to echo a <c>Secure</c> cookie
/// back over the test host's http://localhost, and we also want to ASSERT the Set-Cookie flags by
/// hand. So we read the token out of Set-Cookie and attach it as a Cookie header ourselves.
/// </summary>
public static class AuthHelpers
{
    public static HttpClient NewClient(this AgoraApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    /// <summary>A client that presents <paramref name="token"/> as the session cookie on every request.</summary>
    public static HttpClient As(this AgoraApiFactory factory, string? token)
    {
        HttpClient client = factory.NewClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Name}={token}");
        }

        return client;
    }

    public static async Task<(HttpResponseMessage Response, string Token)> RegisterAsync(
        this AgoraApiFactory factory, string email, string password = "a-long-enough-password")
    {
        HttpResponseMessage response = await factory.NewClient()
            .PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, password));
        response.EnsureSuccessStatusCode();
        return (response, SessionTokenFrom(response));
    }

    public static async Task<string> RegisterCustomerAsync(
        this AgoraApiFactory factory, string email, string password = "a-long-enough-password") =>
        (await factory.RegisterAsync(email, password)).Token;

    public static async Task<string> LoginAsync(
        this AgoraApiFactory factory, string email, string password)
    {
        HttpResponseMessage response = await factory.NewClient()
            .PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        return SessionTokenFrom(response);
    }

    /// <summary>Pulls the session token value out of a response's Set-Cookie header.</summary>
    public static string SessionTokenFrom(HttpResponseMessage response)
    {
        string cookie = SetCookieFor(response, SessionCookie.Name)
            ?? throw new InvalidOperationException("No agora_session cookie was set.");

        // "agora_session=<token>; path=/; ..." → the value between '=' and the first ';'.
        int eq = cookie.IndexOf('=');
        int semi = cookie.IndexOf(';', eq);
        return semi < 0 ? cookie[(eq + 1)..] : cookie[(eq + 1)..semi];
    }

    public static string? SetCookieFor(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies))
        {
            return null;
        }

        return cookies.FirstOrDefault(c => c.StartsWith(name + "=", StringComparison.Ordinal));
    }
}
