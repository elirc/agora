import { useMutation, useQueryClient } from '@tanstack/react-query';
import {
  addCartLine,
  clearCart,
  removeCartLine,
  setCartLineQuantity,
  type CartDto,
} from '../../api/client';
import { cartKey } from './queries';

// 🔗 EVERY MUTATION HERE ENDS THE SAME WAY, and the shape is the lesson: seed the cache with the
// server's answer (onSuccess), then invalidate anyway (onSettled).
//
// `setQueryData(response)` is not an optimisation for its own sake — it is the drawer TRUSTING THE
// MUTATION'S RESPONSE. Every cart write answers with the whole authoritative cart, subtotal and all,
// so there is nothing left to ask about and no reason to guess. The invalidate on settle is the belt:
// it converges after an error, after a rollback, and after another tab wrote to the same cart, and it
// runs in the background while the UI already shows the right numbers.
function useCartWrites() {
  const queryClient = useQueryClient();

  return {
    onSuccess: (cart: CartDto) => queryClient.setQueryData(cartKey, cart),
    onSettled: () => queryClient.invalidateQueries({ queryKey: cartKey }),
  };
}

export function useAddToCart() {
  return useMutation({
    mutationFn: ({ productId, quantity }: { productId: string; quantity: number }) =>
      addCartLine(productId, quantity),
    ...useCartWrites(),
  });
}

/**
 * The stepper's write — the one optimistic mutation in the app.
 */
// ⚠️ READ WHAT THE OPTIMISTIC PATCH TOUCHES, AND WHAT IT LEAVES ALONE. It sets `quantity`. It does
// NOT touch `lineTotalAmount` or `subtotalAmount`, and that restraint is the whole teaching point:
//
//   • The quantity is honest to guess, because the client is not guessing. It AUTHORED that number —
//     the shopper clicked +, the value is 3, and there is no world in which the server disagrees.
//   • The totals are NOT ours to guess. Computing them means `unitPrice * quantity` in JavaScript,
//     which is a double, which is the exact bug this course has been circling since S04. Being
//     optimistic about money means inventing a number and showing it to a shopper as though the shop
//     had agreed to it.
//
// So the stepper responds instantly and the subtotal lags by one round trip, marked busy in the UI
// rather than silently stale. That is not a compromise — it is the honest rendering of what we know:
// we know what they asked for; we do not know what it costs until the store says so.
//
// The rollback matters for the same reason. A 422 (quantity > 99) must put the stepper back where it
// was, or the UI is showing a quantity the cart does not have and the shopper is the last to know.
export function useSetCartLineQuantity() {
  const queryClient = useQueryClient();
  const writes = useCartWrites();

  return useMutation({
    mutationFn: ({ lineId, quantity }: { lineId: string; quantity: number }) =>
      setCartLineQuantity(lineId, quantity),

    onMutate: async ({ lineId, quantity }: { lineId: string; quantity: number }) => {
      // Cancel in-flight reads first, or a refetch that started before this click can land after it
      // and clobber the optimistic value with a pre-click cart.
      await queryClient.cancelQueries({ queryKey: cartKey });

      const previous = queryClient.getQueryData<CartDto>(cartKey);

      queryClient.setQueryData<CartDto>(cartKey, (cart) =>
        cart === undefined
          ? cart
          : {
              ...cart,
              lines: cart.lines.map((line) =>
                line.id === lineId ? { ...line, quantity } : line,
              ),
            },
      );

      return { previous };
    },

    onError: (_error, _variables, context) => {
      if (context?.previous) {
        queryClient.setQueryData(cartKey, context.previous);
      }
    },

    ...writes,
  });
}

// WHY remove and clear are NOT optimistic, though dropping a row from an array is easy: the subtotal
// would be wrong the instant the row vanished, and a cart showing items that no longer add up to its
// total is worse than one that takes 80ms to catch up. The optimistic case earns its complexity when
// the client already knows the answer. Here it doesn't.
export function useRemoveCartLine() {
  return useMutation({
    mutationFn: (lineId: string) => removeCartLine(lineId),
    ...useCartWrites(),
  });
}

export function useClearCart() {
  return useMutation({
    mutationFn: () => clearCart(),
    ...useCartWrites(),
  });
}
