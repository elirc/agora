using Agora.Application.Abstractions;
using Agora.Application.Errors;
using Agora.Domain.Carts;

namespace Agora.Application.Carts;

/// <summary>Take a product out of the caller's cart.</summary>
public sealed class RemoveCartLine(
    CartLookup cartLookup,
    CartPricing cartPricing,
    IUnitOfWork unitOfWork)
{
    public async Task<CartMutation> ExecuteAsync(
        Guid lineId,
        string? cartToken,
        CancellationToken cancellationToken)
    {
        Cart? cart = await cartLookup.FindAsync(cartToken, cancellationToken);

        // Same 404-for-everything rule as the quantity write — see UpdateCartLineQuantity for why a
        // stranger's line id must not be distinguishable from a typo.
        if (cart?.LineOrDefault(lineId) is null)
        {
            throw new NotFoundException("cart line", lineId);
        }

        // Resolve BEFORE the removal, deliberately: the prices are then a superset of what the
        // response needs (it includes the product we are about to drop, harmlessly), and the instant
        // that stamps the cart is the same instant the surviving lines are priced at. Resolving after
        // would be one fewer product and one more clock read.
        CartPrices prices = await cartPricing.ResolveAsync(cart, [], cancellationToken);

        cart.RemoveLine(lineId, prices.AsOf);

        await unitOfWork.CommitAsync(cancellationToken);

        return new CartMutation(CartPricing.Compose(cart, prices), null);
    }
}
