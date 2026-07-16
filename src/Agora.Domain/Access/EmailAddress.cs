namespace Agora.Domain.Access;

/// <summary>A normalized email address. Case-insensitive by construction.</summary>
// LEARN: Agora's second value object (Slug was the first, in S01). New wrinkle:
// NORMALIZATION. Emails compare case-insensitively in practice, so we lowercase once at
// the boundary — and now "Ada@Example.com" and "ada@example.com" cannot become two accounts.
// The unique index on the users table (Infrastructure) trusts this normalization.
public readonly record struct EmailAddress
{
    public const int MaxLength = 254;

    public string Value { get; }

    private EmailAddress(string value) => Value = value;

    public static EmailAddress From(string? raw)
    {
        string candidate = (raw ?? string.Empty).Trim().ToLowerInvariant();

        // WHY: shape check only — one '@' with something on both sides, no spaces.
        // Full RFC 5322 validation rejects real addresses and accepts fake ones; the only
        // true validator is a delivered message (out of scope — Agora never sends email).
        int at = candidate.IndexOf('@');
        bool shapeOk = candidate.Length is > 2 and <= MaxLength
            && at > 0
            && at == candidate.LastIndexOf('@')
            && at < candidate.Length - 1
            && !candidate.Contains(' ');

        if (!shapeOk)
        {
            throw new DomainRuleException($"'{raw}' is not a usable email address.");
        }

        return new EmailAddress(candidate);
    }

    public override string ToString() => Value;
}
