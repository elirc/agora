namespace Agora.Domain.Values;

/// <summary>
/// An ISO 4217 currency code (e.g. <c>USD</c>) and the one thing the rest of the domain needs to
/// know about it: how many minor units it divides into.
/// </summary>
// LEARN: this type exists so <see cref="Money"/> can treat currency as part of its IDENTITY rather
// than as a label riding alongside a number. $5 and €5 are not "the same amount in two skins" —
// they are different values, and no arithmetic relates them without a rate someone chose. Making the
// currency a typed field (not a loose string) is what lets Money REFUSE to add them.
public readonly record struct CurrencyCode
{
    public const int Length = 3;

    private readonly string? _code;

    private CurrencyCode(string code) => _code = code;

    /// <summary>The three-letter code, uppercased at the door.</summary>
    // GOTCHA: `default(CurrencyCode)` is the one hole a struct value object cannot close — C# hands
    // out a zero-initialized instance to anyone who asks and no constructor runs. So the getter
    // refuses instead of silently reporting an empty currency: a default is a bug, and it says so at
    // the first read rather than three layers later as a mismatched-currency exception. Every real
    // instance comes through `From`.
    public string Code => _code
        ?? throw new DomainRuleException("A default(CurrencyCode) has no currency — use CurrencyCode.From.");

    /// <summary>
    /// How many decimal places this currency's amounts carry — the scale every <see cref="Money"/>
    /// in it is rounded to.
    /// </summary>
    // WHY a property and not a constant: it is the seam a real ISO 4217 table plugs into. Agora sells
    // in USD only (multi-currency is explicitly out of scope), and every currency it could plausibly
    // add next divides into 100. But JPY has ZERO minor units and KWD has three, so a store that
    // opens in Tokyo does not get to assume `Math.Round(x, 2)` — it gets to fill in this method.
    // Pinning the assumption behind a named member is what makes that a small change instead of a
    // grep for the number 2. (ADR-0006 records this as the multi-currency trigger.)
    public int MinorUnits => 2;

    /// <summary>USD — the only currency Agora prices in today.</summary>
    public static CurrencyCode Usd => From("USD");

    public static CurrencyCode From(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new DomainRuleException("Currency is required.");
        }

        string candidate = raw.Trim().ToUpperInvariant();

        // WHY only a shape check and not a whitelist of the ~180 live ISO codes: the shape is what
        // stops "dollars", "$" and "" from becoming currencies; a whitelist would be a table we must
        // maintain against a standard that changes, to reject codes we never accept anyway (the
        // Product's price currency is whatever the admin typed, and the store sells in one). Shape
        // here, policy in the ADR — and when a second currency arrives, the whitelist lands HERE, in
        // one place, which is the entire point of the type.
        if (candidate.Length != Length || !candidate.All(char.IsAsciiLetterUpper))
        {
            throw new DomainRuleException($"'{raw}' is not an ISO 4217 currency code.");
        }

        return new CurrencyCode(candidate);
    }

    public override string ToString() => Code;
}
