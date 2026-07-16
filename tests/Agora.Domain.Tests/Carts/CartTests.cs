using Agora.Domain;
using Agora.Domain.Carts;
using Agora.Domain.Values;

namespace Agora.Domain.Tests.Carts;

public class CartTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 10, 0, 0, TimeSpan.Zero);

    private static Money Usd(decimal amount) => Money.Of(amount, CurrencyCode.Usd);

    // ── Who holds a cart ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void An_anonymous_cart_is_held_by_a_token_and_owned_by_nobody()
    {
        var cart = Cart.ForToken("tokenhash-1", Now);

        Assert.Equal("tokenhash-1", cart.TokenHash);
        Assert.Null(cart.UserId);
        Assert.False(cart.IsRetired);
        Assert.Empty(cart.Lines);
    }

    [Fact]
    public void An_owned_cart_is_held_by_a_user_and_has_no_token()
    {
        Guid userId = Guid.NewGuid();

        var cart = Cart.ForUser(userId, Now);

        Assert.Equal(userId, cart.UserId);
        Assert.Null(cart.TokenHash);
    }

    // 🔍 the two factories are the ONLY doors, so "exactly one holder" is not a rule anybody has to
    // remember to check — there is no way to construct the illegal state. These two tests are what
    // stops someone quietly adding a third factory that takes both.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_anonymous_cart_without_a_token_is_refused(string? tokenHash)
    {
        Assert.Throws<DomainRuleException>(() => Cart.ForToken(tokenHash, Now));
    }

    [Fact]
    public void An_owned_cart_without_a_user_is_refused()
    {
        Assert.Throws<DomainRuleException>(() => Cart.ForUser(Guid.Empty, Now));
    }

    // ── Quantity rules ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Adding_a_product_creates_a_line_that_remembers_the_price_it_was_quoted()
    {
        var cart = Cart.ForToken("t", Now);

        CartLine line = cart.AddOrIncrease(Guid.NewGuid(), 2, Usd(34.50m), Now);

        Assert.Equal(2, line.Quantity);
        Assert.Equal(Usd(34.50m), line.PriceAtAddition);
        Assert.Equal(Now, line.AddedAtUtc);
        Assert.Equal(Now, cart.UpdatedAtUtc);
    }

    [Fact]
    public void Adding_the_same_product_twice_increases_one_line_rather_than_making_two()
    {
        var cart = Cart.ForToken("t", Now);
        Guid productId = Guid.NewGuid();

        cart.AddOrIncrease(productId, 2, Usd(34.50m), Now);
        cart.AddOrIncrease(productId, 3, Usd(34.50m), Now.AddMinutes(1));

        CartLine line = Assert.Single(cart.Lines);
        Assert.Equal(5, line.Quantity);
    }

    // 📘 THE SNAPSHOT DOES NOT MOVE when you add more of something you already have — and this is the
    // test that pins it, because "re-stamp it, it's fresher" is the obvious-looking change. If the
    // snapshot re-took itself here it would always equal the live price by the time anyone looked,
    // and PriceChanged could never be true. The fact is "this product entered your cart THEN, quoted
    // at THAT"; clicking + does not make it arrive again.
    [Fact]
    public void Increasing_a_line_does_not_re_stamp_the_price_it_was_quoted_at()
    {
        var cart = Cart.ForToken("t", Now);
        Guid productId = Guid.NewGuid();

        cart.AddOrIncrease(productId, 1, Usd(34.50m), Now);
        cart.AddOrIncrease(productId, 1, Usd(27.60m), Now.AddDays(1));

        CartLine line = Assert.Single(cart.Lines);
        Assert.Equal(Usd(34.50m), line.PriceAtAddition);
        Assert.Equal(Now, line.AddedAtUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_line_cannot_be_for_fewer_than_one(int quantity)
    {
        var cart = Cart.ForToken("t", Now);

        Assert.Throws<DomainRuleException>(
            () => cart.AddOrIncrease(Guid.NewGuid(), quantity, Usd(9.99m), Now));
    }

    [Fact]
    public void A_line_cannot_exceed_the_maximum()
    {
        var cart = Cart.ForToken("t", Now);

        Assert.Throws<DomainRuleException>(
            () => cart.AddOrIncrease(Guid.NewGuid(), CartLine.MaxQuantity + 1, Usd(9.99m), Now));
    }

    // ⚠️ 98 + 5 THROWS; it does not become 99. Clamping would charge a shopper for a number they
    // never chose and never hear about — and the shopper is right there, able to pick a smaller one,
    // if only we tell them.
    [Fact]
    public void Increasing_past_the_maximum_is_refused_rather_than_clamped()
    {
        var cart = Cart.ForToken("t", Now);
        Guid productId = Guid.NewGuid();
        cart.AddOrIncrease(productId, CartLine.MaxQuantity - 1, Usd(9.99m), Now);

        Assert.Throws<DomainRuleException>(() => cart.AddOrIncrease(productId, 5, Usd(9.99m), Now));

        // …and the refusal left the line exactly as it was. A guard that throws AFTER mutating is a
        // guard that turns a 422 into corruption.
        Assert.Equal(CartLine.MaxQuantity - 1, Assert.Single(cart.Lines).Quantity);
    }

    [Fact]
    public void Exactly_the_maximum_is_allowed()
    {
        var cart = Cart.ForToken("t", Now);

        CartLine line = cart.AddOrIncrease(Guid.NewGuid(), CartLine.MaxQuantity, Usd(9.99m), Now);

        Assert.Equal(CartLine.MaxQuantity, line.Quantity);
    }

    [Fact]
    public void A_quoted_price_cannot_be_negative()
    {
        var cart = Cart.ForToken("t", Now);

        Assert.Throws<DomainRuleException>(
            () => cart.AddOrIncrease(Guid.NewGuid(), 1, Usd(-1m), Now));
    }

    // ── Editing a cart ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Setting_a_quantity_replaces_it_rather_than_adding_to_it()
    {
        var cart = Cart.ForToken("t", Now);
        CartLine line = cart.AddOrIncrease(Guid.NewGuid(), 5, Usd(9.99m), Now);

        cart.SetQuantity(line.Id, 2, Now.AddMinutes(1));

        Assert.Equal(2, line.Quantity);
        Assert.Equal(Now.AddMinutes(1), cart.UpdatedAtUtc);
    }

    [Fact]
    public void Setting_a_quantity_to_zero_is_refused_because_zero_is_not_a_quantity()
    {
        var cart = Cart.ForToken("t", Now);
        CartLine line = cart.AddOrIncrease(Guid.NewGuid(), 5, Usd(9.99m), Now);

        Assert.Throws<DomainRuleException>(() => cart.SetQuantity(line.Id, 0, Now));
        Assert.Equal(5, line.Quantity);
    }

    [Fact]
    public void Removing_a_line_takes_the_product_out()
    {
        var cart = Cart.ForToken("t", Now);
        CartLine line = cart.AddOrIncrease(Guid.NewGuid(), 1, Usd(9.99m), Now);
        cart.AddOrIncrease(Guid.NewGuid(), 1, Usd(19.99m), Now);

        cart.RemoveLine(line.Id, Now.AddMinutes(1));

        Assert.Single(cart.Lines);
        Assert.Null(cart.LineOrDefault(line.Id));
    }

    [Fact]
    public void Clearing_empties_every_line()
    {
        var cart = Cart.ForToken("t", Now);
        cart.AddOrIncrease(Guid.NewGuid(), 1, Usd(9.99m), Now);
        cart.AddOrIncrease(Guid.NewGuid(), 1, Usd(19.99m), Now);

        cart.Clear(Now.AddMinutes(1));

        Assert.Empty(cart.Lines);
        Assert.Equal(Now.AddMinutes(1), cart.UpdatedAtUtc);
    }

    // A line id from somebody else's cart is not a line in this one. The domain says "not in this
    // cart"; the use case turns that into a 404 (see UpdateCartLineQuantity for why 404 and not 403).
    [Fact]
    public void A_line_that_is_not_in_this_cart_cannot_be_touched()
    {
        var mine = Cart.ForToken("t", Now);
        var theirs = Cart.ForToken("u", Now);
        CartLine theirLine = theirs.AddOrIncrease(Guid.NewGuid(), 1, Usd(9.99m), Now);

        Assert.Null(mine.LineOrDefault(theirLine.Id));
        Assert.Throws<DomainRuleException>(() => mine.SetQuantity(theirLine.Id, 2, Now));
        Assert.Throws<DomainRuleException>(() => mine.RemoveLine(theirLine.Id, Now));
    }

    // ── Line totals: Money doing the arithmetic ──────────────────────────────────────────────

    // 🔗 the price is a PARAMETER, not the snapshot — a line cannot tell you what it costs, because a
    // line does not know what its product costs. It knows what its product cost once.
    [Fact]
    public void A_line_total_is_the_price_in_force_times_the_quantity_not_the_snapshot()
    {
        var cart = Cart.ForToken("t", Now);
        CartLine line = cart.AddOrIncrease(Guid.NewGuid(), 3, Usd(34.50m), Now);

        Assert.Equal(Usd(82.80m), line.TotalAt(Usd(27.60m)));
        Assert.Equal(Usd(103.50m), line.TotalAt(line.PriceAtAddition));
    }

    // ── Retirement ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Retiring_records_when_and_leaves_the_lines_intact()
    {
        var cart = Cart.ForToken("t", Now);
        cart.AddOrIncrease(Guid.NewGuid(), 2, Usd(9.99m), Now);

        cart.Retire(Now.AddMinutes(5));

        Assert.True(cart.IsRetired);
        Assert.Equal(Now.AddMinutes(5), cart.RetiredAtUtc);
        // The evidence survives. When a shopper says "signing in ate my cart", this row is the
        // difference between an investigation and an apology.
        Assert.Single(cart.Lines);
    }

    [Fact]
    public void A_cart_cannot_be_retired_twice()
    {
        var cart = Cart.ForToken("t", Now);
        cart.Retire(Now);

        Assert.Throws<DomainRuleException>(() => cart.Retire(Now.AddMinutes(1)));
    }
}
