using Agora.Application.Auth;
using Agora.Application.Errors;
using Agora.Application.Tests.Fakes;
using Agora.Domain.Access;

namespace Agora.Application.Tests.Auth;

public class LoginLogoutTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryUserRepository _users = new();
    private readonly InMemorySessionRepository _sessions = new();
    private readonly CountingUnitOfWork _uow = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly SequentialSessionTokens _tokens = new();

    public LoginLogoutTests()
    {
        _users.Rows.Add(User.Create(
            EmailAddress.From("ada@example.com"),
            _hasher.Hash("correct-horse-battery"),
            Role.Customer,
            Now));
    }

    private Login LoginUseCase() =>
        new(_users, _sessions, _hasher, _tokens, _uow, new FixedClock(Now));

    [Fact]
    public async Task Valid_credentials_mint_a_session()
    {
        // Email match is case-insensitive (normalized in EmailAddress).
        LoggedIn result = await LoginUseCase().ExecuteAsync(
            "ADA@example.com", "correct-horse-battery", CancellationToken.None);

        Assert.Equal("token-1", result.Token);
        Assert.Single(_sessions.Rows);
        Assert.Equal(Now + Session.Lifetime, result.ExpiresAtUtc);
        Assert.Equal(1, _uow.Commits);
    }

    [Theory]
    [InlineData("ada@example.com", "wrong-password")]
    [InlineData("nobody@example.com", "correct-horse-battery")]
    [InlineData("not-even-an-email", "whatever-here")]
    public async Task All_failures_are_indistinguishable(string email, string password)
    {
        // LEARN: wrong password, unknown user, and garbage email must produce the SAME exception
        // and the SAME message — distinct errors are a user-enumeration oracle. No session is
        // written, and nothing is committed.
        var ex = await Assert.ThrowsAsync<UnauthenticatedException>(
            () => LoginUseCase().ExecuteAsync(email, password, CancellationToken.None));

        Assert.Equal("Invalid email or password.", ex.Message);
        Assert.Empty(_sessions.Rows);
        Assert.Equal(0, _uow.Commits);
    }

    [Fact]
    public async Task Logout_revokes_the_session_and_is_idempotent()
    {
        LoggedIn login = await LoginUseCase().ExecuteAsync(
            "ada@example.com", "correct-horse-battery", CancellationToken.None);

        var logout = new Logout(_sessions, _tokens, _uow, new FixedClock(Now.AddMinutes(1)));

        await logout.ExecuteAsync(login.Token, CancellationToken.None);
        Assert.False(_sessions.Rows[0].IsActive(Now.AddMinutes(2)));

        // Second logout with the same (now dead) token still succeeds — a safe retry.
        await logout.ExecuteAsync(login.Token, CancellationToken.None);
    }

    [Fact]
    public async Task Logout_of_an_unknown_token_is_a_silent_success()
    {
        var logout = new Logout(_sessions, _tokens, _uow, new FixedClock(Now));

        await logout.ExecuteAsync("token-does-not-exist", CancellationToken.None);

        Assert.Empty(_sessions.Rows);
        Assert.Equal(1, _uow.Commits); // commit still runs; there was simply nothing to revoke
    }
}
