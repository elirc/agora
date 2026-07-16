using Agora.Api.Auth;
using Agora.Api.Carts;

namespace Agora.Api.Tests.Infrastructure;

/// <summary>
/// Two-cookie test clients. A shopper can present a session, a cart token, both, or neither — and
/// "both" is the combination the sprint's identity boundary is actually about.
/// </summary>
// LEARN: same reason AuthHelpers hand-rolls its cookies (HandleCookies = false): .NET's
// CookieContainer refuses to echo a `Secure` cookie back over the test host's http://localhost, and
// we want to ASSERT the flags anyway. GOTCHA: HTTP wants ONE `Cookie` header with `; `-separated
// pairs, not two headers — DefaultRequestHeaders.Add called twice produces two, which some servers
// merge and some ignore. Build the string once.
public static class CartHelpers
{
    /// <summary>A client presenting whichever of the two cookies you give it.</summary>
    public static HttpClient AsShopper(
        this AgoraApiFactory factory,
        string? sessionToken = null,
        string? cartToken = null)
    {
        HttpClient client = factory.NewClient();

        List<string> cookies = [];
        if (sessionToken is not null)
        {
            cookies.Add($"{SessionCookie.Name}={sessionToken}");
        }

        if (cartToken is not null)
        {
            cookies.Add($"{CartCookie.Name}={cartToken}");
        }

        if (cookies.Count > 0)
        {
            client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies));
        }

        return client;
    }

    /// <summary>The agora_cart token a response minted, or null if it didn't mint one.</summary>
    // GOTCHA: an EMPTY string here means the cookie was CLEARED (a delete is a Set-Cookie with no
    // value and a past expiry), which is a different fact from "no cart cookie in this response" —
    // and the login tests care about exactly that difference.
    public static string? CartTokenFrom(HttpResponseMessage response)
    {
        string? value = AuthHelpers.CookieValueFrom(response, CartCookie.Name);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>True when the response told the browser to forget its cart token.</summary>
    public static bool CartCookieCleared(HttpResponseMessage response) =>
        AuthHelpers.CookieValueFrom(response, CartCookie.Name) == string.Empty;
}
