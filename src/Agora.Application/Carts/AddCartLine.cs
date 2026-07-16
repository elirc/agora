using Agora.Application.Abstractions;
using Agora.Application.Errors;
using Agora.Application.Events;
using Agora.Domain.Carts;
using Agora.Domain.Catalog;

namespace Agora.Application.Carts;

/// <summary>Put a product in the caller's cart, creating the cart (and its token) if there isn't one.</summary>
public sealed class AddCartLine(
    IProductRepository products,
    CartLookup cartLookup,
    CartPricing cartPricing,
    IEventSink events,
    IUnitOfWork unitOfWork)
{
    public async Task<CartMutation> ExecuteAsync(
        Guid productId,
        int quantity,
        string? cartToken,
        CancellationToken cancellationToken)
    {
        Product? product = await products.FindByIdAsync(productId, cancellationToken);

        // ⚠️ AN ARCHIVED PRODUCT IS A 404 HERE, NOT A 422, and the reasoning is worth more than the
        // status code. S03 decided that an archived product "reads as not-found to a shopper while
        // still existing for the admin" — GET /products/{slug} on one is a 404. So this must be a 404
        // too: same product, same shopper, same question ("can I have this?"), and an API that
        // answers it two different ways depending on which route you took is an API with two
        // opinions. A 422 would also leak — "this exists, but no" confirms the product is real to
        // anybody willing to guess ids, which is exactly the courtesy we decline to extend on every
        // other storefront read.
        if (product is null || product.IsArchived)
        {
            throw new NotFoundException("product", productId);
        }

        OpenedCart opened = await cartLookup.OpenAsync(cartToken, cancellationToken);
        Cart cart = opened.Cart;

        // Resolve first, mutate second, compose third — one clock read, one campaigns query, and the
        // snapshot, the timestamps and the response all speak about the same instant. See CartPricing.
        CartPrices prices = await cartPricing.ResolveAsync(cart, [product.Id], cancellationToken);

        CartLine line = cart.AddOrIncrease(
            product.Id,
            quantity,
            prices.PriceFor(product.Id).Amount,
            prices.AsOf);

        // The fact, announced. `quantity` is what the shopper asked for; the line's own quantity is
        // what they have now — CartEvents explains why a subscriber needs both.
        await events.PublishAsync(
            CartEvents.ProductAdded(cart, line, quantity, prices.AsOf),
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);

        return new CartMutation(CartPricing.Compose(cart, prices), opened.IssuedToken);
    }
}
