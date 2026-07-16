using Agora.Domain.Values;

namespace Agora.Domain.Catalog;

/// <summary>
/// Something a shopper can buy. Belongs to exactly one <see cref="Category"/>. Seed-only
/// and read-only through the API in Sprint 01; admin CRUD, soft-archive, and optimistic
/// concurrency arrive in Sprint 03, real pricing in Sprint 04.
/// </summary>
public sealed class Product
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 4000;

    public Guid Id { get; }
    public Guid CategoryId { get; private set; }
    public string Name { get; private set; }
    public Slug Slug { get; }
    public Sku Sku { get; }
    public string Description { get; private set; }

    // 📘 THE PAYOFF, COLLECTED. S01 left this as `decimal PriceAmount` + `string PriceCurrency` with
    // a note promising S04 would promote the pair to a Money type, and an invitation to grep for the
    // two properties then and count the call sites that move. Here is the count: the DOMAIN gained a
    // type, the DATABASE gained nothing (Money maps to the same two columns — PriceAmount, decimal
    // (18,2), and PriceCurrency, char(3) — so AddPricing does not touch this table), and the WIRE
    // gained nothing (ProductDto still sends a JSON number and a string; see its S01 comment, which
    // predicted exactly this). Only the layer that DOES ARITHMETIC learned a new word.
    //
    // That is what "contained" bought. The pair was never leaked as a loose decimal that anything
    // could add, round, or multiply on its own — so promoting it was a change of vocabulary, not a
    // migration. Primitive obsession is not expensive because primitives are bad; it is expensive
    // because by the time you want the type, forty call sites are already doing money math badly and
    // every one of them is a decision. Keep the pair together and the type is cheap whenever you
    // want it. (ADR-0006.)
    public Money Price { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    // 📘 THE LOST-UPDATE GUARD. `Version` is a code-managed optimistic-concurrency token: it is 1
    // when the product is born and increments on EVERY mutation below. The client that loaded v3
    // sends v3 back with its edit; if the row is already at v4 (another admin saved first), the
    // write is REJECTED (409) instead of silently clobbering the newer data. Last-write-wins would
    // accept it and the other admin's change would vanish with no error, no log, no trace — the
    // worst kind of data loss because nobody knows it happened. One int is the whole defense.
    // WHY an explicit int and not SQL Server rowversion / Postgres xmin: those are provider-native
    // and do not exist on the SQLite the test suite runs on — the concurrency story would be
    // untested exactly where it matters. An int we increment ourselves behaves IDENTICALLY on every
    // provider and is mapped as EF's concurrency token (IsConcurrencyToken) so the database still
    // enforces it in the UPDATE's WHERE clause. Same mechanics everywhere. (ADR-0005.)
    public int Version { get; private set; }

    // ⚠️ soft-archive: a product leaves the STOREFRONT without leaving the DATABASE. Archived is a
    // STATE (this timestamp), never a DELETE — the row keeps its history, its slug, and its SKU, so
    // past orders still resolve it and its identifiers stay reserved. Every catalog query must now
    // DECLARE whether it wants archived rows (see IProductRepository / ProductStatusFilter): the
    // storefront asks for active only, the admin list asks for all. There is no ambient default.
    public DateTimeOffset? ArchivedAtUtc { get; private set; }

    // Computed, not stored — deliberately UNMAPPED (see ModelMappingTests' allowlist). The single
    // source of truth is ArchivedAtUtc; a stored bool alongside it could disagree with the timestamp.
    public bool IsArchived => ArchivedAtUtc is not null;

    private Product(
        Guid id,
        Guid categoryId,
        string name,
        Slug slug,
        Sku sku,
        string description,
        Money price,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        CategoryId = categoryId;
        Name = name;
        Slug = slug;
        Sku = sku;
        Description = description;
        Price = price;
        CreatedAtUtc = createdAtUtc;
        Version = 1;
    }

    public static Product Create(
        Guid categoryId,
        string? name,
        Slug slug,
        Sku sku,
        string? description,
        Money price,
        DateTimeOffset createdAtUtc)
    {
        if (categoryId == Guid.Empty)
        {
            throw new DomainRuleException("A product must belong to a category.");
        }

        return new Product(
            Guid.NewGuid(),
            categoryId,
            ValidName(name),
            slug,
            sku,
            ValidDescription(description),
            ValidPrice(price),
            createdAtUtc);
    }

    /// <summary>
    /// Edit the mutable facts of a product: which category it sits in, its name, description, and
    /// price. The <see cref="Slug"/> and <see cref="Sku"/> are deliberately NOT parameters.
    /// </summary>
    // WHY the slug and SKU never move: both are IDENTIFIERS, not attributes. The slug is a permalink
    // — external links, bookmarks, and search results point at /products/{slug}, and a rename that
    // re-slugged would 404 every one of them. The SKU is what a warehouse picks by; reprinting labels
    // because a title changed is not a thing. Names describe; identifiers endure. Renaming to a fresh
    // slug is a create-plus-redirect operation, not an edit — out of scope here, and recorded in
    // ADR-0005. Every edit bumps Version, so a stale editor's save is caught.
    public void UpdateDetails(
        Guid categoryId,
        string? name,
        string? description,
        Money price)
    {
        if (categoryId == Guid.Empty)
        {
            throw new DomainRuleException("A product must belong to a category.");
        }

        CategoryId = categoryId;
        Name = ValidName(name);
        Description = ValidDescription(description);
        Price = ValidPrice(price);
        Version++;
    }

    /// <summary>Remove the product from the storefront while keeping the row and its history.</summary>
    // LEARN: archiving is a state transition with a guard, not an idempotent toggle. Archiving an
    // already-archived product is a 422 (the admin's view was stale) rather than a silent no-op —
    // and because it would otherwise bump Version for no real change, the guard keeps the token
    // honest. The concurrency check upstream will usually catch the stale view first; this is the
    // domain saying no even if it doesn't.
    public void Archive(DateTimeOffset nowUtc)
    {
        if (IsArchived)
        {
            throw new DomainRuleException("Product is already archived.");
        }

        ArchivedAtUtc = nowUtc;
        Version++;
    }

    /// <summary>Return an archived product to the storefront.</summary>
    public void Restore()
    {
        if (!IsArchived)
        {
            throw new DomainRuleException("Product is not archived.");
        }

        ArchivedAtUtc = null;
        Version++;
    }

    private static string ValidName(string? name)
    {
        string trimmed = (name ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            throw new DomainRuleException("Product name is required.");
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new DomainRuleException($"Product name exceeds {MaxNameLength} characters.");
        }

        return trimmed;
    }

    private static string ValidDescription(string? description)
    {
        string trimmed = (description ?? string.Empty).Trim();

        if (trimmed.Length > MaxDescriptionLength)
        {
            throw new DomainRuleException(
                $"Product description exceeds {MaxDescriptionLength} characters.");
        }

        return trimmed;
    }

    // LEARN: look at what this validator no longer does. It used to check the currency's shape too
    // ("is it three uppercase letters?") — that rule now lives in CurrencyCode.From, so by the time a
    // Money exists its currency is already real and there is nothing left here to doubt. What remains
    // is the one rule Money cannot know: money in general MAY be negative (a refund is), but a SHELF
    // PRICE may not. General invariants belong to the type; local ones belong to the thing they are
    // local to. Getting that split right is most of what value objects are for.
    private static Money ValidPrice(Money price)
    {
        if (price.IsNegative)
        {
            throw new DomainRuleException("Product price cannot be negative.");
        }

        return price;
    }

    // WHY: EF Core materialization only.
    private Product()
    {
        Name = null!;
        Description = null!;
    }
}
