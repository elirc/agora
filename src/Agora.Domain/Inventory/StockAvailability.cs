namespace Agora.Domain.Inventory;

/// <summary>
/// What a shopper may actually have, as of an instant: what is on the shelf, minus what other people
/// are currently holding.
/// </summary>
// 🔍 READ THE SIGNATURES AND NOTICE THERE IS NO CLOCK IN THIS FILE. Availability is a FUNCTION OF
// TIME, in exactly the sense a sale price is (S04's PriceResolver is this file's sibling, and they are
// the same shape on purpose): ask about a moment and you get that moment's answer.
//
// That is why "3 left" is a sentence with a timestamp hiding in it, and why the storefront badge is
// advisory rather than a promise. Between the render and the click, four minutes of reservations can
// expire and one can be taken. The number was true when it was computed and the computation is
// reproducible; neither of those makes it true *now*.
//
// The promise is made in exactly one place — the moment a reservation is written, under the stock
// row's concurrency token — and nowhere else. Everything else is a read of a moving number.
public static class StockAvailability
{
    /// <summary>
    /// Units of <paramref name="stock"/> a new shopper could claim at <paramref name="asOf"/>, given
    /// every <paramref name="reservations"/> candidate for that product.
    /// </summary>
    public static int Available(
        StockLevel stock,
        IEnumerable<Reservation> reservations,
        DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(stock);
        ArgumentNullException.ThrowIfNull(reservations);

        int held = HeldAt(reservations, asOf);

        // ⚠️ CLAMPED AT ZERO, and this is not defensive padding — it is the honest treatment of a
        // state that CAN legitimately exist. A stock take can find two skillets where the system
        // believed three, while three are already reserved by people mid-checkout. On-hand becomes 2,
        // held is 3, and the arithmetic says -1.
        //
        // Nobody is wrong here. The reservations were valid promises when made; the count is the
        // physical truth. What must not happen is that -1 escapes into a UI as "-1 left", or into a
        // comparison where `quantity <= available` accidentally admits somebody. To a NEW shopper,
        // "less than nothing available" and "nothing available" are the same sentence: no.
        //
        // The people already holding those three claims are a separate, human problem — the shop owes
        // one of them an apology, and no amount of Math.Max fixes that. S15's runbook is where an
        // oversell like this gets a procedure. What this line guarantees is only that the arithmetic
        // does not make it worse by lying in the other direction.
        return Math.Max(0, stock.OnHand - held);
    }

    /// <summary>Units currently spoken for by live reservations at <paramref name="asOf"/>.</summary>
    public static int HeldAt(IEnumerable<Reservation> reservations, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(reservations);

        return reservations
            .Where(reservation => reservation.IsActiveAt(asOf))
            .Sum(reservation => reservation.Quantity);
    }

    /// <summary>
    /// Whether <paramref name="quantity"/> could be claimed at <paramref name="asOf"/> — the
    /// friendly answer, not the authoritative one.
    /// </summary>
    // ⚠️ THIS IS THE `if` IN A CHECK-THEN-ACT, AND IT DOES NOT PROTECT ANYTHING. Two requests can
    // both call this about the last skillet, a microsecond apart, and both be told yes — and both
    // would be right, because at the instant each one asked, one skillet was available.
    //
    // That is not a flaw in this function. It is what a check IS. The protection lives one layer down,
    // where the reservation is written against StockLevel's concurrency token: whoever commits second
    // finds the row has moved, is rejected, retries, and *then* this function tells them the truth.
    //
    // This exists to give a fast, friendly, usually-correct answer — so the common case (plenty in
    // stock) never pays for a conflict, and the shopper gets "out of stock" instead of a retry. It is
    // an optimisation and a courtesy. It is not a lock, and any code that treats it as one has a race
    // with better manners. See ADR-0008 §The race we keep.
    public static bool CanTake(
        StockLevel stock,
        IEnumerable<Reservation> reservations,
        int quantity,
        DateTimeOffset asOf) =>
        quantity > 0 && quantity <= Available(stock, reservations, asOf);
}
