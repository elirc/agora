namespace Agora.Domain.Access;

/// <summary>
/// A person who can sign in: a shopper, a store admin, or an ops user. The account a cart
/// and (from S07) orders will belong to.
/// </summary>
public sealed class User
{
    public Guid Id { get; }
    public EmailAddress Email { get; }

    // WHY: the Domain stores an opaque VERIFIER string and never learns what a "PBKDF2" is —
    // hashing strategy is an Infrastructure decision behind the IPasswordHasher port. There is
    // no plaintext-password property here, and there never will be: a User cannot exist holding
    // a password, only a hash of one. If the hash format changes, no Domain code notices.
    public string PasswordHash { get; private set; }

    public Role Role { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    private User(Guid id, EmailAddress email, string passwordHash, Role role, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Email = email;
        PasswordHash = passwordHash;
        Role = role;
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>
    /// Create a user with an already-computed password verifier. Registration mints a Customer;
    /// StoreAdmin and Ops accounts are seeded, not self-served (there is no public path to a
    /// privileged role — that is a deliberate authorization boundary, not an oversight).
    /// </summary>
    public static User Create(
        EmailAddress email,
        string passwordHash,
        Role role,
        DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            // WHY: the verifier is required, but the domain refuses to know how it was made.
            // "A user with no way to authenticate" is the invariant we protect here.
            throw new DomainRuleException("A password verifier is required.");
        }

        return new User(Guid.NewGuid(), email, passwordHash, role, createdAtUtc);
    }

    // WHY: EF Core materialization only.
    private User()
    {
        PasswordHash = null!;
    }
}
