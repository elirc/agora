using Agora.Domain.Access;

namespace Agora.Application.Abstractions;

/// <summary>
/// Who is making this request. Built once per request by the Api's session-cookie middleware;
/// consumed by use cases that need to know (or gate on) the caller.
/// </summary>
// LEARN: this is a PORT, not a static. A use case declares "I need to know the caller" in its
// constructor; a test hands it a stub caller; nothing in Application ever touches HttpContext.
// Ambient statics (the old HttpContext.Current style) are exactly how authorization becomes
// untestable — and untested authorization is how the resource-ownership bugs in this course's
// hardening sprint get in. The guard methods live here so "who may do this" is asked the SAME
// way everywhere, instead of a hand-rolled `if (role == ...)` scattered across endpoints.
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>The signed-in user's id. Meaningful only when <see cref="IsAuthenticated"/>.</summary>
    Guid UserId { get; }

    Role? Role { get; }

    /// <summary>Throws <c>UnauthenticatedException</c> (→ 401) when the caller is anonymous.</summary>
    void RequireUser();

    /// <summary>
    /// Requires the caller to hold one of <paramref name="anyOf"/>. Throws
    /// <c>UnauthenticatedException</c> (→ 401) when anonymous, <c>ForbiddenException</c> (→ 403)
    /// when signed in but lacking every listed role. No implicit god-role: Ops passes only when
    /// Ops is listed (roles are capabilities, not a hierarchy — see <see cref="Role"/>).
    /// </summary>
    void RequireRole(params Role[] anyOf);
}
