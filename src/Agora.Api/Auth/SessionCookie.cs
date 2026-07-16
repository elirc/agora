namespace Agora.Api.Auth;

/// <summary>
/// The one place that knows the session cookie's name and attributes. Issue on login/register,
/// clear on logout — always with the SAME options, because a cookie is only deleted by a
/// Set-Cookie whose name/path/attributes match the one that set it.
/// </summary>
public static class SessionCookie
{
    public const string Name = "agora_session";

    // ⚠️ the three attributes that make a session cookie safe:
    //   HttpOnly              — JavaScript cannot read it, so an XSS cannot exfiltrate the session.
    //   Secure                — sent only over HTTPS (localhost counts as a secure context, so dev
    //                           over http://localhost:5158 still works).
    //   SameSite=Lax          — not sent on cross-site subrequests (the CSRF surface), but IS sent
    //                           on top-level navigations, so following a link back into the shop
    //                           keeps you logged in. Strict would break that; None would reopen CSRF.
    private static CookieOptions Options(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        IsEssential = true, // a login cookie is strictly necessary; not subject to consent gating
        Expires = expires,
    };

    public static void Issue(HttpResponse response, string token, DateTimeOffset expiresAtUtc) =>
        response.Cookies.Append(Name, token, Options(expiresAtUtc));

    public static void Clear(HttpResponse response) =>
        // Delete must repeat the defining attributes (Path/Secure/SameSite) or the browser keeps
        // the original cookie. ASP.NET writes an already-expired Set-Cookie from these options.
        response.Cookies.Delete(Name, Options(null));
}
