namespace Agora.Contracts.V1;

/// <summary>A shopper's cart, as served by /api/v1/cart.</summary>
// 📘 READ THE FIELDS THAT ARE NOT HERE — they are the contract's argument.
//
// **No `id`.** A cart is not addressable. There is exactly one cart per caller, the server works out
// which from the cookies you already sent, and no route anywhere takes a cart id. Ship the id and the
// next honest-looking feature request is `GET /cart/{id}`, and then a cart id is a thing that exists
// in a URL, in a log, in a bug report someone pastes into Slack. What you never expose, you never
// have to defend.
//
// **No `itemCount`.** The header badge sums the quantities in the browser, and that is not a breach
// of "the client does not do maths". That rule is about MONEY: JavaScript has no decimal type, so
// 19.99 × 3 is a lie told in a double. It has exact integers to 2^53, and 2 + 1 is 3 in every
// language ever written. Send what the client CANNOT compute; let it compute what it can. A DTO that
// carries every derivable number is a DTO with more ways to disagree with itself — and the day
// `itemCount` and `lines` disagree, both of them are the server's fault.
//
// **No `isOnSale`.** ProductDto has one, because the catalog's job is persuasion and a badge is how
// you say "now is a good time". A cart's job is reconciliation: what will I be charged. Same data,
// different question, so a different shape. Copying the catalog's fields here because they were
// handy is how a DTO becomes a database view.
public sealed record CartDto(
    IReadOnlyList<CartLineDto> Lines,
    // What the lines come to, resolved server-side in decimal, right now. The client renders it.
    decimal SubtotalAmount,
    string SubtotalCurrency);

/// <summary>One product in the cart, priced as of this response.</summary>
public sealed record CartLineDto(
    Guid Id,
    Guid ProductId,
    string Name,
    string Slug,
    string Sku,
    int Quantity,
    // One currency for the whole line: the unit price, the line total and the snapshot are all in the
    // product's currency by construction — a line cannot be priced in two. (The catalog repeats the
    // currency per amount because its amounts arrive from different places; here they cannot.)
    string Currency,
    // What one costs RIGHT NOW — the effective price, sale applied, resolved this request.
    decimal UnitPriceAmount,
    // …and what the line comes to. Server-side, because quantity × price is money math.
    decimal LineTotalAmount,
    // ⚠️ WHAT WE QUOTED WHEN IT WENT IN THE CART, and it is not what you will be charged. It is here
    // so the shopper can be TOLD their price moved, which is a courtesy that requires remembering.
    // A client that ever renders this as the price — or, worse, totals it — has invented a discount.
    decimal PriceAtAdditionAmount,
    // True when the two disagree. Deliberately directionless: "cheaper" and "dearer" are the same
    // fact to a shopper ("this is not the number you last saw"), both amounts are right here to
    // compare, and a `priceWentUp` flag would be the server picking which news is bad.
    bool PriceChanged,
    DateTimeOffset AddedAtUtc);

/// <summary>Put a product in the cart.</summary>
// WHY productId and not sku or slug: the cart stores a product id, so a request that named a slug
// would make this endpoint resolve one — a second lookup, a second failure mode ("slug not found" vs
// "product not found"), and a route that quietly depends on a permalink that S03 promised would never
// move but that a future rename-plus-redirect would. The catalog hands the client an id in every
// ProductDto; this is what it is for.
public sealed record AddCartLineRequest(Guid ProductId, int Quantity);

/// <summary>Set a line to an exact quantity.</summary>
// WHY an exact quantity and not a delta (`{ "by": 1 }`): a delta is not idempotent. Retry it — a
// flaky network, a double click, a service worker replaying a queued request — and the shopper has
// two of something they asked for once. `{ "quantity": 3 }` says what the world should look like, so
// applying it twice is applying it once. The stepper knows the number it is asking for; making the
// client send a difference would be asking it to do arithmetic it does not need to do.
public sealed record UpdateCartLineQuantityRequest(int Quantity);
