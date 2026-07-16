using Agora.Application.Abstractions;
using Agora.Application.Errors;
using Agora.Domain.Access;

namespace Agora.Application.Auth;

/// <summary>
/// Resolves the caller for <c>GET /api/v1/auth/me</c>. Requires an authenticated session and
/// re-reads the user from the store — the middleware attached an id, this returns the record.
/// </summary>
public sealed class GetCurrentUser(ICurrentUser currentUser, IUserRepository users)
{
    public async Task<User> ExecuteAsync(CancellationToken cancellationToken)
    {
        // The gate is a policy call, not an inline if — the same RequireUser() every future
        // protected use case will make. Anonymous → 401 before any query runs.
        currentUser.RequireUser();

        // WHY re-read instead of trusting the middleware's snapshot: the record is the source of
        // truth. If the account were disabled mid-session (a future feature), a missing row here
        // is the honest answer — the token resolved, the user no longer does → 401.
        return await users.FindAsync(currentUser.UserId, cancellationToken)
            ?? throw new UnauthenticatedException();
    }
}
