using Agora.Application.Abstractions;
using Agora.Application.Errors;
using Agora.Domain.Access;

namespace Agora.Api.Auth;

/// <summary>
/// The per-request implementation of <see cref="ICurrentUser"/>. Populated at most once by
/// <see cref="SessionCookieAuthenticator"/>, read-only to every use case downstream.
/// </summary>
public sealed class RequestCurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; private set; }
    public Guid UserId { get; private set; }
    public Role? Role { get; private set; }

    internal void Attach(User user)
    {
        IsAuthenticated = true;
        UserId = user.Id;
        Role = user.Role;
    }

    public void RequireUser()
    {
        if (!IsAuthenticated)
        {
            throw new UnauthenticatedException();
        }
    }

    public void RequireRole(params Role[] anyOf)
    {
        // WHY order matters: authentication FIRST (401), authorization SECOND (403). An anonymous
        // caller is never "forbidden" — they are simply unknown, and the honest status says so.
        RequireUser();

        // No implicit Ops god-mode: a caller passes only if they hold one of the LISTED roles.
        if (Role is not { } role || !anyOf.Contains(role))
        {
            throw new ForbiddenException();
        }
    }
}
