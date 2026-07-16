using Agora.Application.Carts;
using Agora.Application.Errors;
using Agora.Application.Events;
using Agora.Application.Pricing;
using Agora.Application.Tests.Fakes;
using Agora.Domain;
using Agora.Domain.Access;
using Agora.Domain.Carts;
using Agora.Domain.Catalog;
using Agora.Domain.Pricing;
using Agora.Domain.Values;

namespace Agora.Application.Tests.Carts;

/// <summary>The cart use cases against in-memory ports: identity, prices, and the refusals.</summary>
public class CartUseCaseTests
{
    private readonly Category _kitchen = CatalogFixtures.CategoryAt("Kitchen", "kitchen", 0);
    private readonly List<Product> _products = [];
    private readonly List<SaleCampaign> _campaigns = [];
    private readonly InMemoryCartRepository _carts = new();
    private readonly RecordingEventSink _events = new();
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly MovableClock _clock = new(CatalogFixtures.Base);
    private readonly SequentialSessionTokens _tokens = new();

    private readonly Product _skillet;
    private readonly Product _board;

    public CartUseCaseTests()
    {
        _skillet = CatalogFixtures.ProductAt(_kitchen.Id, "Skillet", "skillet", "AGA-KIT-001", 1, 34.50m);
        _board = CatalogFixtures.ProductAt(_kitchen.Id, "Board", "board", "AGA-KIT-002", 2, 19.99m);
        _products.AddRange([_skillet, _board]);
    }

    // ── Anonymous carts ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_shopper_with_no_account_and_no_cookie_has_an_empty_cart()
    {
        PricedCart cart = await GetCart(null).ExecuteAsync(null, CancellationToken.None);

        Assert.Empty(cart.Lines);
        Assert.Equal(Money.Of(0m, CurrencyCode.Usd), cart.Subtotal);
        // ⚠️ and NOTHING was written. A read that mints a cart would leave a row behind for every
        // crawler that ever touched the storefront.
        Assert.Empty(_carts.Rows);
    }

    // 📘 the first WRITE is what mints a token — the first thing that needs the browser to remember
    // anything. The use case hands the token back; the endpoint decides it becomes a cookie.
    [Fact]
    public async Task A_shoppers_first_add_mints_a_cart_token()
    {
        CartMutation result = await AddCartLine(null).ExecuteAsync(
            _skillet.Id, 2, null, CancellationToken.None);

        Assert.Equal("token-1", result.IssuedToken);
        Cart stored = Assert.Single(_carts.Rows);
        Assert.Equal("tokenhash-1", stored.TokenHash);
        Assert.Null(stored.UserId);
        Assert.Equal(1, _unitOfWork.Commits);
    }

    [Fact]
    public async Task A_shopper_presenting_their_token_gets_their_cart_back_and_mints_nothing_new()
    {
        await AddCartLine(null).ExecuteAsync(_skillet.Id, 2, null, CancellationToken.None);

        CartMutation second = await AddCartLine(null).ExecuteAsync(
            _board.Id, 1, "token-1", CancellationToken.None);

        Assert.Null(second.IssuedToken);
        Assert.Single(_carts.Rows);
        Assert.Equal(2, second.Cart.Lines.Count);
    }

    // ── Owned carts, and the boundary between them ───────────────────────────────────────────

    [Fact]
    public async Task A_signed_in_shoppers_cart_belongs_to_their_user_id_not_to_a_token()
    {
        Guid userId = Guid.NewGuid();

        CartMutation result = await AddCartLine(userId).ExecuteAsync(
            _skillet.Id, 1, null, CancellationToken.None);

        Assert.Null(result.IssuedToken);
        Cart stored = Assert.Single(_carts.Rows);
        Assert.Equal(userId, stored.UserId);
        Assert.Null(stored.TokenHash);
    }

