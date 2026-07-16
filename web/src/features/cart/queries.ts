import { useQuery } from '@tanstack/react-query';
import { getCart, type CartDto } from '../../api/client';
import { toInt } from '../catalog/format';

/** One cache key for the one cart. Exported so mutations and the auth hooks can invalidate it. */
// WHY a flat key and not ['cart', userId]: the client does not know whose cart it is, and must not
// have an opinion — the cookies decide, on the server. Keying by a user id here would mean this app
// held a second, guessable answer to "whose cart is this?", which is precisely the thing the two
// cookies exist to keep out of reach. When identity changes (login, register, logout), the auth
// mutations invalidate this key and the answer comes back from the only party who knows.
export const cartKey = ['cart'] as const;

export function useCart() {
  return useQuery({
    queryKey: cartKey,
    queryFn: () => getCart(),
  });
}

/**
 * How many things are in the cart — the header badge's number.
 */
// 📘 THE CLIENT DOES THIS SUM, AND THAT IS NOT A CONTRADICTION. The rule is "the client does not do
// MONEY math": JavaScript has one number type, an IEEE-754 double, so 19.99 × 3 is a lie it tells
// confidently. But it has exact integers up to 2^53, and 2 + 1 is 3 in every language ever written.
//
// So: send what the client cannot compute (every amount), and let it compute what it can. The
// alternative — an `itemCount` field on CartDto — would be a second copy of a number already in the
// response, and the day it disagreed with `lines`, the server would be wrong twice. Discriminate;
// don't cargo-cult.
export function cartItemCount(cart: CartDto | undefined): number {
  return (cart?.lines ?? []).reduce((total, line) => total + toInt(line.quantity), 0);
}
