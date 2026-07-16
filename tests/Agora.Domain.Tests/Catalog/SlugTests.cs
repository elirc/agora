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

    [Theory]
    [InlineData("Kitchen & Dining", "kitchen-dining")]
    [InlineData("  Trail   Running  Shoes ", "trail-running-shoes")]
    [InlineData("Standing Desk Converter", "standing-desk-converter")]
    [InlineData("AGA-KIT-014", "aga-kit-014")]
    [InlineData("Waffle-Weave Bath Towel!", "waffle-weave-bath-towel")]
    [InlineData("café noir", "caf-noir")] // non-ASCII letters drop out; the rest still slugs
    public void Generate_slugifies_human_text(string text, string expected)
    {
        Assert.Equal(expected, Slug.Generate(text).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]        // no slug-able characters
    [InlineData("—— ——")]     // punctuation and spaces only
    public void Generate_rejects_text_with_no_slug_able_characters(string? text)
    {
        Assert.Throws<DomainRuleException>(() => Slug.Generate(text));
    }

    [Fact]
    public void Generate_still_enforces_the_length_limit()
    {
        string tooLong = new('a', Slug.MaxLength + 1);

        // Generation is a convenience, not an escape hatch — it re-enters through From, so an
        // over-long result is rejected, not silently truncated.
        Assert.Throws<DomainRuleException>(() => Slug.Generate(tooLong));
    }
}
