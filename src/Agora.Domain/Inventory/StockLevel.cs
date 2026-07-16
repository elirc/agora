namespace Agora.Domain.Inventory;

/// <summary>
/// How many of a product are physically on the shelf. What is <em>available</em> to a new shopper is
/// a different question — see <see cref="StockAvailability"/>.
/// </summary>
// 📘 ON-HAND IS NOT AVAILABLE, and keeping those two ideas in different types is the whole model.
//
// On-hand is a fact about a shelf: three skillets exist. Available is a fact about a *moment*: three
// exist, two are spoken for by people currently in checkout, and one of those claims expires in four
// minutes — so "available" is 1 now and might be 2 shortly, with nobody having touched the shelf.
//
// The tempting design is a single `Available` column that everything increments and decrements. It is
// tempting because it makes reads trivial, and it is wrong because it makes the number a RUNNING
// TOTAL: every code path that forgets to decrement, double-decrements, or crashes halfway leaves the
// column permanently, silently untrue, and nothing can ever detect the drift because the column is
// its own source of truth. The count is then reconciled by a human with a clipboard.
//
// So: on-hand changes when physical objects move. Reservations are separate rows with their own
// lifetimes. Availability is COMPUTED from both, as of an instant, and is therefore always derivable
// and never drifts. The cost is a join. The benefit is that the number cannot be wrong.
public sealed class StockLevel
{
    public Guid Id { get; }

    public Guid ProductId { get; }

    /// <summary>Units physically in the building.</summary>
    public int OnHand { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    // 📘 THE SAME `int Version` S03 GAVE Product — and this time it is not about two admins.
    //
    // Reserving is a check-then-act: read what's available, decide, write a reservation. Between the
    // check and the act, another request can do the same thing, and both can honestly conclude that
    // the last skillet is theirs. No `if` can fix that, because the `if` is the thing being raced.
    //
    // The fix is to make the WRITE conditional on the world not having moved: every reservation bumps
    // this number, so the second writer's UPDATE matches zero rows, EF throws, and the request is told
    // to try again — at which point it re-reads availability and correctly discovers there is nothing
    // left. The `if` gives the friendly answer; the version closes the race. Belt and braces, the same
    // shape as S02's unique index on users.email and S05's unique (CartId, ProductId).
    //
    // S05's cart deliberately has NO version, and this is why that was not laziness: a cart is raced
    // by one person in two tabs over something that cannot run out. Stock is raced by strangers over
    // something that can. Same tool, opposite verdicts, because the question was never "should we use
    // optimistic concurrency" — it was "does a silent overwrite destroy something somebody wants".
    public int Version { get; private set; }

    private StockLevel(Guid id, Guid productId, int onHand, DateTimeOffset nowUtc)
    {
        Id = id;
        ProductId = productId;
        OnHand = onHand;
        UpdatedAtUtc = nowUtc;
        Version = 1;
    }

    public static StockLevel Create(Guid productId, int onHand, DateTimeOffset nowUtc)
    {
        if (productId == Guid.Empty)
        {
            throw new DomainRuleException("Stock must belong to a product.");
        }

        return new StockLevel(Guid.NewGuid(), productId, ValidOnHand(onHand), nowUtc);
    }

    /// <summary>Stock arrived: more of the thing now physically exists.</summary>
    public void Receive(int quantity, DateTimeOffset nowUtc)
    {
        if (quantity <= 0)
        {
            throw new DomainRuleException("Received quantity must be positive.");
        }

        OnHand += quantity;
        Bump(nowUtc);
    }

    /// <summary>
    /// Set on-hand to a counted figure — a stock take, a breakage, a correction.
    /// </summary>
    // WHY an absolute set and not an "adjust by -1": the person doing this is standing in the
    // stockroom having just counted the shelf. They know the answer, not the delta, and asking them to
    // do the subtraction is asking them to do arithmetic against a number they already believe is
    // wrong. Corrections are stated, not calculated.
    public void CountedAt(int onHand, DateTimeOffset nowUtc)
    {
        OnHand = ValidOnHand(onHand);
        Bump(nowUtc);
    }

    /// <summary>
    /// Note that the world moved: bumps the concurrency token so a racing writer's update is rejected.
    /// </summary>
    // WHY reserving touches this row at all, when a reservation is its own table: because the token is
    // what serialises the reservation race, and a token nobody touches serialises nothing. Reserving
    // does not change on-hand — no skillet moved — but it DOES change what the next shopper may
    // conclude, and that is precisely the thing two racing requests must not both conclude at once.
    public void ClaimedAgainst(DateTimeOffset nowUtc) => Bump(nowUtc);

    private static int ValidOnHand(int onHand) =>
        onHand < 0
            ? throw new DomainRuleException("On-hand stock cannot be negative.")
            : onHand;

    private void Bump(DateTimeOffset nowUtc)
    {
        Version++;
        UpdatedAtUtc = nowUtc;
    }

    // WHY: EF Core materialization only.
    private StockLevel()
    {
    }
}
