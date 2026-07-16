using Agora.Domain.Carts;

namespace Agora.Application.Carts;

/// <summary>The caller's cart, priced as of now.</summary>
// 🔍 NOTICE WHAT THIS USE CASE DOES NOT DO, in order:
//   • It does not authorize. There is no RequireUser and no RequireRole, and this is the first
//     mutating-adjacent surface in Agora with neither. That is not an oversight — a cart is not
//     gated by WHO you are, it is scoped by WHICH cart you can name, and CartLookup already answered
//     that from your session or your token. A guard here would be theatre: there is no cart you could
//     ask for and be refused, because there is no way to ask for one that isn't yours.
//   • It does not create. A read that writes a row is a read with a side effect (see CartLookup).
//   • It does not 404. Every shopper has a cart; an empty one is a cart. (See PricedCart.Empty.)
//   • It does not commit. Nothing changed.
// The whole use case is "look it up, price it, hand it back" — and the shortness is the evidence that
// the ownership decision was made in the right place.
public sealed class GetCart(CartLookup cartLookup, CartPricing cartPricing)
{
    public async Task<PricedCart> ExecuteAsync(string? cartToken, CancellationToken cancellationToken)
    {
        Cart? cart = await cartLookup.FindAsync(cartToken, cancellationToken);

        if (cart is null)
        {
            return PricedCart.Empty;
        }

        // ⚠️ THE PRICES ARE RESOLVED LIVE, EVERY READ. A cart line remembers what it was quoted; it is
        // never charged that. Open a drawer a month after a sale ended and every number in it is
        // today's — because what a shop charges for a thing is a fact about the shop, not about what
        // the shopper's browser saw in March. (What the snapshot IS for: PricedCartLine.PriceChanged,
        // which is how the shopper hears about it rather than discovering it at checkout.)
        CartPrices prices = await cartPricing.ResolveAsync(cart, [], cancellationToken);

        return CartPricing.Compose(cart, prices);
    }
}
