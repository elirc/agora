using Agora.Application.Carts;
using Agora.Application.Events;
using Agora.Application.Tests.Fakes;
using Agora.Domain.Carts;
using Agora.Domain.Values;

namespace Agora.Application.Tests.Carts;

/// <summary>Signing in: the anonymous cart is folded into the customer's own and then retired.</summary>
public class MergeCartsTests
{
    private static readonly DateTimeOffset Now = CatalogFixtures.Base;

    private readonly InMemoryCartRepository _carts = new();
    private readonly RecordingEventSink _events = new();
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly SequentialSessionTokens _tokens = new();

    private readonly Guid _ada = Guid.NewGuid();
    private readonly Guid _skillet = Guid.NewGuid();
    private readonly Guid _board = Guid.NewGuid();

    private MergeCarts UseCase() =>
        new(_carts, _tokens, _events, _unitOfWork, new FixedClock(Now));

    private static Money Usd(decimal amount) => Money.Of(amount, CurrencyCode.Usd);

    // ── The happy path ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Signing_in_folds_the_anonymous_cart_into_the_customers_own_and_retires_it()
    {
        Cart owned = OwnedCart(_ada, (_board, 1));
        Cart anonymous = AnonymousCart("tokenhash-1", (_skillet, 2));

        await UseCase().ExecuteAsync(_ada, "token-1", CancellationToken.None);

        Assert.Equal(2, owned.Lines.Count);
        Assert.Equal(2, owned.Lines.Single(line => line.ProductId == _skillet).Quantity);
        Assert.True(anonymous.IsRetired);
        // LEARN: two carts changed — one commit. Either the shopper signs in and their cart came with
        // them, or neither happened.
        Assert.Equal(1, _unitOfWork.Commits);
    }

    // 📘 A CUSTOMER WITH NO CART YET goes down the SAME path — merged into a cart created on the spot,
    // not "claimed" by re-pointing the anonymous row. One code path means the boring case (this one,
    // most of the traffic) walks the same lines as the interesting one (a collision), so the boring
    // case tests it. See MergeCarts for the argument.
    [Fact]
    public async Task A_customer_who_has_never_had_a_cart_gets_one_holding_what_they_were_carrying()
    {
        AnonymousCart("tokenhash-1", (_skillet, 2), (_board, 1));

        await UseCase().ExecuteAsync(_ada, "token-1", CancellationToken.None);

        Cart owned = _carts.Rows.Single(cart => cart.UserId == _ada);
        Assert.Equal(2, owned.Lines.Count);
        Assert.Null(owned.TokenHash);
    }

    // ⚠️ THE COLLISION, at the use-case seam this time: 2 on the phone, 3 in the account → 3, not 5.
    // The domain owns the rule (Cart.MergeFrom / Reconcile); this pins that the rule is what actually
    // runs when somebody logs in.
    [Fact]
    public async Task The_same_product_in_both_carts_keeps_the_larger_quantity()
    {
        Cart owned = OwnedCart(_ada, (_skillet, 3));
        AnonymousCart("tokenhash-1", (_skillet, 2));

        await UseCase().ExecuteAsync(_ada, "token-1", CancellationToken.None);

        Assert.Equal(3, Assert.Single(owned.Lines).Quantity);
    }

    // ── Idempotence: the reason a retry is boring ────────────────────────────────────────────

    // 📘 TWO INDEPENDENT REASONS this is safe, and the test exercises the first one: a retired cart is
    // not findable, so the second merge has nothing to merge. The second reason is algebraic — `max`
    // is idempotent, so even if it DID find it, the answer would be the same (that one is pinned in
    // CartMergeTests). Belt and braces, and neither is a coincidence: it is why "just retry the
    // login" is a real answer to the two-transaction window in AuthEndpoints rather than a hopeful
    // one.
    [Fact]
    public async Task Merging_twice_with_the_same_token_does_nothing_the_second_time()
    {
        Cart owned = OwnedCart(_ada, (_skillet, 1));
        AnonymousCart("tokenhash-1", (_skillet, 2));

        await UseCase().ExecuteAsync(_ada, "token-1", CancellationToken.None);
        await UseCase().ExecuteAsync(_ada, "token-1", CancellationToken.None);

        Assert.Equal(2, Assert.Single(owned.Lines).Quantity);
        // One merge happened, so one commit and one fact. The second call found nothing and said so
        // by doing nothing at all.
        Assert.Equal(1, _unitOfWork.Commits);
        Assert.Single(_events.Published);
    }

    // ── Nothing to do ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task No_cart_token_means_nothing_happens(string? token)
    {
        await UseCase().ExecuteAsync(_ada, token, CancellationToken.None);

        Assert.Empty(_carts.Rows);
        Assert.Equal(0, _unitOfWork.Commits);
    }

    [Fact]
    public async Task A_token_with_no_cart_behind_it_is_not_an_error()
    {
        await UseCase().ExecuteAsync(_ada, "token-99", CancellationToken.None);

        Assert.Equal(0, _unitOfWork.Commits);
    }

    // WHY an empty anonymous cart is not even retired: it would be a whole transaction to tidy a row
    // whose holder loses the cookie in this very response anyway, and which the reaper collects for
    // free. A no-op should cost a read.
    [Fact]
    public async Task An_empty_anonymous_cart_is_left_alone_rather_than_merged_or_retired()
    {
        Cart anonymous = AnonymousCart("tokenhash-1");

        await UseCase().ExecuteAsync(_ada, "token-1", CancellationToken.None);

        Assert.False(anonymous.IsRetired);
        Assert.Equal(0, _unitOfWork.Commits);
        Assert.Empty(_events.Published);
    }

    // ── The fact ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Merging_publishes_the_fact_naming_both_carts()
    {
        Cart owned = OwnedCart(_ada, (_board, 1));
        Cart anonymous = AnonymousCart("tokenhash-1", (_skillet, 2));

        await UseCase().ExecuteAsync(_ada, "token-1", CancellationToken.None);

        AgoraEvent merged = _events.Single("cart.merged");
        Assert.Equal(owned.Id.ToString(), merged.Data["cartId"]);
        // 🔗 the retired cart's id is the only thread back to what the shopper had before they signed
        // in — without it, the merge is a state change with no visible cause and support has nothing
        // to answer "why does my cart say 2, I added 3" with.
        Assert.Equal(anonymous.Id.ToString(), merged.Data["retiredCartId"]);
        Assert.Equal(_ada.ToString(), merged.Data["userId"]);
        Assert.Equal("2", merged.Data["lineCount"]);
        Assert.Equal(Now, merged.OccurredAtUtc);
    }

    // ── Builders ─────────────────────────────────────────────────────────────────────────────

    private Cart OwnedCart(Guid userId, params (Guid ProductId, int Quantity)[] lines) =>
        Fill(Cart.ForUser(userId, Now), lines);

    private Cart AnonymousCart(string tokenHash, params (Guid ProductId, int Quantity)[] lines) =>
        Fill(Cart.ForToken(tokenHash, Now), lines);

    private Cart Fill(Cart cart, (Guid ProductId, int Quantity)[] lines)
    {
        foreach ((Guid productId, int quantity) in lines)
        {
            cart.AddOrIncrease(productId, quantity, Usd(9.99m), Now);
        }

        _carts.Add(cart);
        return cart;
    }
}
