using Agora.Domain;
using Agora.Domain.Catalog;

namespace Agora.Domain.Tests.Catalog;

public class ProductTests
{
    private static readonly DateTimeOffset SomeInstant =
        new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid SomeCategory = Guid.NewGuid();

    private static Product Make(
        decimal price = 34.50m,
        string? currency = "USD",
        string? name = "Cast Iron Skillet",
        string? description = "Pre-seasoned cast iron.") =>
        Product.Create(
            SomeCategory,
            name,
            Slug.From("cast-iron-skillet"),
            Sku.From("AGA-KIT-002"),
            description,
            price,
            currency,
            SomeInstant);

    [Fact]
    public void Create_assigns_all_fields_and_trims_text()
    {
        Product product = Product.Create(
            SomeCategory,
            "  Cast Iron Skillet  ",
            Slug.From("cast-iron-skillet"),
            Sku.From("aga-kit-002"),
            "  Pre-seasoned.  ",
            34.50m,
            "USD",
            SomeInstant);

        Assert.Equal(SomeCategory, product.CategoryId);
        Assert.Equal("Cast Iron Skillet", product.Name);
        Assert.Equal("cast-iron-skillet", product.Slug.Value);
        Assert.Equal("AGA-KIT-002", product.Sku.Value);
        Assert.Equal("Pre-seasoned.", product.Description);
        Assert.Equal(34.50m, product.PriceAmount);
        Assert.Equal("USD", product.PriceCurrency);
        Assert.Equal(SomeInstant, product.CreatedAtUtc);
    }

    [Fact]
    public void Create_rejects_empty_category()
    {
        Assert.Throws<DomainRuleException>(() => Product.Create(
            Guid.Empty,
            "Cast Iron Skillet",
            Slug.From("cast-iron-skillet"),
            Sku.From("AGA-KIT-002"),
            "desc",
            34.50m,
            "USD",
            SomeInstant));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_missing_name(string? name)
    {
        Assert.Throws<DomainRuleException>(() => Make(name: name));
    }

    [Fact]
    public void Create_rejects_negative_price()
    {
        Assert.Throws<DomainRuleException>(() => Make(price: -0.01m));
    }

    [Fact]
    public void Create_allows_free_products()
    {
        Product product = Make(price: 0m);

        Assert.Equal(0m, product.PriceAmount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("US")]
    [InlineData("usd")]
    [InlineData("DOLLAR")]
    public void Create_rejects_non_iso_currency(string? currency)
    {
        Assert.Throws<DomainRuleException>(() => Make(currency: currency));
    }
}
