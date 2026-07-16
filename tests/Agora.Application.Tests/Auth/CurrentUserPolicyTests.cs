using Agora.Application.Auth;
using Agora.Application.Errors;
using Agora.Application.Tests.Fakes;
using Agora.Domain.Access;

namespace Agora.Application.Tests.Auth;

/// <summary>
/// The authorization PRIMITIVES — RequireUser / RequireRole — exercised directly through a stub
/// ICurrentUser, plus GetCurrentUser's gate. These are the policy calls every future protected
/// use case makes; proving them here means an endpoint never re-implements them inline.
/// </summary>
public class CurrentUserPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RequireUser_throws_for_anonymous_and_passes_for_signed_in()
    {
        Assert.Throws<UnauthenticatedException>(() => StubCurrentUser.Anonymous().RequireUser());

        StubCurrentUser.SignedIn(Role.Customer).RequireUser(); // does not throw
    }

    [Fact]
    public void RequireRole_is_401_for_anonymous_before_it_is_ever_403()
    {
        // WHY: "who are you" is answered before "may you" — an anonymous caller is 401, not 403,
        // even for a role-gated action. You cannot be forbidden from something you have not yet
        // authenticated for.
        Assert.Throws<UnauthenticatedException>(
            () => StubCurrentUser.Anonymous().RequireRole(Role.StoreAdmin));
    }

    [Fact]
    public void RequireRole_forbids_a_signed_in_caller_lacking_the_role()
    {
        Assert.Throws<ForbiddenException>(
            () => StubCurrentUser.SignedIn(Role.Customer).RequireRole(Role.StoreAdmin, Role.Ops));
    }

    [Fact]
    public void RequireRole_admits_any_listed_role_but_grants_ops_no_implicit_god_mode()
    {
        // Ops passes only when Ops is listed — capabilities, not a hierarchy.
        StubCurrentUser.SignedIn(Role.Ops).RequireRole(Role.StoreAdmin, Role.Ops); // ok
        StubCurrentUser.SignedIn(Role.StoreAdmin).RequireRole(Role.StoreAdmin);     // ok

        // Ops is NOT implicitly a StoreAdmin: a StoreAdmin-only action forbids Ops.
        Assert.Throws<ForbiddenException>(
            () => StubCurrentUser.SignedIn(Role.Ops).RequireRole(Role.StoreAdmin));
    }

    [Fact]
    public async Task GetCurrentUser_returns_the_signed_in_user()
    {
        var users = new InMemoryUserRepository();
        var user = User.Create(EmailAddress.From("ada@example.com"), "hashed:x", Role.Customer, Now);
        users.Rows.Add(user);

        var useCase = new GetCurrentUser(StubCurrentUser.SignedIn(Role.Customer, user.Id), users);

        User me = await useCase.ExecuteAsync(CancellationToken.None);
        Assert.Equal(user.Id, me.Id);
    }

    [Fact]
    public async Task GetCurrentUser_rejects_anonymous_callers()
    {
        var useCase = new GetCurrentUser(StubCurrentUser.Anonymous(), new InMemoryUserRepository());

        await Assert.ThrowsAsync<UnauthenticatedException>(
            () => useCase.ExecuteAsync(CancellationToken.None));
    }
}
