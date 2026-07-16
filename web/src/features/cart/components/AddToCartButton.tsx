import type { ProductDto } from '../../../api/client';
import { useAddToCart } from '../mutations';

/**
 * Add one of a product to the cart. Used by the grid card and the product page.
 */
// 📘 THE MOST IMPORTANT THING THIS COMPONENT DOES IS NOT SEND A PRICE. It sends a product id and a
// quantity, and that is the entire request. What the thing costs is resolved on the server, at the
// instant of the add, from the same pricing pipeline the page you are looking at used.
//
// ⚠️ the alternative is the classic and it looks so reasonable: the card already HAS
// `effectivePriceAmount`, so why not post it and save a lookup? Because then the price of a thing is
// whatever the client says it is — and the client is a browser, on somebody else's computer, running
// code anyone can edit. `{ "productId": "…", "price": 0.01 }` is a valid request to a server that
// believes the client about money. Never accept a price from a client; not as a hint, not as a
// check, not "just for the receipt".
export function AddToCartButton({ product }: { product: ProductDto }) {
  const add = useAddToCart();

  return (
    <button
      type="button"
      onClick={() => add.mutate({ productId: product.id, quantity: 1 })}
      disabled={add.isPending}
      className="w-full rounded-md bg-neutral-900 px-3 py-2 text-sm font-medium text-white hover:bg-neutral-700 disabled:opacity-50"
    >
      {/* WHY the button reports success rather than the drawer flying open: the shopper is browsing.
          Yanking a panel over the catalog interrupts the thing they are actually doing, and the
          header badge (which just went up) already told them it worked. The drawer opens when they
          ask for it. */}
      {add.isSuccess ? 'Added ✓' : 'Add to cart'}
    </button>
  );
}
