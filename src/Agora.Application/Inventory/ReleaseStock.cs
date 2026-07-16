using Agora.Application.Abstractions;
using Agora.Domain.Inventory;

namespace Agora.Application.Inventory;

/// <summary>Give back everything a cart was holding — it checked out, or gave up.</summary>
public sealed class ReleaseCartReservations(
    IInventoryRepository inventory,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<int> ExecuteAsync(Guid cartId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();

        IReadOnlyList<Reservation> reservations = await inventory.ListForCartAsync(cartId, cancellationToken);

        int released = 0;

        foreach (Reservation reservation in reservations)
        {
            if (reservation.IsReleased)
            {
                continue;
            }

            reservation.Release(now);
            released++;
        }

        // Committing unconditionally, including when nothing changed. A CommitAsync with an empty
        // change set is a no-op EF handles for free, and the alternative — `if (released > 0)` — is a
        // branch whose only purpose is to save a database round trip that does not happen, at the cost
        // of a reader wondering what the branch protects.
        await unitOfWork.CommitAsync(cancellationToken);

        return released;
    }
}

/// <summary>The janitor: tidy up claims whose windows have closed.</summary>
// ⚠️ THIS IS NOT WHAT MAKES EXPIRY WORK, and the distinction is the most important thing on this
// page. A reservation whose window has passed already stopped counting — StockAvailability filters on
// IsActiveAt(asOf), so an expired claim is invisible to every shopper the microsecond it lapses, with
// nothing running and nobody notified.
//
// What this does is mark the rows, so that queries stay small, the admin view is legible, and a human
// reading the table can tell "lapsed" from "live" without doing date arithmetic in their head. If
// this job is switched off for a week, the shop is CORRECT and untidy. That is the property to aim
// for: a background job whose failure is a mess rather than an outage.
//
// The opposite design — availability reads a `Reserved` counter that only this sweep decrements — is
// the one that turns a stuck job into overselling, silently, at exactly the moment nobody is looking
// at the job. 🔗 S15's health checks watch this thing's lag for tidiness reasons, not correctness ones.
public sealed class ReleaseExpiredReservations(
    IInventoryRepository inventory,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    /// <summary>How many rows one pass will touch.</summary>
    // WHY a limit: an unbounded sweep is a table scan with a transaction wrapped around it, and the
    // first time it runs on a year of data it locks the inventory of a live shop. A capped pass that
    // runs again in a minute is strictly better than a perfect pass that takes the store down once.
    public const int BatchSize = 500;

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();

        IReadOnlyList<Reservation> expired =
            await inventory.ListExpiredAsync(now, BatchSize, cancellationToken);

        foreach (Reservation reservation in expired)
        {
            // 📘 IDEMPOTENT BY CONSTRUCTION, which is why this loop has no bookkeeping in it.
            // Reservation.Release is "make this released", not "do a release" — so a claim this pass
            // already tidied, or that a checkout released a millisecond ago, is simply passed over.
            // Two sweeps overlapping do not release twice, because there is no such thing as
            // releasing twice.
            //
            // The alternative — `Released++` inside a Release() that throws if already released —
            // makes every overlapping run a crash, and the fix for THAT is a try/catch that
            // eventually swallows a real error. Phrase the operation so the retry is boring.
            reservation.Release(now);
        }

        await unitOfWork.CommitAsync(cancellationToken);

        return expired.Count;
    }
}
