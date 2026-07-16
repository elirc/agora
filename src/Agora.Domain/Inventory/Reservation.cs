using Agora.Domain.Values;

namespace Agora.Domain.Inventory;

/// <summary>
/// A temporary claim on stock: this cart may have this many, until this instant.
/// </summary>
// 📘 A RESERVATION IS A PROMISE WITH AN EXPIRY DATE, and the expiry is not a chore — it is the whole
// reason the thing is safe to hand out.
//
// The two obvious designs both fail, and they fail in opposite directions:
//
//   • Decrement stock when someone adds to cart. Now a shopper who fills a cart and goes to lunch is
//     holding the last skillet hostage from people who would actually pay for it. The shop looks sold
//     out and isn't. Nothing ever gives the stock back, because "they closed the tab" is not an event
//     anybody receives.
//   • Decrement stock at the end, when the payment lands. Now two people can both be told "yes" and
//     one of them finds out at the packing bench. That is the bug the stakeholder reported: we sold
//     four of a thing we had three of.
//
// A reservation is the honest middle. It says yes to exactly one of them, and it says it for a few
// minutes rather than forever, so the cost of a shopper wandering off is a short wait rather than a
// permanent hole in the inventory. The window is short because it only has to cover a checkout, and
// it is a window rather than a lock because nobody is coming back to release it.
//
// ⚠️ AND NOTE WHAT MAKES THE EXPIRY WORK: nothing runs. `ExpiresAtUtc` is in the past, so
// `IsActiveAt(now)` is false, so `StockAvailability` stops counting it. That is the entire mechanism.
// The sweep (see ReleaseExpiredReservations) does NOT make expiry happen — it is a janitor that tidies
// rows that have already stopped mattering. A janitor that hasn't run is a mess; a janitor that must
// run for the numbers to be right is a bug waiting for its first outage. Get that ordering wrong and
// you have built a system whose correctness depends on a background job's uptime.
public sealed class Reservation
{
    public Guid Id { get; }

    public Guid ProductId { get; }

    /// <summary>The cart this claim is held for.</summary>
    // WHY the CART and not the user: the shopper reserving the last skillet may not have an account
    // yet. Stock is claimed by whoever is checking out, and S05 established that "whoever is checking
    // out" is a cart — which might be a signed-in customer or an anonymous browser holding a token.
    // Tying reservations to a user id would mean the last unit can only be protected for people who
    // already signed up, which is precisely backwards.
    public Guid CartId { get; }

    public int Quantity { get; }

    /// <summary>The window this claim is good for. Half-open, like every window in this codebase.</summary>
    public DateRange Window { get; }

    public DateTimeOffset? ReleasedAtUtc { get; private set; }

    public bool IsReleased => ReleasedAtUtc is not null;

    private Reservation(Guid id, Guid productId, Guid cartId, int quantity, DateRange window)
    {
        Id = id;
        ProductId = productId;
        CartId = cartId;
        Quantity = quantity;
        Window = window;
    }

    public static Reservation Take(
        Guid productId,
        Guid cartId,
        int quantity,
        DateTimeOffset nowUtc,
        TimeSpan holdFor)
    {
        if (productId == Guid.Empty)
        {
            throw new DomainRuleException("A reservation needs a product.");
        }

        if (cartId == Guid.Empty)
        {
            throw new DomainRuleException("A reservation needs a cart.");
        }

        if (quantity <= 0)
        {
            throw new DomainRuleException("A reservation must be for at least one unit.");
        }

        if (holdFor <= TimeSpan.Zero)
        {
            throw new DomainRuleException("A reservation must be held for a positive duration.");
        }

        return new Reservation(
            Guid.NewGuid(),
            productId,
            cartId,
            quantity,
            DateRange.From(nowUtc, nowUtc + holdFor));
    }

    /// <summary>Whether this claim still counts against availability at <paramref name="instant"/>.</summary>
    // 🔍 THE SAME SHAPE AS SaleCampaign.IsActiveAt — deliberately, and the resemblance is the point.
    // The instant is a parameter. There is no clock in this type. Ask it about a moment and it answers
    // about that moment, in a test, in 2031, on a laptop in Manila.
    //
    // Read the two conditions in the order they are written. A released claim stops counting at once,
    // whenever it was released — that is a decision somebody made. An unreleased claim stops counting
    // when its window ends — that is time passing, and nobody decided anything. Two different ways to
    // stop being true, and the second one needs no actor at all.
    public bool IsActiveAt(DateTimeOffset instant) => !IsReleased && Window.Contains(instant);

    /// <summary>Give the stock back: the checkout finished, or was abandoned, or the sweep tidied up.</summary>
    // ⚠️ RELEASING TWICE IS A NO-OP, NOT AN ERROR, and that is a deliberate choice rather than
    // sloppiness about invariants. Everything that releases a reservation is a retry waiting to
    // happen: the sweep runs on a timer and may overlap itself, a checkout may be double-submitted, a
    // response may be lost on the way back to a caller who then tries again. If the second release
    // throws, every one of those callers needs a try/catch around a thing that already worked, and the
    // first person to write `catch { }` there deletes a real error along with the harmless one.
    //
    // "Make this true" is safe to repeat. "Do this" is not. Where a method can be phrased as the
    // former, phrase it as the former. 🔗 S10 makes an entire sprint out of this idea.
    public void Release(DateTimeOffset nowUtc)
    {
        if (IsReleased)
        {
            return;
        }

        ReleasedAtUtc = nowUtc;
    }

    // WHY: EF Core materialization only.
    private Reservation()
    {
    }
}
