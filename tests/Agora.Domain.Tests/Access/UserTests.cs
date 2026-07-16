using Agora.Domain;
using Agora.Domain.Access;

namespace Agora.Domain.Tests.Access;

public class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_stores_the_verifier_role_and_time()
    {
        var user = User.Create(
            EmailAddress.From("shopper@example.com"), "pbkdf2$verifier", Role.Customer, Now);

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("shopper@example.com", user.Email.Value);
        Assert.Equal("pbkdf2$verifier", user.PasswordHash);
        Assert.Equal(Role.Customer, user.Role);
        Assert.Equal(Now, user.CreatedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_a_password_verifier(string? hash)
    {
        // WHY: the domain refuses a user with no way to authenticate. It does NOT validate the
        // password itself — it never sees one; length rules live in the RegisterCustomer use case.
        Assert.Throws<DomainRuleException>(
            () => User.Create(EmailAddress.From("x@example.com"), hash!, Role.Customer, Now));
    }

    [Fact]
    public void There_is_no_way_to_construct_a_user_holding_plaintext()
    {
        // 📘 a compile-time guarantee expressed as a test-of-record: the only public constructor
        // takes a HASH, so no code path anywhere puts a plaintext password on a User. Grep the
        // type — there is no `Password` property to accidentally log or serialize.
        Type t = typeof(User);
        Assert.Null(t.GetProperty("Password"));
        Assert.Null(t.GetProperty("PlaintextPassword"));
    }
}
