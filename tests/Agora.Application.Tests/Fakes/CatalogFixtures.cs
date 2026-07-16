using Agora.Domain.Catalog;

namespace Agora.Application.Tests.Fakes;

/// <summary>Deterministic catalog builders for use-case tests — staggered, explicit instants.</summary>
public static class CatalogFixtures
{
    public static readonly DateTimeOffset Base =
        new(2026, 7, 16, 10, 0, 0, TimeSpan.Zero);

    public static Category CategoryAt(string name, string slug, int minute) =>
        Category.Create(name, Slug.From(slug), Base.AddMinutes(minute));

    public static Product ProductAt(Guid categoryId, string name, string slug, string sku, int minute) =>
        Product.Create(
            categoryId,
            name,
            Slug.From(slug),
            Sku.From(sku),
            $"{name} — a fine thing.",
            19.99m,
            "USD",
            Base.AddMinutes(minute));
}
