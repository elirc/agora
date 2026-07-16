using Agora.Application.Abstractions;
using Agora.Application.Errors;
using Agora.Domain.Inventory;

namespace Agora.Application.Inventory;

/// <summary>How long a claim on stock is good for.</summary>
// WHY fifteen minutes, and why it is a named constant rather than a number in a method: it is a
// BUSINESS decision wearing a technical costume. Too short and a shopper hunting for their card loses
// the thing they were buying. Too long and one abandoned checkout keeps the last unit off the shelf
// through a lunch hour. Fifteen is long enough to find a wallet and short enough that nobody notices
// the wait — and when someone eventually wants it configurable per product (they will), this is the
// one place that changes.
public static class ReservationPolicy
{
    public static readonly TimeSpan HoldFor = TimeSpan.FromMinutes(15);
}

/// <summary>Claim stock for a cart, so that a checkout can be honest about having it.</summary>
public sealed class ReserveStock(
    IInventoryRepository inventory,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<ReservationResult> ExecuteAsync(
        Guid productId,
        Guid cartId,
        int quantity,
        CancellationToken cancellationToken)
    {
        // ⚠️ CHECK-THEN-ACT STARTS HERE. Everything from this read to the CommitAsync below is a
        // window in which another request can do exactly the same thing and reach exactly the same
        // conclusion about the same last skillet. Read the code with that in mind: there is no `if`
        // anywhere below that closes it, and adding one would only make the window narrower and the
        // false confidence larger.
        //
        // What actually protects the last unit is at the bottom of this method. See ADR-0008.
        DateTimeOffset now = clock.GetUtcNow();

        StockSnapshot? snapshot = await inventory.SnapshotAsync(productId, cancellationToken)
            ?? throw new NotFoundException("stock", productId);

        // ⚠️ AVAILABILITY IS COMPUTED AGAINST *OTHER PEOPLE'S* CLAIMS, NOT ALL CLAIMS — and getting
        // this backwards is a bug I wrote on the way to this line, so it is worth the paragraph.
        //
        // A shopper already holding 2 of a shelf of 3, who changes their mind and asks for 3, is
        // asking to REPLACE their claim, not to add to it. Count their own live claim against them and
        // the arithmetic says "1 available, you asked for 3, no" — and the shop has just refused
        // somebody stock that it is holding *on their behalf*. They cannot buy it and nobody else can
        // either, until it expires.
        //
        // So a shopper's own claim is invisible to their own availability check. Everyone else's is
        // not. `held` is what OTHER people are holding; the rest of the shelf is theirs to ask for.
        Reservation? existing = snapshot.Reservations
            .SingleOrDefault(candidate => candidate.CartId == cartId && candidate.IsActiveAt(now));

        int heldByOthers = StockAvailability.HeldAt(
            snapshot.Reservations.Where(candidate => candidate.CartId != cartId),
            now);

        int availableToThisCart = Math.Max(0, snapshot.Stock.OnHand - heldByOthers);

        // The friendly answer. In the overwhelmingly common case — plenty on the shelf — this is also
        // the true one, and the shopper never pays for a conflict. When it says no, it says so in a
        // sentence a UI can render, rather than by making somebody lose a race they never entered.
        //
        // NOTHING HAS BEEN MUTATED ABOVE THIS LINE. The decision is made against a read; only after it
        // is made does anything change. The version I wrote first released the old claim *before*
        // checking, which works right up until the check says no — and then the shopper has silently
        // lost the claim they already had, in a request that "did nothing".
        if (quantity <= 0 || quantity > availableToThisCart)
        {
            return ReservationResult.OutOfStock(availableToThisCart);
        }

        // Replacing rather than stacking: a shopper who re-enters checkout should not accumulate
        // claims against themselves. 🔗 S10 makes this idea — a retry is not a new request — mandatory
        // rather than merely sensible.
        existing?.Release(now);

        var reservation = Reservation.Take(productId, cartId, quantity, now, ReservationPolicy.HoldFor);
        inventory.Add(reservation);

        // 📘 THIS LINE IS THE LOCK, and it does not look like one.
        //
        // Nothing physically moved — no skillet left the building — so touching the stock row seems
        // gratuitous. It is the entire protection. Bumping the version means the UPDATE that lands
        // with this transaction carries `WHERE Version = @loaded`, so if another request reserved
        // between our read and our write, our update matches zero rows and EF throws a concurrency
        // exception. One of the two racing shoppers gets the skillet; the other is told to try again,
        // re-reads availability, and is honestly told it's gone.
        //
        // The `if` above gives the friendly answer. THIS gives the correct one. Every layered defence
        // in this codebase has the same shape: S02's unique index on users.email, S05's unique
        // (CartId, ProductId), and now this. The domain is polite; the database is right.
        snapshot.Stock.ClaimedAgainst(now);

        await unitOfWork.CommitAsync(cancellationToken);

        return ReservationResult.Taken(reservation);
    }
}

/// <summary>Either the claim, or the reason there isn't one.</summary>
// 📘 A CLOSED RESULT for the outcome a caller must handle, and an EXCEPTION for the one they cannot.
// "Out of stock" is not exceptional — it is Tuesday, and every caller has something sensible to do
// about it. A missing product is exceptional: somebody asked about a thing that does not exist, and
// there is no sensible local recovery. Return the first; throw the second. 🔗 S09's payment result is
// this idea at full size.
public sealed record ReservationResult
{
    private ReservationResult(Reservation? reservation, int available)
    {
        Reservation = reservation;
        Available = available;
    }

    public Reservation? Reservation { get; }

    /// <summary>What was available when we looked — the number a UI should show if this failed.</summary>
    public int Available { get; }

    public bool Success => Reservation is not null;

    public static ReservationResult Taken(Reservation reservation) => new(reservation, reservation.Quantity);

    public static ReservationResult OutOfStock(int available) => new(null, available);
}
