using Agora.Domain.Values;

namespace Agora.Domain.Pricing;

/// <summary>
/// A scheduled sale on one product: a discount that applies for a window of time and then stops,
/// with nobody having to remember to switch it off.
/// </summary>
// 📘 THE SHAPE OF THIS ENTITY IS THE SPRINT'S ARGUMENT. The tempting design is a boolean —
// `Product.IsOnSale` plus a `SalePrice` — flipped by whoever is awake at midnight. That design has
// no answer to any of the questions a store actually asks: what was this priced at last Tuesday
// (the order says $34.50 — was that the sale or a mistake?); what is scheduled for next weekend;
// why did it stay on sale for three days after the sale ended. A boolean records only NOW, and NOW
// is the one moment you never need to look up afterwards.
//
// A window records the FACT instead: this discount applied over this interval. "Is it on sale?" then
// stops being state anybody maintains and becomes a QUESTION WITH AN ARGUMENT — on sale AS OF WHEN?
// The storefront asks as of now, an order asks as of when it was placed (S07), a report asks as of
// last Tuesday, and all three get a correct answer from the same row. Nobody flips anything.
// 🔗 the same shape, one sprint later: S06's stock reservations are a window too (see DateRange).
public sealed class SaleCampaign
{
    public const int MaxNameLength = 120;

    public Guid Id { get; }

    /// <summary>The product this campaign discounts. Campaigns are per-product in S04 (ADR-0006).</summary>
    public Guid ProductId { get; }

    /// <summary>What the admin calls it — "Labour Day Weekend" — for the back-office and the badge.</summary>
    public string Name { get; private set; }

    public Discount Discount { get; }

    public DateRange Window { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    private SaleCampaign(
        Guid id,
        Guid productId,
        string name,
        Discount discount,
        DateRange window,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        ProductId = productId;
        Name = name;
        Discount = discount;
        Window = window;
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>Schedule a sale on a product for a window of time.</summary>
    // WHY the window may be in the past, the future, or straddling now, and this method does not
    // care: scheduling is not activating. Backdating a campaign is a legitimate act (the sale ran,
    // someone is recording it), and forbidding it would mean this method needs a clock — which would
    // make every test of it depend on when it ran. The clock belongs to the CALLER, who knows what
    // "now" means for the request it is serving.
    public static SaleCampaign Schedule(
        Guid productId,
        string? name,
        Discount discount,
        DateRange window,
        DateTimeOffset createdAtUtc)
    {
        if (productId == Guid.Empty)
        {
            throw new DomainRuleException("A sale campaign must belong to a product.");
        }

        // A sale that covers no instants can never discount anything. DateRange allows an empty
        // window (it is what ending a not-yet-started campaign collapses to); being BORN empty is a
        // mistake, and that rule belongs here, to the sale — not to intervals in general.
        if (window.IsEmpty)
        {
            throw new DomainRuleException("A sale campaign must run for a non-empty window of time.");
        }

        return new SaleCampaign(
            Guid.NewGuid(),
            productId,
            ValidName(name),
            discount,
            window,
            createdAtUtc);
    }

    /// <summary>Whether this campaign's window covers <paramref name="instant"/>.</summary>
    // 🔍 THE PURE CHECK — and the one every price the storefront quotes goes through. Read the
    // signature: the instant is a PARAMETER. This function has no clock, no timezone, no ambient
    // state, and therefore no way to disagree with itself between two machines or two runs. Pin a
    // campaign's boundaries in a test and they stay pinned in 2031.
    // TRY: a campaign starts "at midnight". Write the test that pins WHOSE midnight that is. The
    // window's endpoints are DateTimeOffsets — instants, not wall-clock readings — so the answer is
    // in the data, not in the server's locale. Now find every place an instant enters this pipeline
    // (there are exactly two: ProductPricing's clock, and the repository's `asOf` filter) and satisfy
    // yourself that each one names it out loud.
    public bool IsActiveAt(DateTimeOffset instant) => Window.Contains(instant);

    /// <summary>Whether this campaign is running as of now.</summary>
    public bool IsActive()
    {
        DateTime now = DateTime.Now;

        return now >= Window.Starts.DateTime && now < Window.Ends.DateTime;
    }

    /// <summary>Whether the campaign was already over by <paramref name="instant"/>.</summary>
    public bool HasEndedBy(DateTimeOffset instant) => instant >= Window.Ends;

    /// <summary>Stop the sale now: cut the window short at <paramref name="nowUtc"/>.</summary>
    // LEARN: "end the sale" is not a delete and not a flag — it is an EDIT TO THE WINDOW. The row
    // survives, so next quarter's report can still say this ran from Friday to Sunday-at-11am when
    // someone pulled it; and because the window is the only source of truth, cutting it short is the
    // whole operation. There is no second field to keep in sync, which is why there is no way for the
    // two of them to disagree.
    public void End(DateTimeOffset nowUtc)
    {
        if (HasEndedBy(nowUtc))
        {
            throw new DomainRuleException("This sale campaign has already ended.");
        }

        Window = Window.EndingAt(nowUtc);
    }

    private static string ValidName(string? name)
    {
        string trimmed = (name ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            throw new DomainRuleException("Sale campaign name is required.");
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new DomainRuleException($"Sale campaign name exceeds {MaxNameLength} characters.");
        }

        return trimmed;
    }

    // WHY: EF Core materialization only.
    private SaleCampaign()
    {
        Name = null!;
    }
}
