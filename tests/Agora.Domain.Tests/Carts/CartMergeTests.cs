using Agora.Domain;
using Agora.Domain.Carts;
using Agora.Domain.Values;

namespace Agora.Domain.Tests.Carts;

/// <summary>
/// The merge policy, pinned. The disjoint case is plumbing; the collisions are the decision.
/// </summary>
// 🔍 READ THESE IN ORDER — they are the argument in ADR-0007 §Decision 3, executable. The first two
// prove nothing vanishes. The next four are the policy: the same product in both carts, with the
// larger quantity on either side, equal, and merged twice. If someone ever changes `Reconcile` to a
// `+`, the four go red and name the number they expected — which is the whole reason the policy is a
// named function with tests rather than a `Math.Max` inline in a loop.
public class CartMergeTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 10, 0, 0, TimeSpan.Zero);

    private static Money Usd(decimal amount) => Money.Of(amount, CurrencyCode.Usd);

    // ── Nothing vanishes ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Products_only_the_anonymous_cart_had_come_across()
    {
        Guid skillet = Guid.NewGuid();
        Guid board = Guid.NewGuid();

        var owned = Cart.ForUser(Guid.NewGuid(), Now);
        owned.AddOrIncrease(board, 1, Usd(19.99m), Now);

        var anonymous = Cart.ForToken("t", Now);
        anonymous.AddOrIncrease(skillet, 2, Usd(34.50m), Now);

        owned.MergeFrom(anonymous, Now.AddMinutes(1));

        Assert.Equal(2, owned.Lines.Count);
        Assert.Equal(2, owned.Lines.Single(line => line.ProductId == skillet).Quantity);
        Assert.Equal(1, owned.Lines.Single(line => line.ProductId == board).Quantity);
    }

    [Fact]
    public void Products_only_the_owned_cart_had_stay_put()
    {
        Guid board = Guid.NewGuid();
        var owned = Cart.ForUser(Guid.NewGuid(), Now);
        owned.AddOrIncrease(board, 4, Usd(19.99m), Now);

        owned.MergeFrom(Cart.ForToken("t", Now), Now.AddMinutes(1));

        Assert.Equal(4, Assert.Single(owned.Lines).Quantity);
    }

    // 📘 the snapshot and the added-at instant TRAVEL with the line — they are facts about what
    // happened to the shopper, and the shopper is the same person on both sides of the login.
    // Re-stamping them here would erase "the price dropped since you added this" at the exact moment
    // we finally know who to tell.
    [Fact]
    public void A_line_that_moves_keeps_the_price_and_the_instant_it_was_added_at()
    {
        Guid skillet = Guid.NewGuid();
        var anonymous = Cart.ForToken("t", Now.AddDays(-3));
        anonymous.AddOrIncrease(skillet, 2, Usd(34.50m), Now.AddDays(-3));

        var owned = Cart.ForUser(Guid.NewGuid(), Now);
        owned.MergeFrom(anonymous, Now);

        CartLine moved = Assert.Single(owned.Lines);
        Assert.Equal(Usd(34.50m), moved.PriceAtAddition);
        Assert.Equal(Now.AddDays(-3), moved.AddedAtUtc);
        // A new row in the new cart, though — the line belongs to the cart that now holds it.
        Assert.Equal(owned.Id, moved.CartId);
    }

    // ── The collisions: the policy ───────────────────────────────────────────────────────────

    // ⚠️ THE TEST THE WHOLE SPRINT ARGUES ABOUT. 2 on the phone, 3 in the account. The answer is 3.
    // Not 5 (sum would bill for a skillet nobody asked for — the shopper added 2 on their phone
    // BECAUSE they forgot the 3 in their account, which is the commonest reason a product is in both
    // carts at all). Not "whichever cart we read second". ADR-0007 §Decision 3.
    [Theory]
    [InlineData(3, 2, 3)] // owned bigger  → owned wins
    [InlineData(1, 4, 4)] // anonymous bigger → anonymous wins
    [InlineData(2, 2, 2)] // equal → no change, and definitely not 4
    public void The_same_product_in_both_carts_keeps_the_larger_quantity(
        int ownedQuantity,
        int anonymousQuantity,
        int expected)
    {
        Guid skillet = Guid.NewGuid();

        var owned = Cart.ForUser(Guid.NewGuid(), Now);
        owned.AddOrIncrease(skillet, ownedQuantity, Usd(34.50m), Now);

        var anonymous = Cart.ForToken("t", Now);
        anonymous.AddOrIncrease(skillet, anonymousQuantity, Usd(34.50m), Now);

        owned.MergeFrom(anonymous, Now.AddMinutes(1));

        CartLine line = Assert.Single(owned.Lines);
        Assert.Equal(expected, line.Quantity);
    }

    // 📘 A COLLISION CANNOT OVERFLOW, and this is the test that makes the point concrete. 60 and 50
    // sum to 110; the per-line maximum is 99. Under `sum`, this merge needs a second rule invented on
    // the spot — and a merge runs inside a LOGIN, so it cannot refuse: there is no shopper waiting on
    // a cart error, only a shopper waiting to be signed in. Its only option would be to clamp
    // silently. `max` of two legal quantities is legal, always, so there is no rule to invent and
    // nothing to get wrong.
    [Fact]
    public void A_collision_that_would_overflow_under_sum_is_simply_the_larger_of_the_two()
    {
        Guid skillet = Guid.NewGuid();

        var owned = Cart.ForUser(Guid.NewGuid(), Now);
        owned.AddOrIncrease(skillet, 60, Usd(1.00m), Now);

        var anonymous = Cart.ForToken("t", Now);
        anonymous.AddOrIncrease(skillet, 50, Usd(1.00m), Now);

        owned.MergeFrom(anonymous, Now.AddMinutes(1));

        Assert.Equal(60, Assert.Single(owned.Lines).Quantity);
    }

    [Fact]
    public void A_surviving_line_keeps_its_own_history_when_only_the_quantity_is_reconciled()
    {
        Guid skillet = Guid.NewGuid();

        var owned = Cart.ForUser(Guid.NewGuid(), Now);
        owned.AddOrIncrease(skillet, 1, Usd(34.50m), Now.AddDays(-7));

        var anonymous = Cart.ForToken("t", Now);
        anonymous.AddOrIncrease(skillet, 5, Usd(27.60m), Now);

        owned.MergeFrom(anonymous, Now.AddMinutes(1));

        CartLine line = Assert.Single(owned.Lines);
        Assert.Equal(5, line.Quantity);
        // Nothing about the line changed except how many. It was already here; it did not arrive
        // again, and its quote is still the quote it was given.
        Assert.Equal(Usd(34.50m), line.PriceAtAddition);
        Assert.Equal(Now.AddDays(-7), line.AddedAtUtc);
    }

    // 📘 IDEMPOTENCE, AND WHY IT IS THE ARGUMENT THAT SETTLES IT. max(max(a,b), b) == max(a,b). Merge
    // twice and you get the same cart — which matters because merging twice is a thing that happens:
    // a retried login, a double-submitted form, a response lost on the way back, and (from S10) an
    // at-least-once delivery doing exactly what it says on the tin. Under `sum` this test would read
    // 2, 5, 8 and the shopper's only evidence would be a number that looks entirely plausible.
    //
    // In production the repository's retired-cart filter means a second merge never finds the source
    // at all — but the ALGEBRA is what makes that a safety net rather than the only thing standing
    // between a shopper and a duplicate order. Two independent reasons a retry is safe; this is the
    // one that does not depend on a WHERE clause.
    [Fact]
    public void Merging_the_same_cart_twice_yields_the_same_cart()
    {
        Guid skillet = Guid.NewGuid();

        var owned = Cart.ForUser(Guid.NewGuid(), Now);
        owned.AddOrIncrease(skillet, 2, Usd(34.50m), Now);

        var anonymous = Cart.ForToken("t", Now);
        anonymous.AddOrIncrease(skillet, 3, Usd(34.50m), Now);

        owned.MergeFrom(anonymous, Now.AddMinutes(1));
        owned.MergeFrom(anonymous, Now.AddMinutes(2));

        Assert.Equal(3, Assert.Single(owned.Lines).Quantity);
    }

    // ── Guards ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Merging_an_empty_cart_changes_nothing_but_the_timestamp()
    {
        var owned = Cart.ForUser(Guid.NewGuid(), Now);
        owned.AddOrIncrease(Guid.NewGuid(), 2, Usd(9.99m), Now);

        owned.MergeFrom(Cart.ForToken("t", Now), Now.AddMinutes(1));

        Assert.Equal(2, Assert.Single(owned.Lines).Quantity);
    }

    [Fact]
    public void A_cart_cannot_be_merged_into_itself()
    {
        var cart = Cart.ForUser(Guid.NewGuid(), Now);

        Assert.Throws<DomainRuleException>(() => cart.MergeFrom(cart, Now));
    }
}
