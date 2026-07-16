using Agora.Domain.Values;

namespace Agora.Domain.Carts;

/// <summary>
/// One product in a cart, and how many of it. Lines are created and destroyed only by their
/// <see cref="Cart"/> — there is no way to hold a line that belongs to no cart.
/// </summary>
public sealed class CartLine
{
    /// <summary>The fewest of a thing a line can be for. Zero is not a quantity — it is a removal.</summary>
    public const int MinQuantity = 1;

    /// <summary>The most of one product a single line may hold.</summary>
    // WHY a cap at all, and why 99: a cap is what stops a fat finger (or a script) from putting
    // 2,000,000 skillets in a cart and making every subtotal, every inventory check (S06) and every
    // payment authorisation (S09) downstream deal with a number nobody meant. 99 because it is the
    // largest quantity a two-digit stepper can express and comfortably more than any real shopper
    // buys from a shop this size — a bulk order is a conversation, not a cart. The number is
    // arbitrary; HAVING one is not, and this is the one place it is written down.
    public const int MaxQuantity = 99;

    public Guid Id { get; }
    public Guid CartId { get; }
    public Guid ProductId { get; }
    public int Quantity { get; private set; }

    /// <summary>What this product was quoted at when it went into the cart.</summary>
    // 📘 A FACT ABOUT THE PAST, AND NOTHING ELSE. Read what this property is NOT used for: no
    // subtotal, no line total, no charge. Not one arithmetic operation in Agora reads it.
    //
    // The temptation is enormous and it is the classic cart bug. A snapshot looks like a price, so
    // it gets used like one — and then a shopper who added a skillet during last month's sale still
    // pays the sale price today, because a `PriceAtAddition` sitting on a line is indistinguishable
    // from a price in force unless somebody remembered the difference. A cart is a list of INTENTIONS
    // ("I want this"), and what the store charges for a thing is decided by the store, at the moment
    // of the transaction, not by whatever the shopper's browser saw in March.
    //
    // So why keep it at all? Because "the price changed since you put this in your cart" is a fact
    // the shopper is entitled to, and you cannot state it without remembering what you quoted. The
    // snapshot's entire job is to be COMPARED against the live price and reported. It is evidence,
    // not input. (ADR-0007, Decision 2.)
    // 🔗 S07's order line is where a snapshot becomes authoritative — because an order IS the
    // transaction, and what was sold at what price stops being a question the moment money moves.
    // Same shape, opposite job. Notice the difference when you get there.
    public Money PriceAtAddition { get; }

    /// <summary>When this product entered the cart — the instant <see cref="PriceAtAddition"/> is true for.</summary>
    // WHY it travels with the line and does not reset when the quantity changes: the fact is "this
    // product entered your cart THEN, quoted at THAT". Clicking + does not make the product arrive
    // again; it makes there be more of it. A snapshot that silently re-took itself on every stepper
    // click could never report a price change, because it would always agree with the live price.
    public DateTimeOffset AddedAtUtc { get; }

    private CartLine(
        Guid id,
        Guid cartId,
        Guid productId,
        int quantity,
        Money priceAtAddition,
        DateTimeOffset addedAtUtc)
    {
        Id = id;
        CartId = cartId;
        ProductId = productId;
        Quantity = quantity;
        PriceAtAddition = priceAtAddition;
        AddedAtUtc = addedAtUtc;
    }

    // WHY internal: a CartLine is meaningless outside the aggregate that owns it, so the only door
    // is Cart.AddOrIncrease / Cart.MergeFrom. There is no public constructor to reach past the
    // quantity rules with, and no orphan line can exist to be handed around.
    internal static CartLine Create(
        Guid cartId,
        Guid productId,
        int quantity,
        Money priceAtAddition,
        DateTimeOffset addedAtUtc) =>
        new(
            Guid.NewGuid(),
            cartId,
            productId,
            ValidQuantity(quantity),
            ValidPrice(priceAtAddition),
            addedAtUtc);

    internal void ChangeQuantity(int quantity) => Quantity = ValidQuantity(quantity);

    /// <summary>What this line costs at <paramref name="unitPrice"/> — the price in force, never the snapshot.</summary>
    // 📘 THE SIGNATURE IS THE ARGUMENT. A line cannot tell you what it costs, because a line does not
    // know what its product costs — it knows what its product cost ONCE. So the price is a parameter,
    // supplied by whoever resolved it (Application/Carts/CartPricing, at one instant), exactly as
    // PriceResolver takes `asOf` rather than reading a clock. If this method could reach for
    // PriceAtAddition, someone would eventually let it, and the bug would be invisible: every number
    // on the page would look like a price, and one of them would be a memory.
    public Money TotalAt(Money unitPrice) => unitPrice.Multiply(Quantity);

    private static int ValidQuantity(int quantity)
    {
        if (quantity < MinQuantity)
        {
            throw new DomainRuleException($"Quantity must be at least {MinQuantity}.");
        }

        if (quantity > MaxQuantity)
        {
            throw new DomainRuleException($"Quantity cannot exceed {MaxQuantity} per line.");
        }

        return quantity;
    }

    // LEARN: the same split Product makes. Money in general MAY be negative (a refund is); a price a
    // shopper was QUOTED may not. The general invariant belongs to the type, the local one to the
    // thing it is local to — so this is four lines here rather than a rule Money imposes on refunds.
    private static Money ValidPrice(Money price) =>
        price.IsNegative
            ? throw new DomainRuleException("A cart line's price cannot be negative.")
            : price;

    // WHY: EF Core materialization only.
    private CartLine()
    {
    }
}
