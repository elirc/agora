namespace Agora.Domain.Catalog;

/// <summary>
/// A grouping of products in the storefront (e.g. Footwear, Outdoor). Managed by the
/// Store Admin; browsed by shoppers. In Sprint 01 categories are seed-only and read-only
/// through the API — admin CRUD arrives in Sprint 03.
/// </summary>
public sealed class Category
{
    public const int MaxNameLength = 100;

    public Guid Id { get; }
    public string Name { get; private set; }
    public Slug Slug { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }

    private Category(Guid id, string name, Slug slug, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        Slug = slug;
        CreatedAtUtc = createdAtUtc;
    }

    public static Category Create(string? name, Slug slug, DateTimeOffset createdAtUtc)
    {
        // LEARN: entities are born valid. There is no Category instance anywhere in the
        // process with a bad name — so no downstream code ever re-validates. The Slug was
        // already verified by its own constructor before it got here.
        return new Category(Guid.NewGuid(), ValidName(name), slug, createdAtUtc);
    }

    private static string ValidName(string? name)
    {
        string trimmed = (name ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            throw new DomainRuleException("Category name is required.");
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new DomainRuleException($"Category name exceeds {MaxNameLength} characters.");
        }

        return trimmed;
    }

    // WHY: EF Core materialization only. Private, parameterless, and invisible to every
    // caller that matters — the factory remains the single door through which a valid
    // Category can exist.
    private Category()
    {
        Name = null!;
    }
}
