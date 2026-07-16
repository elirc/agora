namespace Agora.Domain.Access;

/// <summary>
/// A login session. The cookie's token itself never touches the database — only its hash is
/// stored, so a leaked <c>sessions</c> table cannot be replayed to impersonate anyone.
/// </summary>
// LEARN: this is a FIXED-lifetime session, not sliding. A sliding window would extend expiry
// on every request — which means a database WRITE on every request, including public catalog
// GETs. For a shop that is a bad trade: fixed expiry + re-login is simpler, still revocable,
// and keeps reads read-only. "Remember me" / sliding is a deliberate future enhancement, and
// ADR-0004 records exactly when we'd add it.
public sealed class Session
{
    // WHY: a week. Long enough that a returning shopper is not re-typing a password every day,
    // short enough that a stolen cookie has a bounded blast radius. The number lives HERE, once.
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public Guid Id { get; }
    public Guid UserId { get; }
    public string TokenHash { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    private Session(
        Guid id,
        Guid userId,
        string tokenHash,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public static Session Start(Guid userId, string tokenHash, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new DomainRuleException("A session needs a token hash.");
        }

        return new Session(Guid.NewGuid(), userId, tokenHash, nowUtc, nowUtc + Lifetime);
    }

    // LEARN: the session knows its OWN liveness rules; callers ask, they don't re-derive.
    // `nowUtc` is passed in (never DateTimeOffset.Now) so a test can prove the boundary to the
    // second, and staging (UTC) and prod agree on when a token dies. When "active" grows a
    // third condition (user disabled, password rotated), there is exactly one method to change.
    public bool IsActive(DateTimeOffset nowUtc) =>
        RevokedAtUtc is null && nowUtc < ExpiresAtUtc;

    public void Revoke(DateTimeOffset nowUtc)
    {
        // WHY: idempotent on purpose — logging out twice is a no-op, not an error. Clients
        // retry; retries must be safe (the S10 idempotency theme, showing up early and small).
        RevokedAtUtc ??= nowUtc;
    }

    // WHY: EF Core materialization only.
    private Session()
    {
        TokenHash = null!;
    }
}
