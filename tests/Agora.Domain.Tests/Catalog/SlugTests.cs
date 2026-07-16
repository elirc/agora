using Agora.Domain;
using Agora.Domain.Catalog;

namespace Agora.Domain.Tests.Catalog;

public class SlugTests
{
    [Theory]
    [InlineData("trail-running-shoes", "trail-running-shoes")]
    [InlineData("  Trail-Running-Shoes  ", "trail-running-shoes")]
    [InlineData("KITCHEN", "kitchen")]
    [InlineData("home-office-2", "home-office-2")]
    public void From_normalizes_case_and_trims(string raw, string expected)
    {
        Assert.Equal(expected, Slug.From(raw).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Trail Shoes")]     // space is not a hyphen
    [InlineData("-leading")]         // leading hyphen
    [InlineData("trailing-")]        // trailing hyphen
    [InlineData("double--hyphen")]   // empty group
    [InlineData("café")]        // non-ASCII
    [InlineData("under_score")]      // underscore not allowed
    public void From_rejects_non_slugs(string? raw)
    {
        Assert.Throws<DomainRuleException>(() => Slug.From(raw));
    }

    [Fact]
    public void From_rejects_over_length()
    {
        string tooLong = new('a', Slug.MaxLength + 1);

        Assert.Throws<DomainRuleException>(() => Slug.From(tooLong));
    }

    [Fact]
    public void Equal_by_value()
    {
        Assert.Equal(Slug.From("kitchen-dining"), Slug.From("  Kitchen-Dining "));
    }
}
