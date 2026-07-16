using Agora.Application.Abstractions;
using Agora.Application.Events;
using Agora.Domain.Carts;

namespace Agora.Application.Carts;

/// <summary>
/// Fold the anonymous cart a shopper was holding into the cart their account owns — the thing that
/// happens when they sign in.
/// </summary>
// 📘 THE SPRINT'S FLAGSHIP, and the reason carts and identity are one sprint rather than two. A cart
// that evaporates when you log in is worse than no cart at all: the shopper did the work, the store
// asked them to authenticate, and the store ate the result. So the anonymous cart follows them across
// the identity boundary — that crossing is this file, and the RULE for it is Cart.MergeFrom (take the
// larger quantity; ADR-0007 §Decision 3 argues it).
//
// 🔍 THE INTERESTING CASES ARE THE COLLISIONS. A product in one cart only is arithmetic — it moves,
// nobody has an opinion. A product in BOTH is a decision, and it is the only decision here: 2 on the
// phone and 3 in the account is 3, not 5, and not "whichever cart we read second". Read the tests in
// that order too; the disjoint case proves the plumbing, the collisions prove the policy.
public sealed class MergeCarts(
    ICartRepository carts,
    ISessionTokens tokens,
    IEventSink events,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    /// <summary>
    /// Merge the cart held by <paramref name="anonymousCartToken"/> into <paramref name="userId"/>'s
    /// cart. Does nothing when there is no token, no cart behind it, or nothing in it.
    /// </summary>
    // ⚠️ WHY THIS TAKES A userId INSTEAD OF READING ICurrentUser like every other cart use case: it
    // runs DURING sign-in. The session-cookie middleware ran at the top of a request that did not yet
    // have a session — the cookie is minted by this very response — so ICurrentUser is anonymous, and
    // correctly so. The id comes from the login that just succeeded, one line up in the endpoint. A
    // use case that reached for the ambient caller here would find nobody, take the anonymous branch,
    // and merge a cart into itself. Ports beat ambient state; this is the request that proves it.
    //
    // 📘 AND THIS METHOD IS IDEMPOTENT, which is not decoration — it is what makes the failure story
    // boring. Run it twice with the same token and the second run finds no live cart (the first
    // retired it) and returns. Run it twice on a collision and `max` gives the same answer both times.
    // So a login that failed after the merge, a double-submitted form, a response lost on the way
    // back — all of them are safe to retry, and "just try again" is a real answer rather than a
    // hopeful one. Compare `sum`, where a retry silently doubles the collision and the evidence is a
    // number that looks entirely plausible.
    public async Task ExecuteAsync(
        Guid userId,
        string? anonymousCartToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(anonymousCartToken))
        {
            return;
        }

        Cart? anonymous = await carts.FindActiveByTokenHashAsync(
            tokens.HashOf(anonymousCartToken), cancellationToken);

        // Nothing to merge: no cart behind that token, or an empty one.
        // WHY we do not retire an empty anonymous cart on the way past: it would be a write — a
        // transaction, a commit — to tidy a row whose holder is about to lose the cookie anyway (the
        // endpoint clears it either way), and which the abandoned-cart reaper collects for free. A
        // no-op should cost a read.
        if (anonymous is null || anonymous.Lines.Count == 0)
        {
            return;
        }

        DateTimeOffset now = clock.GetUtcNow();

        // 📘 WHY WE MERGE RATHER THAN "CLAIM", even for a customer who has never had a cart. Claiming
        // — set UserId, clear TokenHash, one UPDATE, done — is strictly cheaper for the commonest
        // case, and it is a second code path. Two ways a cart becomes owned means two sets of edge
        // cases, and the interesting one (the collision) would then be exercised ONLY by customers who
        // already had a cart, i.e. by the half of the traffic that is harder to reproduce. This way
        // there is one path: the boring case walks the same lines as the interesting one, so the
        // boring case tests it. An empty cart is a `new` and an INSERT. That is not a price worth a
        // branch.
        Cart owned = await carts.FindActiveByUserAsync(userId, cancellationToken)
            ?? NewOwnedCart(userId, now);

        owned.MergeFrom(anonymous, now);
        anonymous.Retire(now);

        await events.PublishAsync(CartEvents.Merged(owned, anonymous, now), cancellationToken);

        // LEARN: two carts changed and one may have been created — one commit. Either the shopper
        // signs in and their cart came with them, or neither happened. A merge that half-applied would
        // be a cart with some of the phone's items and an anonymous cart still holding the rest,
        // findable by a cookie the response is about to clear. This is IUnitOfWork doing the same job
        // it did for RegisterCustomer's user-and-session in S02, with more at stake.
        await unitOfWork.CommitAsync(cancellationToken);
    }

    private Cart NewOwnedCart(Guid userId, DateTimeOffset now)
    {
        var cart = Cart.ForUser(userId, now);
        carts.Add(cart);
        return cart;
    }
}
