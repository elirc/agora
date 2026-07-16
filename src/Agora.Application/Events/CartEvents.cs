using Agora.Domain.Carts;

namespace Agora.Application.Events;

/// <summary>The facts a cart emits, built in one place so their names and fields cannot drift.</summary>
// 📘 WHY ONLY TWO EVENTS, when there are five cart use cases. Because an event log that mirrors your
// mutations is not an event log, it is a change feed nobody reads — and every fact you publish is a
// promise you have to keep to whoever subscribed. `cart.line_quantity_set` would be a fact, and it
// would be true, and no one has ever wanted it: the shopper clicks the stepper four times and a
// subscriber gets four events describing a decision they made once.
//
// These two earn their place because something outside this module actually wants them. A cart coming
// alive is when an abandoned-cart mailer's clock starts. A merge is the one moment in a cart's life
// where support gets a ticket ("signing in changed my cart") and needs to see what the system thought
// it was doing. Emit facts someone asked for; the rest is logging.
internal static class CartEvents
{
    /// <summary>A shopper put some quantity of a product into a cart.</summary>
    // 📘 THE NAME IS `product_added`, NOT `line_added`, AND THAT IS NOT PEDANTRY. Adding a skillet you
    // already have adds no line — it makes there be more skillets. So `cart.line_added` would be a
    // lie roughly half the time it fired, and the subscriber who counted lines by counting those
    // events would be wrong in a way that reconciles perfectly right up until it doesn't. An event
    // name is a claim about what happened; it has to be true every time, not usually.
    // (The endpoint makes the same call in HTTP: POST /cart/lines answers 200, not 201, because half
    // the time it creates nothing. Same fact, same honesty, two vocabularies.)
    public static AgoraEvent ProductAdded(Cart cart, CartLine line, int added, DateTimeOffset occurredAtUtc) =>
        AgoraEvent.Of(
            "cart.product_added",
            occurredAtUtc,
            ("cartId", AgoraEvent.Text(cart.Id)),
            ("lineId", AgoraEvent.Text(line.Id)),
            ("productId", AgoraEvent.Text(line.ProductId)),
            // WHY both numbers: they answer different questions and neither recovers the other. "They
            // added 2" is what an analytics pipeline counts; "the line now holds 5" is what a demand
            // forecast reads. Send one and the other is unrecoverable — a delta cannot be derived
            // from a total, nor a total from a delta. Two facts, two fields, no arithmetic asked of
            // the reader.
            ("quantity", AgoraEvent.Text(added)),
            ("lineQuantity", AgoraEvent.Text(line.Quantity)),
            // The price we quoted, at the instant we quoted it — as an exact decimal string. Never a
            // float: this field's eventual reader is JavaScript (S10's webhook bodies), and a JSON
            // number is a double the moment it is parsed.
            ("unitPrice", AgoraEvent.Text(line.PriceAtAddition.Amount)),
            ("currency", line.PriceAtAddition.Currency.Code));

    /// <summary>An anonymous cart was folded into a customer's own cart when they signed in.</summary>
    public static AgoraEvent Merged(Cart owned, Cart retired, DateTimeOffset occurredAtUtc) =>
        AgoraEvent.Of(
            "cart.merged",
            occurredAtUtc,
            ("cartId", AgoraEvent.Text(owned.Id)),
            // WHY the retired cart's id is in the payload: it is the only thread back to what the
            // shopper had before they signed in. When support is asked "why does my cart say 2, I
            // added 3", this id plus the retired row is the entire answer, and without it the merge
            // is a state change with no visible cause.
            ("retiredCartId", AgoraEvent.Text(retired.Id)),
            ("userId", AgoraEvent.Text(owned.UserId ?? Guid.Empty)),
            ("lineCount", AgoraEvent.Text(owned.Lines.Count)));
}
