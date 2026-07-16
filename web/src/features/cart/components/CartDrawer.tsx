import { formatPrice } from '../../catalog/format';
import { useClearCart } from '../mutations';
import { useCart } from '../queries';
import { CartLineRow } from './CartLineRow';

/**
 * The cart, in a panel that slides in from the right. Opened from the header.
 */
// 🔗 THE DRAWER TRUSTS THE MUTATION'S RESPONSE, NOT ITS OWN GUESS — and this component is where that
// promise is kept, by doing almost nothing. It renders `useCart()`. It does not hold a copy of the
// cart in useState, it does not add anything up, and it does not patch itself when a stepper is
// clicked: the mutation writes the server's answer into the cache (see mutations.ts) and this
// re-renders from it.
//
// That is the whole reason optimistic UI is safe here. The optimistic patch touches exactly one
// field the client authored (the quantity), and every number that involves money arrives resolved and
// is rendered as-is. A drawer that kept its own subtotal in state would be a second source of truth
// about what the shopper owes, and it would be right until the first sale ended mid-session.
export function CartDrawer({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { data: cart, isPending, isError } = useCart();
  const clear = useClearCart();

  if (!open) {
    return null;
  }

  return (
    <>
      {/* WHY a real <button> for the backdrop and not a <div onClick>: a div with a click handler is
          invisible to a keyboard and to a screen reader. This is the same "close" action as the ✕,
          so it gets the same affordances. */}
      <button
        type="button"
        aria-label="Close cart"
        onClick={onClose}
        className="fixed inset-0 z-40 cursor-default bg-neutral-900/20"
      />

      <aside
        aria-label="Shopping cart"
        className="fixed inset-y-0 right-0 z-50 flex w-full max-w-sm flex-col border-l border-neutral-200 bg-white shadow-xl"
      >
        <header className="flex items-center justify-between border-b border-neutral-200 px-4 py-3">
          <h2 className="text-lg font-semibold text-neutral-900">Your cart</h2>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close cart"
            className="text-neutral-500 hover:text-neutral-900"
          >
            ✕
          </button>
        </header>

        <div className="flex-1 overflow-y-auto px-4">
          {isPending && <p className="py-8 text-sm text-neutral-500">Loading…</p>}

          {isError && (
            <p role="alert" className="py-8 text-sm text-neutral-600">
              We couldn’t load your cart right now. Please try again.
            </p>
          )}

          {cart && cart.lines.length === 0 && (
            // The empty state is a state, not an error. A shopper who has never added anything has a
            // cart; it is empty. (There is no 404 from GET /cart for exactly this reason.)
            <p className="py-8 text-sm text-neutral-500">
              Your cart is empty. Browse the catalog and add something you like.
            </p>
          )}

          {cart && cart.lines.length > 0 && (
            <ul>
              {cart.lines.map((line) => (
                <CartLineRow key={line.id} line={line} />
              ))}
            </ul>
          )}
        </div>

        {cart && cart.lines.length > 0 && (
          <footer className="border-t border-neutral-200 px-4 py-4">
            <div className="flex items-baseline justify-between">
              <span className="text-sm text-neutral-600">Subtotal</span>
              {/* The server's number, rendered. Nothing in this app knows how to compute it. */}
              <span className="text-lg font-semibold tabular-nums text-neutral-900">
                {formatPrice(cart.subtotalAmount, cart.subtotalCurrency)}
              </span>
            </div>

            <p className="mt-1 text-xs text-neutral-500">
              Shipping and tax are worked out at checkout.
            </p>

            <button
              type="button"
              onClick={() => clear.mutate()}
              disabled={clear.isPending}
              className="mt-4 w-full rounded-md border border-neutral-300 py-2 text-sm text-neutral-700 hover:border-neutral-500 disabled:opacity-50"
            >
              Empty cart
            </button>
          </footer>
        )}
      </aside>
    </>
  );
}
