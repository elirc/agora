using System.Text;
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

    // LEARN: generation and validation are DIFFERENT jobs. `From` says "this string already IS a
    // slug — prove it"; `Generate` says "turn this human text INTO a slug." Admin create forms want
    // the second (type a product name, get "trail-running-shoes" for free); a URL path segment wants
    // the first (it must already be a slug or it's a 422). Both end at the same door — Generate hands
    // its result to From — so there is exactly one definition of what a valid slug is.
    // WHY this is a *create-time* convenience only: once a product exists its slug is a PERMALINK and
    // never regenerates, even when the name changes (see Product — renaming does not re-slug). A slug
    // that chased its name would break every saved link and inbound search result the day of a typo
    // fix. Generation is a starting suggestion; immutability is the contract. (ADR-0005.)
    public static Slug Generate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new DomainRuleException("Cannot generate a slug from empty text.");
        }

        // Fold to lowercase, then collapse every run of non-[a-z0-9] into a single hyphen and trim
        // the ends. Non-ASCII letters simply drop out — the result is a URL-safe ASCII slug or a
        // clear failure, never a half-encoded mess.
        string candidate = Collapse(text);

        if (candidate.Length == 0)
        {
            throw new DomainRuleException(
                $"'{text}' has no characters a slug can be built from — provide an explicit slug.");
        }

        // WHY re-enter through From: length and shape are enforced in ONE place. A 300-character
        // product name generates a too-long slug and is rejected here, not silently truncated.
        return From(candidate);
    }

    // Collapse arbitrary text to hyphen-joined lowercase alphanumeric groups (no leading/trailing/
    // doubled hyphens) — the exact shape SlugPattern accepts.
    private static string Collapse(string text)
    {
        var builder = new StringBuilder(text.Length);
        bool lastWasHyphen = true; // treat the start as a boundary so a leading run adds no hyphen

        foreach (char c in text.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c))
            {
                builder.Append(c);
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen)
            {
                builder.Append('-');
                lastWasHyphen = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
