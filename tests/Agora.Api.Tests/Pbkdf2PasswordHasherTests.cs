using Agora.Application.Abstractions;
using Agora.Infrastructure.Security;

namespace Agora.Api.Tests;

/// <summary>
/// The REAL hasher's unit tests. The HTTP suite swaps in a fast fake (600k iterations is too slow
/// to log in dozens of times), so this file is where the production crypto is actually exercised.
/// It lives in Api.Tests because Infrastructure exposes its adapters via InternalsVisibleTo here.
/// </summary>
public class Pbkdf2PasswordHasherTests
{
    private readonly IPasswordHasher _hasher = new Pbkdf2PasswordHasher();

    [Fact]
    public void Hash_then_verify_roundtrips()
    {
        string hash = _hasher.Hash("correct-horse-battery");

        Assert.True(_hasher.Verify("correct-horse-battery", hash));
        Assert.False(_hasher.Verify("wrong-horse-battery", hash));
    }

    [Fact]
    public void Same_password_hashes_differently_every_time()
    {
        // LEARN: per-password random salt. Identical hashes for identical passwords would let an
        // attacker crack one and unlock everyone who chose it — and reveal WHO shares a password.
        Assert.NotEqual(_hasher.Hash("same-password-here"), _hasher.Hash("same-password-here"));
    }

    [Fact]
    public void Stored_format_self_describes_its_parameters()
    {
        string hash = _hasher.Hash("some-password-here");

        // pbkdf2-sha256$<iterations>$<salt>$<key>
        string[] parts = hash.Split('$');
        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2-sha256", parts[0]);
        Assert.True(int.Parse(parts[1]) >= 600_000);
    }

    [Theory]
    [InlineData("not-a-hash")]
    [InlineData("")]
    [InlineData("pbkdf2-sha256$abc$??$??")]
    public void Garbage_stored_hashes_verify_false_never_throw(string storedHash)
    {
        Assert.False(_hasher.Verify("password", storedHash));
    }
}
