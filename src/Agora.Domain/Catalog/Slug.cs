using System.Text.RegularExpressions;

namespace Agora.Domain.Catalog;

/// <summary>
/// A URL-safe identifier for a catalog resource (e.g. <c>trail-running-shoes</c>).
/// Agora's first value object — the shape every storefront link and API path depends on.
/// </summary>
// LEARN: primitive obsession is the gateway bug. A `string slug` accepts "Trail Shoes",
// "  ", "../etc/passwd" and ""; a Slug accepts exactly one shape — lowercase ASCII words
// joined by single hyphens — verified once, at the boundary. Every function that receives
// a Slug afterwards can stop checking. See docs/concepts/fitness-functions.md: types are
// fitness functions too.
public readonly partial record struct Slug
{
    public const int MaxLength = 120;

    public string Value { get; }

    private Slug(string value) => Value = value;

    public static Slug From(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new DomainRuleException("Slug is required.");
        }

        // WHY: normalize case at the door — slugs are compared and routed on, so "Shoes"
        // and "shoes" must not be two different resources. Trim first; the rest is shape.
        string candidate = raw.Trim().ToLowerInvariant();

        if (candidate.Length > MaxLength)
        {
            throw new DomainRuleException($"Slug exceeds {MaxLength} characters.");
        }

        // WHY: the pattern IS the contract — lowercase letters/digits in hyphen-separated
        // groups, no leading/trailing/double hyphens. Anything else is not a slug, and the
        // caller finds out here rather than three layers down as a broken link.
        if (!SlugPattern().IsMatch(candidate))
        {
            throw new DomainRuleException(
                $"'{raw}' is not a valid slug (use lowercase words separated by single hyphens).");
        }

        return new Slug(candidate);
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
