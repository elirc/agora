using Agora.Application.Abstractions;
using Agora.Application.Errors;
using Agora.Domain.Carts;

namespace Agora.Application.Carts;

/// <summary>Set one line to an exact quantity — the stepper's write.</summary>
public sealed class UpdateCartLineQuantity(
    CartLookup cartLookup,
    CartPricing cartPricing,
    IUnitOfWork unitOfWork)
{
    public async Task<CartMutation> ExecuteAsync(
        Guid lineId,
        int quantity,
        string? cartToken,
        CancellationToken cancellationToken)
    {
        Cart? cart = await cartLookup.FindAsync(cartToken, cancellationToken);

        // 🔍 THE IDOR THAT ISN'T, and it is worth naming because the shape is the one that bites.
        // `lineId` is a raw id straight off the URL — the classic ingredient. But the cart it is
        // looked up in was found by the CALLER's identity, so a line belonging to someone else is
        // simply not in the list, and this is a 404 without anybody writing an ownership check.
        //   • 404 and not 403, because 403 confirms the line exists. An id that yields "forbidden"
        //     for a stranger and "not found" for a typo is an oracle: iterate ids, read the statuses,
        //     learn the shape of other people's carts without ever seeing one.
        //   • 404 and not 401, because being signed in would not help — it isn't yours either way.
        // Same answer for "no cart at all", "wrong cart", and "no such line anywhere": there is
        // exactly one thing a stranger can learn from this endpoint, and it is nothing.
        if (cart?.LineOrDefault(lineId) is null)
        {
            throw new NotFoundException("cart line", lineId);
        }

        CartPrices prices = await cartPricing.ResolveAsync(cart, [], cancellationToken);

        // ⚠️ QUANTITY 0 IS A 422, NOT A DELETE. It is tempting to be helpful — "they set it to zero,
        // they obviously mean remove it" — and it is a trap: `parseInt(input.value) || 0` is one of
        // the most common lines in front-end code, so "0" is what an EMPTY BOX sends. Accept it as a
        // removal and a client bug silently deletes a shopper's line; refuse it and the same bug is a
        // 422 nobody's cart pays for. Two operations that mean different things get two verbs. DELETE
        // removes. (Cart.MinQuantity is where the rule lives; this is just where it lands.)
        cart.SetQuantity(lineId, quantity, prices.AsOf);

        await unitOfWork.CommitAsync(cancellationToken);

        return new CartMutation(CartPricing.Compose(cart, prices), null);
    }
}
