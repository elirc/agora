using Agora.Application.Auth;
using Agora.Application.Tests.Fakes;
using Agora.Domain;
using Agora.Domain.Access;

namespace Agora.Application.Tests.Auth;

public class RegisterCustomerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryUserRepository _users = new();
    private readonly InMemorySessionRepository _sessions = new();
    private readonly CountingUnitOfWork _uow = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly SequentialSessionTokens _tokens = new();

    private RegisterCustomer UseCase() =>
        new(_users, _sessions, _hasher, _tokens, _uow, new FixedClock(Now));

    [Fact]
    public async Task Registers_a_customer_with_a_live_session_in_one_commit()
    {
        CustomerRegistered result = await UseCase().ExecuteAsync(
            "New.Shopper@Example.com", "a-long-enough-password", CancellationToken.None);

        // The account is a Customer (never a privileged role — there is no public path to one).
        User stored = Assert.Single(_users.Rows);
        Assert.Equal(Role.Customer, stored.Role);
        Assert.Equal("new.shopper@example.com", stored.Email.Value);

        // The verifier is a hash, never the plaintext.
        Assert.Equal("hashed:a-long-enough-password", stored.PasswordHash);

        // A session was minted and its expiry is exactly one week out.
        Session session = Assert.Single(_sessions.Rows);
        Assert.Equal("token-1", result.Token);
        Assert.Equal(Now + Session.Lifetime, result.ExpiresAtUtc);
        Assert.Equal(session.UserId, stored.Id);

        // Two writes, ONE transaction.
        Assert.Equal(1, _uow.Commits);
    }

    [Fact]
    public async Task Duplicate_email_is_a_domain_rule_violation_and_writes_nothing()
    {
        await UseCase().ExecuteAsync("dup@example.com", "a-long-enough-password", CancellationToken.None);

        // Same address, different case — normalization makes it the same account.
        var ex = await Assert.ThrowsAsync<DomainRuleException>(
            () => UseCase().ExecuteAsync("DUP@example.com", "another-good-password", CancellationToken.None));

        Assert.Contains("already registered", ex.Message);
        Assert.Single(_users.Rows);
        Assert.Single(_sessions.Rows);
        Assert.Equal(1, _uow.Commits); // the failed attempt never committed
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short")]
    public async Task Short_or_missing_password_is_rejected_before_any_write(string? password)
    {
        await Assert.ThrowsAsync<DomainRuleException>(
            () => UseCase().ExecuteAsync("shopper@example.com", password, CancellationToken.None));

        Assert.Empty(_users.Rows);
        Assert.Equal(0, _uow.Commits);
    }

    [Fact]
    public async Task Malformed_email_is_a_domain_rule_violation()
    {
        await Assert.ThrowsAsync<DomainRuleException>(
            () => UseCase().ExecuteAsync("not-an-email", "a-long-enough-password", CancellationToken.None));
    }
}
