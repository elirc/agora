using Agora.Application.Abstractions;

namespace Agora.Application.Auth;

public sealed class Logout(
    ISessionRepository sessions,
    ISessionTokens tokens,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task ExecuteAsync(string token, CancellationToken cancellationToken)
    {
        Domain.Access.Session? session =
            await sessions.FindByTokenHashAsync(tokens.HashOf(token), cancellationToken);

        // WHY: revoking an already-dead (or unknown) session succeeds silently. The client's
        // goal — "this token must not work anymore" — is already true, so failing the request
        // would punish a retry for being safe. Revoke is idempotent in the domain; commit always
        // runs so the write lands whether or not there was anything to revoke.
        session?.Revoke(clock.GetUtcNow());

        await unitOfWork.CommitAsync(cancellationToken);
    }
}
