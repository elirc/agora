using Agora.Domain.Catalog;
using Agora.Domain.Pricing;
using Agora.Domain.Values;

namespace Agora.Application.Tests.Fakes;

/// <summary>Deterministic catalog builders for use-case tests — staggered, explicit instants.</summary>
public static class CatalogFixtures
{
    public static readonly DateTimeOffset Base =
        new(2026, 7, 16, 10, 0, 0, TimeSpan.Zero);

    public static Category CategoryAt(string name, string slug, int minute) =>
        Category.Create(name, Slug.From(slug), Base.AddMinutes(minute));

    public static Product ProductAt(
        Guid categoryId,
        string name,
        string slug,
        string sku,
        int minute,
        decimal price = 19.99m) =>
        Product.Create(
            categoryId,
            name,
            Slug.From(slug),
            Sku.From(sku),
            $"{name} — a fine thing.",
            Money.Of(price, CurrencyCode.Usd),
            Base.AddMinutes(minute));

    /// <summary>A campaign on a product, its window expressed in minutes relative to <see cref="Base"/>.</summary>
    // LEARN: every fixture instant is derived from one frozen Base — never from DateTimeOffset.Now.
    // A test whose window is "now ± an hour" passes today and is untrustworthy forever after: you
    // cannot tell from a green tick whether it asserted the boundary or merely missed it by a mile.
    public static SaleCampaign CampaignOn(
        Guid productId,
        Discount discount,
        int startsMinute,
        int endsMinute,
        string name = "Test Sale",
        int createdMinute = -60) =>
        SaleCampaign.Schedule(
            productId,
            name,
            discount,
            DateRange.From(Base.AddMinutes(startsMinute), Base.AddMinutes(endsMinute)),
            Base.AddMinutes(createdMinute));
}
