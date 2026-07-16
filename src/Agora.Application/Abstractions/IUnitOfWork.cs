namespace Agora.Application.Abstractions;

/// <summary>
/// Commits everything a use case changed, atomically.
/// </summary>
// WHY: repositories COLLECT changes; the use case decides WHEN they become real. Keeping commit
// explicit (instead of hiding SaveChanges inside every repository Add) is what makes "several
// writes, one transaction" the default rather than an advanced trick — register writes a User AND
// a Session, and either both land or neither does. This is Agora's FIRST write path (S01 was
// read-only), so this seam is born here. It becomes load-bearing in Sprint 10 (the transactional
// outbox joins the same commit) — the whole reliability story hangs off this one method.
public interface IUnitOfWork
{
    Task CommitAsync(CancellationToken cancellationToken);
}
