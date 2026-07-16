using Agora.Application.Abstractions;
using Agora.Application.Errors;
using Agora.Domain;
using Agora.Domain.Access;

namespace Agora.Application.Auth;

/// <summary>The outcome of a successful login: the user and a freshly minted session.</summary>
public sealed record LoggedIn(User User, string Token, DateTimeOffset ExpiresAtUtc);

public sealed class Login(
    IUserRepository users,
    ISessionRepository sessions,
    IPasswordHasher hasher,
    ISessionTokens tokens,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<LoggedIn> ExecuteAsync(
        string? email,
        string? password,
        CancellationToken cancellationToken)
    {
        // WHY: unknown email and wrong password produce the SAME exception and the same message.
        // Distinct errors are an oracle that lets an attacker enumerate which addresses have
        // accounts — a real vulnerability class (user enumeration). A malformed email is just a
        // failed login too; the shape of the failure must not reveal anything either.
        User? user = null;
        try
        {
            user = await users.FindByEmailAsync(EmailAddress.From(email), cancellationToken);
        }
        catch (DomainRuleException)
        {
            // Malformed email at login is a failed login, not a 422.
        }

        if (user is null || !hasher.Verify(password ?? string.Empty, user.PasswordHash))
        {
            throw new UnauthenticatedException("Invalid email or password.");
        }

        DateTimeOffset now = clock.GetUtcNow();
        (string token, string hash) = tokens.Mint();
        var session = Session.Start(user.Id, hash, now);

        await sessions.AddAsync(session, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return new LoggedIn(user, token, session.ExpiresAtUtc);
    }
}
