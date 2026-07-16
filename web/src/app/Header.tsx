import { Link, useNavigate } from 'react-router-dom';
import { useCurrentUser } from '../features/auth/queries';
import { useLogout } from '../features/auth/mutations';
import { cartItemCount, useCart } from '../features/cart/queries';

// 🔗 the header reflects auth state, but showing/hiding a link is UX only — the server is the
// authority (see RequireAuth). Anonymous shoppers see Sign in / Register; signed-in users see
// their email (a link to the account page) and Sign out. It reads `useCurrentUser`, the same
// cached user every other component reads, so login/logout updates the header with no prop-drilling.
//
// 📘 AND FROM S05 IT READS THE CART THE SAME WAY — which is what makes the merge VISIBLE. Sign in
// with two skillets in an anonymous cart and this badge changes on its own, because `useLogin`
// invalidates the cart key and this component is already subscribed to it. Nobody passes the new
// count anywhere; nobody tells the header that a merge happened. Server state in one cache, read by
// whoever needs it, is why a feature living in `features/auth` can correctly change a number in
// `app/Header` without either file importing the other.
export function Header({ onOpenCart }: { onOpenCart: () => void }) {
  const { data: user } = useCurrentUser();
  const { data: cart } = useCart();
  const logout = useLogout();
  const navigate = useNavigate();

  const itemCount = cartItemCount(cart);

  return (
    <header className="border-b border-neutral-200">
      <nav className="mx-auto flex max-w-5xl items-center justify-between px-4 py-3">
        <Link to="/" className="text-lg font-semibold text-neutral-900">
          Agora
        </Link>

        <div className="flex items-center gap-4 text-sm">
          {user ? (
            <>
              {/* The Admin link only appears for a StoreAdmin. WHY this is UX, not security: the
                  server gates every /admin/* request regardless of who can SEE the link — hiding it
                  is a courtesy to the other roles, not a boundary. (RequireRole / the authz matrix.) */}
              {(user.role === 'StoreAdmin' || user.role === 'Ops') && (
                <Link to="/admin/stock" className="text-neutral-700 hover:underline">
                  Stock
                </Link>
              )}
              {user.role === 'StoreAdmin' && (
                <Link to="/admin/products" className="text-neutral-700 hover:underline">
                  Admin
                </Link>
              )}
              <Link to="/account" className="text-neutral-700 hover:underline">
                {user.email}
              </Link>
              <button
                type="button"
                onClick={() => logout.mutate(undefined, { onSuccess: () => navigate('/') })}
                disabled={logout.isPending}
                className="text-neutral-600 hover:underline disabled:opacity-50"
              >
                Sign out
              </button>
            </>
          ) : (
            <>
              <Link to="/login" className="text-neutral-700 hover:underline">
                Sign in
              </Link>
              <Link
                to="/register"
                className="rounded-md bg-neutral-900 px-3 py-1.5 text-white hover:bg-neutral-700"
              >
                Register
              </Link>
            </>
          )}

          {/* ⚠️ THE CART BUTTON IS OUTSIDE THE `user` TERNARY, and that placement is the sprint's
              thesis expressed in JSX. A shopper with no account has a cart. Putting this inside the
              signed-in branch would be the UI asserting that a cart requires an identity — which is
              the exact assumption the anonymous cart cookie exists to refute, and the exact
              assumption that turns "add to cart" into a login wall. */}
          <button
            type="button"
            onClick={onOpenCart}
            className="flex items-center gap-1.5 rounded-md border border-neutral-300 px-3 py-1.5 text-neutral-800 hover:border-neutral-500"
          >
            Cart
            {itemCount > 0 && (
              // The count goes in the accessible name too — a bare number beside the word "Cart"
              // reads as "Cart 3" to a screen reader, which happens to be fine here and would not be
              // if the badge ever meant something else.
              <span
                aria-label={`${itemCount} item${itemCount === 1 ? '' : 's'} in cart`}
                className="rounded-full bg-neutral-900 px-1.5 text-xs font-medium tabular-nums text-white"
              >
                {itemCount}
              </span>
            )}
          </button>
        </div>
      </nav>
    </header>
  );
}