    // ⚠️ THE IDENTITY BOUNDARY, PINNED. A signed-in caller who ALSO presents a cart cookie — which is
    // the normal state of affairs for about one request, and forever if anything goes wrong with the
    // clearing — gets THEIR cart. Not the token's. If this ever inverts, presenting a stolen or stale
    // agora_cart while signed in hands you somebody else's shopping, and every single-browser test in
    // the suite still passes. The identity you have beats the claim check you present.
    [Fact]
    public async Task A_signed_in_shopper_presenting_someone_elses_cart_token_still_gets_their_own_cart()
    {
        // A stranger's anonymous cart, holding a board.
        await AddCartLine(null).ExecuteAsync(_board.Id, 3, null, CancellationToken.None);

        // Ada signs in and — for whatever reason — still has that cookie in the jar.
        Guid ada = Guid.NewGuid();
        CartMutation result = await AddCartLine(ada).ExecuteAsync(
            _skillet.Id, 1, "token-1", CancellationToken.None);

        CartLine line = Assert.Single(result.Cart.Lines).Line;
        Assert.Equal(_skillet.Id, line.ProductId);
        Assert.Equal(2, _carts.Rows.Count);
        Assert.Equal(3, _carts.Rows.Single(cart => cart.TokenHash == "tokenhash-1").Lines[0].Quantity);
    }

    // ── Prices: live, snapshotted, and the difference reported ───────────────────────────────

    [Fact]
    public async Task A_line_records_the_price_in_force_when_it_was_added()
    {
        // A sale is running: the shopper is quoted 27.60, not the 34.50 list price.
        _campaigns.Add(CatalogFixtures.CampaignOn(_skillet.Id, Discount.PercentOff(20m), -60, 60));

        CartMutation result = await AddCartLine(null).ExecuteAsync(
            _skillet.Id, 2, null, CancellationToken.None);

        PricedCartLine line = Assert.Single(result.Cart.Lines);
        Assert.Equal(Money.Of(27.60m, CurrencyCode.Usd), line.Line.PriceAtAddition);
        Assert.Equal(Money.Of(27.60m, CurrencyCode.Usd), line.UnitPrice);
        Assert.Equal(Money.Of(55.20m, CurrencyCode.Usd), line.LineTotal);
        Assert.False(line.PriceChanged);
    }

    // 📘 THE SECOND TEACHING AXIS OF THE SPRINT, in one test. The shopper added a skillet during a
    // sale. The sale ended. They open the drawer:
    //   • the price is TODAY's (34.50) — a cart quotes what the store charges, not what a browser saw
    //     in March;
    //   • the subtotal is built from today's price;
    //   • and they are TOLD (PriceChanged), rather than finding out at checkout.
    // A cart that charged the snapshot would be a cart honouring an expired sale forever. A cart that
    // re-priced silently would be a cart that changed the number while the shopper wasn't looking.
    // Neither is honest; this is.
    [Fact]
    public async Task A_price_that_changed_since_the_add_is_surfaced_not_swallowed()
    {
        // The sale is running when they add…
        _campaigns.Add(CatalogFixtures.CampaignOn(_skillet.Id, Discount.PercentOff(20m), -60, 60));
        await AddCartLine(null).ExecuteAsync(_skillet.Id, 2, null, CancellationToken.None);

        // …and over by the time they look at the drawer.
        _clock.Advance(TimeSpan.FromMinutes(120));

        PricedCart cart = await GetCart(null).ExecuteAsync("token-1", CancellationToken.None);

        PricedCartLine line = Assert.Single(cart.Lines);
        Assert.Equal(Money.Of(27.60m, CurrencyCode.Usd), line.Line.PriceAtAddition);
        Assert.Equal(Money.Of(34.50m, CurrencyCode.Usd), line.UnitPrice);
        Assert.True(line.PriceChanged);
        Assert.Equal(Money.Of(69.00m, CurrencyCode.Usd), cart.Subtotal);
    }

