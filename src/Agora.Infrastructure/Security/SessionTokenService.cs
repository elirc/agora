using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Agora.Application.Abstractions;

namespace Agora.Infrastructure.Security;

internal sealed class SessionTokenService : ISessionTokens
{
    // A short prefix makes a token recognizable in logs/support ("agr_…") without revealing
    // anything — the entropy is all in the 256 random bits after it.
    private const string Prefix = "agr_";

    public (string Token, string Hash) Mint()
    {
        // ⚠️ 256 bits of CSPRNG entropy — unguessable, and carrying NO claims. An opaque token
        // beats a JWT here because sessions are server-side state anyway (we need revocation on
        // logout), so a self-describing token would buy nothing and cost us the ability to kill
        // it. RandomNumberGenerator, never System.Random — the latter is predictable.
        string token = Prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        return (token, HashOf(token));
    }

    // WHY: SHA-256, not PBKDF2, for the token. Password hashing is slow ON PURPOSE because
    // passwords are low-entropy; a 256-bit random token cannot be brute-forced, so its hash only
    // needs to be one-way, not expensive — and an auth check runs on EVERY request, which cannot
    // afford 600k iterations. Right tool, right threat model.
    public string HashOf(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
