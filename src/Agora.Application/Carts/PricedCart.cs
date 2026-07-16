using Agora.Domain.Carts;
using Agora.Domain.Catalog;
using Agora.Domain.Pricing;
using Agora.Domain.Values;

namespace Agora.Application.Carts;

/// <summary>One cart line, the product it points at, and what that product costs right now.</summary>
public sealed record PricedCartLine(CartLine Line, Product Product, EffectivePrice Price)
{
    /// <summary>What one of these costs right now — the price in force, never the snapshot.</summary>
    public Money UnitPrice => Price.Amount;

    /// <summary>What this line costs right now.</summary>
    public Money LineTotal => Line.TotalAt(Price.Amount);

    /// <summary>True when the price in force is not what we quoted the shopper when they added it.</summary>
    // 📘 THE WHOLE POINT OF KEEPING A SNAPSHOT, IN ONE LINE — and note the direction it is NOT
    // interested in. It does not say "cheaper" or "dearer", because both are the same fact to the
    // shopper ("this is not the number you last saw") and the two amounts are right there for them to
    // read. A `priceWentUp` flag would be the server deciding which news is bad, and would tempt the
    // client into a comparison of its own.
    //
    // ⚠️ AND THIS IS THE ONE COMPARISON PriceAtAddition IS FOR. Money's `!=` compares the amount AND
    // the currency, so it is a comparison this type can make honestly. What it must never become is a
    // *fallback*: `PriceChanged ? PriceAtAddition : Price.Amount` is how a cart starts charging last
    // month's sale price, and it would read as mercy to a reviewer skimming. The snapshot is evidence.
    // It is never input.
    public bool PriceChanged => Line.PriceAtAddition != Price.Amount;
}

/// <summary>A cart as the API answers it: its lines priced as of one instant, and what they add to.</summary>
public sealed record PricedCart(IReadOnlyList<PricedCartLine> Lines, Money Subtotal)
{
    /// <summary>The answer for a shopper who has never put anything in a cart.</summary>
    // 📘 A SHOPPER ALWAYS HAS A CART; sometimes it is empty. That is why there is no `POST /cart`, no
    // 404 from `GET /cart`, and no null anywhere in this file: "you do not have a cart yet" is a
    // distinction the database cares about and a shopper has never once needed. The empty cart is a
    // value, not an absence — so the endpoint has one shape, the drawer has one shape, and neither of
    // them has a branch for a state that isn't real.
    public static PricedCart Empty { get; } = new([], Money.Zero(CartPricing.StoreCurrency));
}
