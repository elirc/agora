using Agora.Application.Abstractions;
using Agora.Application.Catalog;
using Agora.Application.Pricing;
using Agora.Domain.Carts;
using Agora.Domain.Catalog;
using Agora.Domain.Pricing;
using Agora.Domain.Values;

namespace Agora.Application.Carts;

/// <summary>
/// Every product a cart operation touches, priced as of ONE instant — resolved before the write and
/// spent afterwards.
/// </summary>
public sealed class CartPrices
{
    private readonly Dictionary<Guid, PricedProduct> _byProduct;

    internal CartPrices(IReadOnlyList<PricedProduct> priced)
    {
        _byProduct = priced.ToDictionary(item => item.Product.Id);
        AsOf = priced.Count > 0 ? priced[0].AsOf : default;
    }

    /// <summary>The single instant every price in here is true for.</summary>
    // GOTCHA: `default` when nothing was resolved (an empty cart adding nothing — a shape that only
    // exists in a test). Every caller that uses AsOf has resolved at least one product to get here,
    // because you cannot add a line without a product. If that ever stops being true, this field
    // starts lying quietly, so: it is read only on the paths that resolved something.
    public DateTimeOffset AsOf { get; }

    public Product ProductFor(Guid productId) => Resolved(productId).Product;

    public EffectivePrice PriceFor(Guid productId) => Resolved(productId).Price;

    // WHY this throws rather than returning null: every id in here came from a cart line, and a cart
    // line's ProductId is a foreign key the database enforces (cart_lines → products, RESTRICT). So a
    // miss is not a case to handle — it is a corrupt database or a bug in the caller, and a 500 that
    // names the product is the honest answer. Handling it would mean inventing a price for a product
    // that does not exist.
    private PricedProduct Resolved(Guid productId) =>
        _byProduct.TryGetValue(productId, out PricedProduct? priced)
            ? priced
            : throw new InvalidOperationException($"No price was resolved for product '{productId}'.");
}

/// <summary>
/// The cart's price-resolution query: fetch the products a cart involves and price them all at one
/// instant, then compose the answer the API returns.
/// </summary>
// 📘 RESOLVE → MUTATE → COMPOSE, and the split is the whole idea. Look at the two members: one is
// async and does the IO; the other is static, pure, and does the arithmetic. A use case calls
// ResolveAsync FIRST, mutates the cart, then Composes the response out of the prices it already has.
//
// Which means a request that adds a line contains exactly ONE clock read and ONE campaigns query —
// and the price stamped on the new line, the timestamp on the cart, the instant on the event, and
// every price in the response are all THE SAME MOMENT. Not "microseconds apart, which is fine". The
// same one.
//
// The tempting shape is the obvious one: mutate the cart, then price it for the response, and take
// the snapshot from a second little resolve on the way past. It even reads better top-to-bottom. It
// also gives one HTTP request three "now"s, and if you write that down honestly you end up writing a
// comment about how the gap between them is too small to matter — which is the exact comment S04
// deleted from the catalog when it hoisted the clock read out of the per-product loop. If the answer
// is "the gap is too small to matter", the gap should not be there.
// 🔗 ProductPricing (S04) makes the same argument one layer down and is where the clock is actually
// read. This class does not read a clock at all; it borrows S04's, once.
public sealed class CartPricing(IProductRepository products, ProductPricing productPricing)
{
    /// <summary>Agora's one currency. Named, so the compiler can find every assumption about it.</summary>
    // GOTCHA: an empty cart's subtotal is zero — but zero WHAT? A Money cannot exist without a
    // currency and an empty cart has no line to borrow one from, so somebody has to say "USD" out
    // loud. This is the second hardcoded currency assumption in Agora (CurrencyCode.MinorUnits is the
    // first, S04) and, like that one, it lives behind a name rather than inline — the day a second
    // currency ships, the store's currency becomes a setting, and these two members are the whole
    // list of places that have to learn about it. An unnamed assumption is a grep; a named one is a
    // change.
    internal static CurrencyCode StoreCurrency => CurrencyCode.Usd;