    // 🔗 the subtotal is Money.Add folded over the line totals — the first place S04's currency gate
    // is load-bearing on a number a shopper is about to be charged.
    [Fact]
    public async Task The_subtotal_is_the_sum_of_the_line_totals_at_todays_prices()
    {
        await AddCartLine(null).ExecuteAsync(_skillet.Id, 2, null, CancellationToken.None);
        CartMutation result = await AddCartLine(null).ExecuteAsync(
            _board.Id, 3, "token-1", CancellationToken.None);

        // 2 × 34.50 + 3 × 19.99 = 69.00 + 59.97
        Assert.Equal(Money.Of(128.97m, CurrencyCode.Usd), result.Cart.Subtotal);
    }

    // ── Refusals ─────────────────────────────────────────────────────────────────────────────

    // ⚠️ 404 and not 422: an archived product reads as not-found to a shopper everywhere else (S03),
    // so it reads as not-found here. Same product, same shopper, same question, same answer.
    [Fact]
    public async Task An_archived_product_cannot_be_added_and_reads_as_not_found()
    {
        _skillet.Archive(_clock.GetUtcNow());

        await Assert.ThrowsAsync<NotFoundException>(
            () => AddCartLine(null).ExecuteAsync(_skillet.Id, 1, null, CancellationToken.None));

        // …and the refusal left nothing behind: no cart, no token, no commit.
        Assert.Empty(_carts.Rows);
        Assert.Equal(0, _unitOfWork.Commits);
    }

