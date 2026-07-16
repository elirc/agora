namespace Agora.Domain.Catalog;

/// <summary>
/// A grouping of products in the storefront (e.g. Footwear, Outdoor). Managed by the
/// Store Admin; browsed by shoppers. In Sprint 01 categories are seed-only and read-only
/// through the API — admin create/rename with optimistic concurrency arrives in Sprint 03.
/// </summary>
public sealed class Category
{
    public const int MaxNameLength = 100;

    public Guid Id { get; }
    public string Name { get; private set; }
    public Slug Slug { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    // The same optimistic-concurrency token Product carries — see the note there. A category rename
    // is far rarer than a product edit, but "rare" is not "never", and the cost is one column.
    public int Version { get; private set; }

    private Category(Guid id, string name, Slug slug, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        Slug = slug;
        CreatedAtUtc = createdAtUtc;
        Version = 1;
    }

    public static Category Create(string? name, Slug slug, DateTimeOffset createdAtUtc)
    {
        // LEARN: entities are born valid. There is no Category instance anywhere in the
        // process with a bad name — so no downstream code ever re-validates. The Slug was
        // already verified by its own constructor before it got here.
        return new Category(Guid.NewGuid(), ValidName(name), slug, createdAtUtc);
    }

    /// <summary>Rename the category. Its <see cref="Slug"/> is a permalink and does not change.</summary>
    // WHY only the name: same rule as Product — the slug is the storefront's routing identity, so a
    // rename does not re-slug (that would break /categories/{slug} links). Version bumps so a stale
    // editor loses the race cleanly. (ADR-0005.)
    public void Rename(string? name)
    {
        Name = ValidName(name);
        Version++;
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
