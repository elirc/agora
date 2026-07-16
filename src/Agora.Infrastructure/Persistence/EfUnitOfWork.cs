using Agora.Application.Abstractions;
using Agora.Application.Errors;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Persistence;

internal sealed class EfUnitOfWork(AgoraDbContext db) : IUnitOfWork
{
    // LEARN: the port promised "everything the use case changed, atomically" — EF's change
    // tracker + SaveChanges IS that promise for a single DbContext. Register's User and Session
    // are inserted together in one round trip and one transaction. When the outbox arrives
    // (Sprint 10), this one call is where domain events and state changes will join the SAME
    // transaction, which is the whole point of keeping commit explicit and in one place.
    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // 🔗 THE ONE PLACE EF's concurrency exception is translated. A Version-token UPDATE that
            // matches zero rows (another writer moved the row after we loaded it) throws this EF type
            // — but EF types must NOT leak past Infrastructure (the architecture test forbids the Api
            // from referencing EntityFrameworkCore outside the composition root). So we re-raise it as
            // the Application's own ConcurrencyConflictException, which the exception handler maps to
            // a 409 exactly like the use case's explicit version check does. Two detection points,
            // one HTTP contract. No current-state snapshot here — this is the narrow post-load race,
            // and the client's recovery is simply to re-fetch. (ADR-0005.)
            string resource = ex.Entries.Count > 0
                ? ex.Entries[0].Entity.GetType().Name.ToLowerInvariant()
                : "record";

            throw new ConcurrencyConflictException(resource);
        }
    }
}
