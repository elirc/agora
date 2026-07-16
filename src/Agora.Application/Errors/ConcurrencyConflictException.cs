namespace Agora.Application.Errors;

/// <summary>
/// An optimistic-concurrency conflict: the caller edited version N, but the stored row has already
/// moved on (another writer got there first). The Api maps this to <c>409 Conflict</c> problem+json
/// and, when a current snapshot is available, echoes it so the client can rebase its edit onto the
/// latest state instead of blindly retrying.
/// </summary>
// LEARN: this is a distinct failure FAMILY, so it gets its own type and its own status code — the
// same discipline that keeps NotFound (404) apart from RequestValidation (400) apart from
// DomainRule (422). A 409 says "your write is well-formed and you're allowed, but the world changed
// under you"; collapsing it into a 400 or 422 would lose exactly the signal a client scripts a
// retry-after-reload against.
//
// It carries the current DOMAIN entity (as <see cref="object"/> so Application stays free of any
// contract/DTO types). Two sources raise it: the use case's explicit version check, which HAS the
// loaded entity and passes it here; and the infrastructure safety net (EfUnitOfWork) for the rarer
// race it catches at commit time, which passes null. A null <see cref="Current"/> simply means the
// 409 body has no snapshot and the client must re-fetch.
public sealed class ConcurrencyConflictException(string resource, object? current = null)
    : Exception($"This {resource} was changed by someone else. Reload the latest version and try again.")
{
    public string Resource { get; } = resource;

    /// <summary>The current server-side entity, or <c>null</c> when only a re-fetch can recover it.</summary>
    public object? Current { get; } = current;
}
