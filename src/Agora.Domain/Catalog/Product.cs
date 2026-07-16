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

    // TRY: PriceAmount + PriceCurrency are a decimal and a string — a Money value object
    // in two trench coats. Sprint 04 promotes them into a real `Money` type (exact decimal
    // arithmetic, currency as identity, banker's rounding). Grep for these two properties
    // then and watch how few call sites move — that locality is the payoff of keeping the
    // pair primitive-but-CONTAINED (never leaked as a loose decimal) until it earns a type.
    // WHY: no `double` anywhere near money, ever — even in the placeholder. `double` cannot
    // represent 0.10, so cents drift. Money is base-10 and exact; that means `decimal`.
    public decimal PriceAmount { get; private set; }
    public string PriceCurrency { get; private set; }

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

    private Product(
        Guid id,
        Guid categoryId,
        string name,
        Slug slug,
        Sku sku,
        string description,
        decimal priceAmount,
        string priceCurrency,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        CategoryId = categoryId;
        Name = name;
        Slug = slug;
        Sku = sku;
        Description = description;
        PriceAmount = priceAmount;
        PriceCurrency = priceCurrency;
        CreatedAtUtc = createdAtUtc;
        Version = 1;
    }

    public static Product Create(
        Guid categoryId,
        string? name,
        Slug slug,
        Sku sku,
        string? description,
        decimal priceAmount,
        string? priceCurrency,
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
            ValidPrice(priceAmount),
            ValidCurrency(priceCurrency),
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
        decimal priceAmount,
        string? priceCurrency)
    {
        if (categoryId == Guid.Empty)
        {
            throw new DomainRuleException("A product must belong to a category.");
        }

        CategoryId = categoryId;
        Name = ValidName(name);
        Description = ValidDescription(description);
        PriceAmount = ValidPrice(priceAmount);
        PriceCurrency = ValidCurrency(priceCurrency);
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

    private static decimal ValidPrice(decimal priceAmount)
    {
        if (priceAmount < 0m)
        {
            throw new DomainRuleException("Product price cannot be negative.");
        }

        return priceAmount;
    }

    private static string ValidCurrency(string? currency)
    {
        // WHY: still just an ISO 4217 string check, deliberately shallow. When Money lands
        // (S04) this becomes a CurrencyCode value object; until then the invariant lives
        // here so no Product exists with a nonsense currency.
        if (currency is not { Length: 3 } || !currency.All(char.IsAsciiLetterUpper))
        {
            throw new DomainRuleException($"'{currency}' is not an ISO 4217 currency code.");
        }

        return currency;
    }

    // WHY: EF Core materialization only.
    private Product()
    {
        Name = null!;
        Description = null!;
        PriceCurrency = null!;
    }
}
