using Agora.Application.Abstractions;

namespace Agora.Api.Auth;

/// <summary>
/// Resolves the <c>agora_session</c> cookie into the request's <see cref="RequestCurrentUser"/>.
/// No cookie → anonymous. A cookie whose session is unknown, expired, or revoked → ALSO anonymous.
/// </summary>
// LEARN: this is the deliberate cookie-vs-bearer difference. A bearer token is PRESENTED on
// purpose, so a bad one is an error (401). A cookie is AMBIENT — the browser attaches it to every
// request, including public catalog GETs — so a stale cookie must degrade to anonymous, not 401,
// or a shopper whose week-old session lapsed couldn't even browse. The endpoints that actually
// need a user (/me, /logout) still return 401; the SPA sees that and sends the shopper to /login.
// So: fail OPEN to anonymous here, and let the PROTECTED endpoints fail CLOSED. (ADR-0004.)
public sealed class SessionCookieAuthenticator(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext http,
        RequestCurrentUser currentUser,
        ISessionTokens tokens,
        ISessionRepository sessions,
        IUserRepository users,
        TimeProvider clock)
    {
        // WHY the early return: no cookie means no database round trip. Anonymous catalog browsing
        // — the common case — pays nothing for auth it isn't using.
        if (http.Request.Cookies.TryGetValue(SessionCookie.Name, out string? token)
            && !string.IsNullOrEmpty(token))
        {
            var session = await sessions.FindByTokenHashAsync(tokens.HashOf(token), http.RequestAborted);

            if (session is not null && session.IsActive(clock.GetUtcNow()))
            {
                var user = await users.FindAsync(session.UserId, http.RequestAborted);
                if (user is not null)
                {
                    currentUser.Attach(user);
                }
            }
        }

        await next(http);
    }
}
