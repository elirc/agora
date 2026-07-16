using Agora.Application.Abstractions;
using Agora.Application.Errors;
using Agora.Domain.Access;

namespace Agora.Application.Tests.Fakes;

public sealed class InMemoryUserRepository : IUserRepository
{
    public List<User> Rows { get; } = [];

    public Task AddAsync(User user, CancellationToken ct)
    {
        Rows.Add(user);
        return Task.CompletedTask;
    }

    public Task<User?> FindAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Rows.FirstOrDefault(u => u.Id == id));

    public Task<User?> FindByEmailAsync(EmailAddress email, CancellationToken ct) =>
        Task.FromResult(Rows.FirstOrDefault(u => u.Email == email));
}

public sealed class InMemorySessionRepository : ISessionRepository
{
    public List<Session> Rows { get; } = [];

    public Task AddAsync(Session session, CancellationToken ct)
    {
        Rows.Add(session);
        return Task.CompletedTask;
    }

    public Task<Session?> FindByTokenHashAsync(string tokenHash, CancellationToken ct) =>
        Task.FromResult(Rows.FirstOrDefault(s => s.TokenHash == tokenHash));
}

/// <summary>Reversible "hash" — fine for tests, where we assert flows, not crypto.</summary>
public sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string password) => $"hashed:{password}";

    public bool Verify(string password, string storedHash) => storedHash == $"hashed:{password}";
}

/// <summary>Deterministic tokens (token-1, token-2, …) so a test can name the exact value minted.</summary>
public sealed class SequentialSessionTokens : ISessionTokens
{
    private int _minted;

    public (string Token, string Hash) Mint()
    {
        _minted++;
        return ($"token-{_minted}", $"tokenhash-{_minted}");
    }

    public string HashOf(string token) => token.Replace("token-", "tokenhash-");
}

/// <summary>A stub ICurrentUser for use-case tests: anonymous, or a chosen user + role.</summary>
public sealed class StubCurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; init; }
    public Guid UserId { get; init; }
    public Role? Role { get; init; }

    public static StubCurrentUser Anonymous() => new();

    public static StubCurrentUser SignedIn(Role role, Guid? userId = null) => new()
    {
        IsAuthenticated = true,
        UserId = userId ?? Guid.NewGuid(),
        Role = role,
    };

    public void RequireUser()
    {
        if (!IsAuthenticated)
        {
            throw new UnauthenticatedException();
        }
    }

    public void RequireRole(params Role[] anyOf)
    {
        RequireUser();
        if (Role is not { } role || !anyOf.Contains(role))
        {
            throw new ForbiddenException();
        }
    }
}