    [Fact]
    public async Task A_product_that_does_not_exist_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => AddCartLine(null).ExecuteAsync(Guid.NewGuid(), 1, null, CancellationToken.None));
    }

    [Fact]
    public async Task A_quantity_below_one_is_a_domain_rule_violation()
    {
        await Assert.ThrowsAsync<DomainRuleException>(
            () => AddCartLine(null).ExecuteAsync(_skillet.Id, 0, null, CancellationToken.None));
    }

    // ── Editing ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Setting_a_quantity_re_prices_the_whole_cart_in_the_response()
    {
        CartMutation added = await AddCartLine(null).ExecuteAsync(
            _skillet.Id, 1, null, CancellationToken.None);
        Guid lineId = added.Cart.Lines[0].Line.Id;

        CartMutation result = await UpdateCartLineQuantity(null)
            .ExecuteAsync(lineId, 3, "token-1", CancellationToken.None);

        Assert.Equal(3, result.Cart.Lines[0].Line.Quantity);
        Assert.Equal(Money.Of(103.50m, CurrencyCode.Usd), result.Cart.Subtotal);
    }

    // 🔍 THE IDOR THAT ISN'T. A line id from a stranger's cart is a 404 — not a 403, which would
    // confirm it exists and turn id-guessing into an oracle. Note there is no ownership `if` anywhere
    // in the use case: the line simply is not in the cart the caller's identity found.
    [Fact]
    public async Task Another_shoppers_line_id_is_not_found_rather_than_forbidden()
    {
        CartMutation strangers = await AddCartLine(null).ExecuteAsync(
            _skillet.Id, 1, null, CancellationToken.None);
        Guid strangersLine = strangers.Cart.Lines[0].Line.Id;

        // A different shopper, with their own cart, presenting the stranger's line id.
        Guid ada = Guid.NewGuid();
        await AddCartLine(ada).ExecuteAsync(_board.Id, 1, null, CancellationToken.None);

        await Assert.ThrowsAsync<NotFoundException>(
            () => UpdateCartLineQuantity(ada).ExecuteAsync(strangersLine, 9, null, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(
            () => RemoveCartLine(ada).ExecuteAsync(strangersLine, null, CancellationToken.None));

        // The stranger's line is untouched.
        Assert.Equal(1, _carts.Rows.Single(cart => cart.TokenHash == "tokenhash-1").Lines[0].Quantity);
    }

    [Fact]
    public async Task A_line_id_with_no_cart_at_all_is_also_just_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => RemoveCartLine(null).ExecuteAsync(Guid.NewGuid(), null, CancellationToken.None));
    }

    [Fact]
    public async Task Removing_the_last_line_leaves_an_empty_cart_and_a_zero_subtotal()
    {
        CartMutation added = await AddCartLine(null).ExecuteAsync(
            _skillet.Id, 1, null, CancellationToken.None);

        CartMutation result = await RemoveCartLine(null)
            .ExecuteAsync(added.Cart.Lines[0].Line.Id, "token-1", CancellationToken.None);

        Assert.Empty(result.Cart.Lines);
        Assert.Equal(Money.Of(0m, CurrencyCode.Usd), result.Cart.Subtotal);
    }

    [Fact]
    public async Task Clearing_empties_the_cart()
    {
        await AddCartLine(null).ExecuteAsync(_skillet.Id, 2, null, CancellationToken.None);
        await AddCartLine(null).ExecuteAsync(_board.Id, 1, "token-1", CancellationToken.None);

        CartMutation result = await ClearCart(null).ExecuteAsync("token-1", CancellationToken.None);

        Assert.Empty(result.Cart.Lines);
        Assert.Empty(_carts.Rows.Single().Lines);
    }

    // ⚠️ DELETE is idempotent: clearing a cart that was never created is a success, not a 404. The
    // postcondition the caller asked for holds.
    [Fact]
    public async Task Clearing_a_cart_that_does_not_exist_succeeds_and_writes_nothing()
    {
        CartMutation result = await ClearCart(null).ExecuteAsync(null, CancellationToken.None);

        Assert.Empty(result.Cart.Lines);
        Assert.Equal(0, _unitOfWork.Commits);
    }

    // ── Events ───────────────────────────────────────────────────────────────────────────────

    // 📘 the fact, and its fields. Note the test asserts the LITERAL event name rather than a
    // constant: an event name is a contract — S10 puts it in a webhook body that somebody else's code
    // switches on — so a test that read the constant would sail through a rename that broke every
    // subscriber. Literals in tests are usually a smell; for a wire contract they are the assertion.
    [Fact]
    public async Task Adding_a_product_publishes_the_fact_with_both_the_delta_and_the_new_total()
    {
        await AddCartLine(null).ExecuteAsync(_skillet.Id, 2, null, CancellationToken.None);
        await AddCartLine(null).ExecuteAsync(_skillet.Id, 3, "token-1", CancellationToken.None);

        Assert.Equal(2, _events.Published.Count);
        AgoraEvent second = _events.Published[1];

        Assert.Equal("cart.product_added", second.Name);
        Assert.Equal(_skillet.Id.ToString(), second.Data["productId"]);
        // What they asked for…
        Assert.Equal("3", second.Data["quantity"]);
        // …and what they now have. Neither number recovers the other.
        Assert.Equal("5", second.Data["lineQuantity"]);
        Assert.Equal("34.50", second.Data["unitPrice"]);
        Assert.Equal("USD", second.Data["currency"]);
    }

    // ── Builders ─────────────────────────────────────────────────────────────────────────────

    private CartLookup Lookup(Guid? userId) =>
        new(_carts, Caller(userId), _tokens, _clock);

    private CartPricing Pricing() =>
        new(new InMemoryProductRepository(_products, [_kitchen]),
            new ProductPricing(new InMemorySaleCampaignRepository(_campaigns), _clock));

    private static StubCurrentUser Caller(Guid? userId) =>
        userId is { } id ? StubCurrentUser.SignedIn(Role.Customer, id) : StubCurrentUser.Anonymous();

    private GetCart GetCart(Guid? userId) => new(Lookup(userId), Pricing());

    private AddCartLine AddCartLine(Guid? userId) =>
        new(new InMemoryProductRepository(_products, [_kitchen]),
            Lookup(userId), Pricing(), _events, _unitOfWork);

    private UpdateCartLineQuantity UpdateCartLineQuantity(Guid? userId) =>
        new(Lookup(userId), Pricing(), _unitOfWork);

    private RemoveCartLine RemoveCartLine(Guid? userId) =>
        new(Lookup(userId), Pricing(), _unitOfWork);

    private ClearCart ClearCart(Guid? userId) =>
        new(Lookup(userId), _unitOfWork, _clock);
}