    /// <summary>
    /// Resolve prices for every product in <paramref name="cart"/>, plus <paramref name="alsoInclude"/>
    /// — the products a write is about to add.
    /// </summary>
    public async Task<CartPrices> ResolveAsync(
        Cart? cart,
        IReadOnlyList<Guid> alsoInclude,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(alsoInclude);

        Guid[] ids =
        [
            .. (cart?.Lines.Select(line => line.ProductId) ?? [])
                .Concat(alsoInclude)
                .Distinct(),
        ];

        if (ids.Length == 0)
        {
            return new CartPrices([]);
        }

        // ⚠️ THE CART ASKS FOR `All`, ARCHIVED INCLUDED, and that is a decision rather than laziness.
        // A product can be archived while it sits in somebody's cart — S03 made archiving a state and
        // not a delete precisely so the row survives — and a cart that silently dropped the line
        // would be a cart that lost something the shopper put there, for a reason nobody told them.
        // So the line stays: priced, visible, removable.
        //
        // What the store must not do is SELL it, and the place to refuse that is checkout (S07), where
        // the money is. Add-to-cart already refuses (404 — the storefront has no such product, which
        // is the same answer /products/{slug} gives). And the merge does not refuse at all, because a
        // merge cannot say no to a shopper who is trying to log in.
        // Three questions — may I ADD it, may I HOLD it, may I BUY it — three different right
        // answers. Conflating them gives you a cart that either lies or loses things.
        IReadOnlyList<Product> found =
            await products.ListByIdsAsync(ids, ProductStatusFilter.All, cancellationToken);

        return new CartPrices(await productPricing.PriceAsync(found, cancellationToken));
    }

    /// <summary>Fold a cart and its resolved prices into the answer the API returns. Pure.</summary>
    public static PricedCart Compose(Cart? cart, CartPrices prices)
    {
        ArgumentNullException.ThrowIfNull(prices);

        if (cart is null || cart.Lines.Count == 0)
        {
            return PricedCart.Empty;
        }

        // WHY the order is pinned and not "whatever the database returned": a drawer whose rows
        // rearrange themselves between two renders is a drawer nobody trusts, and EF makes no promise
        // about collection order without an ORDER BY. Oldest first (it is the order the shopper built
        // it in), ties broken by id so the same cart renders identically on every device, forever.
        IReadOnlyList<PricedCartLine> lines =
        [
            .. cart.Lines
                .OrderBy(line => line.AddedAtUtc)
                .ThenBy(line => line.Id)
                .Select(line => new PricedCartLine(
                    line,
                    prices.ProductFor(line.ProductId),
                    prices.PriceFor(line.ProductId))),
        ];

        return new PricedCart(lines, Subtotal(lines));
    }

    // 📘 THE FOLD IS THE GUARD, and this is the first place S04's Money is genuinely load-bearing
    // rather than decorative.
    //
    // `Aggregate(Add)` looks like a fancy way to write a for-loop over decimals. It is not: every step
    // goes through Money's currency gate, so a cart that somehow held a USD line and a EUR line does
    // not quietly produce a plausible 40 — it throws, at the line that combined them, naming both
    // currencies. `lines.Sum(l => l.Amount)` over decimals would have added twenty dollars to twenty
    // euros and shipped the answer. That refusal is what the whole S04 struct was for, and here it is
    // doing a job, in production, on the number a shopper is about to be charged.
    //
    // ⚠️ AND NOW THE HONEST BIT, because S04 made a prediction about this exact method and got it
    // wrong. ADR-0006 shipped `Allocate`/`Distribute` with no caller and said "S05's cart is the
    // first caller". S05 is here. It isn't. The cart MULTIPLIES (price × quantity) and ADDS (line
    // totals into a subtotal); it has nothing to SPLIT, because splitting is what you do to a
    // cart-level discount across lines, and cart-level discounts are S11's promotion engine. The
    // first real caller for allocation is S07's order lines, or S11 — one sprint later than the ADR
    // guessed, at least.
    //
    // Which is worth more than the two lines it costs to admit. The Reviewer's objection in the S04
    // debate was literally "Allocate has no caller — YAGNI with a straight face", and they conceded
    // on a DIFFERENT argument: that allocation is the part of Money nobody can review by eye, so it
    // is the part that must be proven by property test. That argument is still exactly as true today,
    // with the caller still missing. The concession was right and the prediction was wrong, and
    // noticing that they were separate claims is the actual skill. Predictions about which sprint
    // will need what are cheap; reasons are not.
    private static Money Subtotal(IReadOnlyList<PricedCartLine> lines) =>
        lines.Count == 0
            ? Money.Zero(StoreCurrency)
            : lines.Select(line => line.LineTotal).Aggregate(static (running, next) => running.Add(next));
}
