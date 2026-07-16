using System.Security.Cryptography;
using Agora.Application.Abstractions;

namespace Agora.Infrastructure.Security;

/// <summary>
/// PBKDF2-SHA256 password hashing. Hand-rolled deliberately (ADR-0004): the point of this sprint
/// is that a mid-level engineer can explain every byte of this file — and that understanding is
/// what makes the S13 audit meaningful.
/// </summary>
internal sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    // WHY: ~600k iterations is the 2026 OWASP guidance neighborhood for PBKDF2-SHA256. The count
    // is EMBEDDED in each hash, so raising it later upgrades new hashes without invalidating old
    // ones — a redeploy, not a migration (see Verify).
    private const int Iterations = 600_000;
    private const int SaltSize = 16; // 128-bit per-user salt
    private const int KeySize = 32;  // 256-bit derived key

    public string Hash(string password)
    {
        // GOTCHA: a NEW random salt every time. Two users with the same password get different
        // hashes — so cracking one never unlocks the other, and equal hashes can't reveal who
        // shares a password.
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);

        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public bool Verify(string password, string storedHash)
    {
        string[] parts = storedHash.Split('$');

        // LEARN: the stored format self-describes its parameters. Verification uses the STORED
        // iteration count, not the current constant — THAT is what makes a work-factor bump a
        // deploy instead of a mass password reset. Anything malformed verifies false, never throws.
        if (parts is not [_, var iterText, var saltText, var keyText]
            || !int.TryParse(iterText, out int iterations))
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(saltText);
            expected = Convert.FromBase64String(keyText);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        // ⚠️ fixed-time comparison. `SequenceEqual` short-circuits on the first differing byte,
        // and that timing difference is measurable across a network — a real side channel.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
