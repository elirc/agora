using Agora.Application.Abstractions;
using Agora.Domain;
using Agora.Domain.Access;

namespace Agora.Application.Auth;

/// <summary>The outcome of a successful registration: the new user and a live session.</summary>
public sealed record CustomerRegistered(User User, string Token, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Self-service sign-up: a new <see cref="Role.Customer"/> and a live session, atomically. Either
/// the account exists with a way to sign in, or nothing was written.
/// </summary>
public sealed class RegisterCustomer(
    IUserRepository users,
    ISessionRepository sessions,
    IPasswordHasher hasher,
    ISessionTokens tokens,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public const int MinPasswordLength = 10;

    public async Task<CustomerRegistered> ExecuteAsync(
        string? email,
        string? password,
        CancellationToken cancellationToken)
    {
        var address = EmailAddress.From(email);

        if (password is null || password.Length < MinPasswordLength)
        {
            // WHY: length is the only rule. Composition rules ("one symbol, one digit")
            // measurably push people toward Password1! — NIST 800-63B agrees. Length now, a
            // breach-list check later (S13 backlog). This is a 422: the request was well-formed,
            // the business said no.
            throw new DomainRuleException(
                $"Password must be at least {MinPasswordLength} characters.");
        }

        if (await users.FindByEmailAsync(address, cancellationToken) is not null)
        {
            // A friendly 422 for the common case. The users.email unique index (Infrastructure)
            // is the real guarantee — it closes the race two simultaneous signups could win.
            throw new DomainRuleException("That email address is already registered.");
        }

        DateTimeOffset now = clock.GetUtcNow();

        // WHY: hash BEFORE building the user — the domain only ever receives a verifier. There is
        // no moment in this method where a User object holds a plaintext password.
        var user = User.Create(address, hasher.Hash(password), Role.Customer, now);

        (string token, string hash) = tokens.Mint();
        var session = Session.Start(user.Id, hash, now);

        await users.AddAsync(user, cancellationToken);
        await sessions.AddAsync(session, cancellationToken);

        // LEARN: two writes, ONE commit. If the session insert failed, no orphaned user would
        // exist — this is IUnitOfWork earning its keep on Agora's very first write path.
        await unitOfWork.CommitAsync(cancellationToken);

        return new CustomerRegistered(user, token, session.ExpiresAtUtc);
    }
}
