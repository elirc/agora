namespace Agora.Application.Carts;

/// <summary>The cart after a write, plus a cart token to hand the browser if one had to be minted.</summary>
// WHY every cart write answers with the WHOLE cart rather than the thing it changed: the drawer needs
// the subtotal, and the subtotal is money the client is not allowed to compute (S04). Answer with
// just the line and the client has two choices — a second round trip to learn what it costs, or
// `lines.reduce(...)` in JavaScript, which is the bug this whole course keeps warning about. So the
// authoritative cart IS the response, every time, and the drawer never has to guess or ask twice.
public sealed record CartMutation(PricedCart Cart, string? IssuedToken);
