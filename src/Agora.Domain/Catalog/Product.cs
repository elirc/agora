namespace Agora.Domain.Catalog;

/// <summary>
/// Something a shopper can buy. Belongs to exactly one <see cref="Category"/>. Seed-only
/// and read-only through the API in Sprint 01; admin CRUD and optimistic concurrency
/// arrive in Sprint 03, real pricing in Sprint 04.
/// </summary>
public sealed class Product
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 4000;

    public Guid Id { get; }
    public Guid CategoryId { get; }
    public string Name { get; private set; }
    public Slug Slug { get; private set; }
    public Sku Sku { get; private set; }
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
