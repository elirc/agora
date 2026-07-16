namespace Agora.Domain.Catalog;

/// <summary>
/// A stock-keeping unit — the merchant's own product code (e.g. <c>AGA-SHOE-014</c>).
/// Uppercase alphanumerics and hyphens; the identity a warehouse actually picks by.
/// </summary>
// LEARN: like Slug, a Sku is a string that has earned a type. A bare `string sku` would
// accept a product name, an empty cell from a spreadsheet import, or a slug by mistake.
// One constructor, one shape, verified once.
public readonly record struct Sku
{
    public const int MaxLength = 40;

    public string Value { get; }

    private Sku(string value) => Value = value;

    public static Sku From(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new DomainRuleException("SKU is required.");
        }

        string candidate = raw.Trim().ToUpperInvariant();

        if (candidate.Length > MaxLength)
        {
            throw new DomainRuleException($"SKU exceeds {MaxLength} characters.");
        }

        // WHY: uppercase A–Z, 0–9 and hyphen only. SKUs get printed on labels, typed into
        // search boxes and pasted into spreadsheets — a narrow, case-folded alphabet keeps
        // "aga-shoe-014" and "AGA-SHOE-014" from becoming two different units.
        foreach (char c in candidate)
        {
            if (!char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c) && c != '-')
            {
                throw new DomainRuleException(
                    $"'{raw}' is not a valid SKU (use letters, digits and hyphens).");
            }
        }

        return new Sku(candidate);
    }

    public override string ToString() => Value;
}
