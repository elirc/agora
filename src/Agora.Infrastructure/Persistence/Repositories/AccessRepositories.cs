using Agora.Application.Abstractions;
using Agora.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(AgoraDbContext db) : IUserRepository
{
    public async Task AddAsync(User user, CancellationToken ct) =>
        await db.Users.AddAsync(user, ct);

    public async Task<User?> FindAsync(Guid id, CancellationToken ct) =>
        await db.Users.FindAsync([id], ct);

    public Task<User?> FindByEmailAsync(EmailAddress email, CancellationToken ct) =>
        // WHY no AsNoTracking here: writes may follow (a future "change password"), and email
        // lookup during login is a read that never mutates but shares the repository with paths
        // that do. The tracked read is cheap at this cardinality; measure before optimizing.
        db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);
}

internal sealed class SessionRepository(AgoraDbContext db) : ISessionRepository
{
    public async Task AddAsync(Session session, CancellationToken ct) =>
        await db.Sessions.AddAsync(session, ct);

    public Task<Session?> FindByTokenHashAsync(string tokenHash, CancellationToken ct) =>
        // The auth middleware calls this on every authenticated request; the unique index on
        // TokenHash (see AccessConfigurations) makes it a single-row point lookup. Logout mutates
        // the returned row, so it is tracked (no AsNoTracking).
        db.Sessions.SingleOrDefaultAsync(s => s.TokenHash == tokenHash, ct);
}
