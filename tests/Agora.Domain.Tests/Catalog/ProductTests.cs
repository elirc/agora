using Agora.Domain;
using Agora.Domain.Catalog;
using Agora.Domain.Values;

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
            Money.Of(price, currency),
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
            Money.Of(34.50m, CurrencyCode.Usd),
            SomeInstant);

        Assert.Equal(SomeCategory, product.CategoryId);
        Assert.Equal("Cast Iron Skillet", product.Name);
        Assert.Equal("cast-iron-skillet", product.Slug.Value);
        Assert.Equal("AGA-KIT-002", product.Sku.Value);
        Assert.Equal("Pre-seasoned.", product.Description);
        Assert.Equal(Money.Of(34.50m, CurrencyCode.Usd), product.Price);
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
            Money.Of(34.50m, CurrencyCode.Usd),
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

        Assert.True(product.Price.IsZero);
    }

    // 📘 S04: the currency invariant MOVED. It used to live in Product.ValidCurrency; it now lives in
    // CurrencyCode.From, so a nonsense currency can no longer reach a Product — there is no Money to
    // build one out of. Same rule, one place, and every future entity that holds money inherits it
    // rather than copying a three-line check.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("DOLLAR")]
    [InlineData("US$")]
    public void A_product_cannot_be_priced_in_a_non_iso_currency(string? currency)
    {
        Assert.Throws<DomainRuleException>(() => Make(currency: currency));
    }

    // 🔍 AND ONE RULE CHANGED ON THE WAY, so here it is in a test rather than in a surprise. S03's
    // check rejected "usd" outright; CurrencyCode NORMALIZES case at the door and accepts it, exactly
    // as Slug and Sku already did with their inputs. That is the more consistent behaviour — "usd"
    // and "USD" were never two currencies, and rejecting one of them was a shape complaint dressed up
    // as a business rule. Nothing else about the boundary got looser: "DOLLAR" and "US$" are still
    // refused above. (ADR-0006.)
    [Fact]
    public void A_currency_typed_in_lower_case_is_normalised_not_rejected()
    {
        Assert.Equal(CurrencyCode.Usd, Make(currency: "usd").Price.Currency);
    }

    // ── Version + mutation (S03) ─────────────────────────────────────────────────────────────

    [Fact]
    public void New_product_is_born_at_version_1_and_active()
    {
        Product product = Make();

        Assert.Equal(1, product.Version);
        Assert.False(product.IsArchived);
        Assert.Null(product.ArchivedAtUtc);
    }

    [Fact]
    public void UpdateDetails_changes_the_mutable_fields_and_bumps_the_version()
    {
        Product product = Make();
        Guid newCategory = Guid.NewGuid();

        product.UpdateDetails(newCategory, "  Renamed Skillet  ", "  New copy.  ", Money.Of(40.00m, CurrencyCode.Usd));

        Assert.Equal(newCategory, product.CategoryId);
        Assert.Equal("Renamed Skillet", product.Name);
        Assert.Equal("New copy.", product.Description);
        Assert.Equal(Money.Of(40.00m, CurrencyCode.Usd), product.Price);
        Assert.Equal(2, product.Version);
    }

    [Fact]
    public void UpdateDetails_does_not_change_the_slug_or_sku()
    {
        Product product = Make();
        Slug originalSlug = product.Slug;
        Sku originalSku = product.Sku;

        product.UpdateDetails(product.CategoryId, "A Totally Different Name", "desc", Money.Of(1m, CurrencyCode.Usd));

        // The slug is a permalink and the SKU a warehouse identity — a rename touches neither.
        Assert.Equal(originalSlug, product.Slug);
        Assert.Equal(originalSku, product.Sku);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateDetails_rejects_a_blank_name(string name)
    {
        Product product = Make();

        Assert.Throws<DomainRuleException>(() =>
            product.UpdateDetails(product.CategoryId, name, "desc", Money.Of(1m, CurrencyCode.Usd)));
    }

    [Fact]
    public void UpdateDetails_rejects_a_negative_price()
    {
        Product product = Make();

        // Money itself permits a negative amount — a refund is negative money. "A shelf price is not
        // negative" is the PRODUCT's rule, so it is the product that says no.
        Assert.Throws<DomainRuleException>(() =>
            product.UpdateDetails(product.CategoryId, "Name", "desc", Money.Of(-1m, CurrencyCode.Usd)));
    }

    [Fact]
    public void UpdateDetails_rejects_an_empty_category()
    {
        Product product = Make();

        Assert.Throws<DomainRuleException>(() =>
            product.UpdateDetails(Guid.Empty, "Name", "desc", Money.Of(1m, CurrencyCode.Usd)));
    }

    [Fact]
    public void Archive_records_the_timestamp_and_bumps_the_version()
    {
        Product product = Make();

        product.Archive(SomeInstant);

        Assert.True(product.IsArchived);
        Assert.Equal(SomeInstant, product.ArchivedAtUtc);
        Assert.Equal(2, product.Version);
    }

    [Fact]
    public void Archiving_an_already_archived_product_throws()
    {
        Product product = Make();
        product.Archive(SomeInstant);

        Assert.Throws<DomainRuleException>(() => product.Archive(SomeInstant));
    }

    [Fact]
    public void Restore_clears_the_archive_and_bumps_the_version()
    {
        Product product = Make();
        product.Archive(SomeInstant);

        product.Restore();

        Assert.False(product.IsArchived);
        Assert.Null(product.ArchivedAtUtc);
        Assert.Equal(3, product.Version); // create(1) → archive(2) → restore(3)
    }

    [Fact]
    public void Restoring_a_product_that_is_not_archived_throws()
    {
        Product product = Make();

        Assert.Throws<DomainRuleException>(() => product.Restore());
    }
}
