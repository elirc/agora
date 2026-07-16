using Agora.Application.Abstractions;
using Agora.Domain.Carts;

namespace Agora.Application.Carts;

/// <summary>Empty the caller's cart.</summary>
// 📘 THE ONE CART WRITE THAT NEEDS NO PRICES, and the absence is the lesson. Every other mutation
// takes a CartPricing because its response has to say what the cart costs; this one already knows.
// The answer to "what does an empty cart come to" does not require asking the catalog anything, so it
// doesn't ask — no products fetched, no campaigns queried, no clock read for a resolution nobody will
// use. Notice how the dependency list shrank on its own; a use case that needed a collaborator to
// return a constant would be a use case with a collaborator it didn't need.
public sealed class ClearCart(CartLookup cartLookup, IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task<CartMutation> ExecuteAsync(string? cartToken, CancellationToken cancellationToken)
    {
        Cart? cart = await cartLookup.FindAsync(cartToken, cancellationToken);

        // ⚠️ CLEARING A CART THAT IS ALREADY EMPTY IS A 200, NOT A 404. DELETE is idempotent — that
        // is not a style preference, it is what the verb MEANS, and the client relies on it: a
        // double-click, a retried request, a stale tab all have to be safe. "There was nothing to
        // delete" is a successful delete; the postcondition the caller asked for holds. A 404 here
        // would make the honest answer look like a failure and teach every client to swallow it.
        if (cart is null || cart.Lines.Count == 0)
        {
            return new CartMutation(PricedCart.Empty, null);
        }

        cart.Clear(clock.GetUtcNow());

        await unitOfWork.CommitAsync(cancellationToken);

        return new CartMutation(PricedCart.Empty, null);
    }
}
