using Agora.Domain.Access;

namespace Agora.Application.Abstractions;

public interface ISessionRepository
{
    Task AddAsync(Session session, CancellationToken cancellationToken);

    /// <summary>The auth middleware's one query per authenticated request: token hash → session.</summary>
    Task<Session?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);
}
