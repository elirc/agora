using Agora.Domain;
using Agora.Domain.Access;

namespace Agora.Domain.Tests.Access;

public class EmailAddressTests
{
    [Theory]
    [InlineData("ada@example.com")]
    [InlineData("a@b.co")]
    [InlineData("first.last+tag@sub.domain.io")]
    public void Accepts_well_shaped_addresses(string raw)
    {
        Assert.Equal(raw.ToLowerInvariant(), EmailAddress.From(raw).Value);
    }

    [Fact]
    public void Normalizes_case_and_surrounding_whitespace()
    {
        // LEARN: two shoppers typing the same address in different case are ONE account. The
        // normalization is what makes the users.email unique index actually case-insensitive.
        Assert.Equal("ada@example.com", EmailAddress.From("  Ada@Example.COM ").Value);
        Assert.Equal(EmailAddress.From("ADA@EXAMPLE.COM"), EmailAddress.From("ada@example.com"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("@example.com")]
    [InlineData("ada@")]
    [InlineData("ada@@example.com")]
    [InlineData("ada @example.com")]
    public void Rejects_malformed_addresses(string? raw)
    {
        Assert.Throws<DomainRuleException>(() => EmailAddress.From(raw));
    }
}
