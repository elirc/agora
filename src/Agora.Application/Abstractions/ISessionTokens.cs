namespace Agora.Application.Abstractions;

/// <summary>Mints opaque session tokens and derives their at-rest hashes.</summary>
// WHY: the raw token exists exactly twice — in the Set-Cookie header and in the browser's cookie
// jar. The database sees only Hash(token), so an exfiltrated `sessions` table cannot be replayed.
// Mint returns BOTH halves so the use case never learns how either is produced (opaque token vs.
// JWT, SHA-256 vs. something else — all Infrastructure's business).
public interface ISessionTokens
{
    /// <summary>A fresh (token, hash) pair: the token goes to the client, the hash to the store.</summary>
    (string Token, string Hash) Mint();

    /// <summary>The at-rest hash of a presented token, for looking the session up.</summary>
    string HashOf(string token);
}
