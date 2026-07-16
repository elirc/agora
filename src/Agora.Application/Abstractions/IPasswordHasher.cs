namespace Agora.Application.Abstractions;

/// <summary>Password hashing port. The domain stores the verifier; this makes and checks it.</summary>
// 📘 WHY a PORT and not a static helper: hashing is a policy that will change (the work factor
// climbs every few years, and one day we may swap PBKDF2 for Argon2). A port means that swap is a
// new adapter and a DI line — not a find-and-replace across call sites — and it means tests inject
// a fast fake instead of paying 600k iterations per login. A `PasswordHasher.Hash(...)` static
// would hard-wire today's algorithm into the application layer forever. See docs/adr/0004.
public interface IPasswordHasher
{
    /// <summary>Produce a self-describing verifier (algorithm + params + salt + key) to store.</summary>
    string Hash(string password);

    /// <summary>Constant-time check of a candidate password against a stored verifier.</summary>
    bool Verify(string password, string storedHash);
}
