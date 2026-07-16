using Agora.Application.Abstractions;

namespace Agora.Infrastructure.Persistence;

internal sealed class EfUnitOfWork(AgoraDbContext db) : IUnitOfWork
{
    // LEARN: the port promised "everything the use case changed, atomically" — EF's change
    // tracker + SaveChanges IS that promise for a single DbContext. Register's User and Session
    // are inserted together in one round trip and one transaction. When the outbox arrives
    // (Sprint 10), this one call is where domain events and state changes will join the SAME
    // transaction, which is the whole point of keeping commit explicit and in one place.
    public Task CommitAsync(CancellationToken cancellationToken) =>
        db.SaveChangesAsync(cancellationToken);
}
