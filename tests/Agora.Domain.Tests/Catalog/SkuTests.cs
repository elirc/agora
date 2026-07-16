using Agora.Domain;
using Agora.Domain.Catalog;

namespace Agora.Domain.Tests.Catalog;

public class SkuTests
{
    [Theory]
    [InlineData("AGA-KIT-001", "AGA-KIT-001")]
    [InlineData("  aga-kit-001  ", "AGA-KIT-001")]
    [InlineData("SKU12345", "SKU12345")]
    public void From_uppercases_and_trims(string raw, string expected)
    {
        Assert.Equal(expected, Sku.From(raw).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    [InlineData("under_score")]
    [InlineData("slash/here")]
    public void From_rejects_invalid_skus(string? raw)
    {
        Assert.Throws<DomainRuleException>(() => Sku.From(raw));
    }

    [Fact]
    public void From_rejects_over_length()
    {
        string tooLong = new('A', Sku.MaxLength + 1);

        Assert.Throws<DomainRuleException>(() => Sku.From(tooLong));
    }
}
