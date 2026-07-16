import { Link, useNavigate } from 'react-router-dom';
import { useCurrentUser } from '../features/auth/queries';
import { useLogout } from '../features/auth/mutations';

// 🔗 the header reflects auth state, but showing/hiding a link is UX only — the server is the
// authority (see RequireAuth). Anonymous shoppers see Sign in / Register; signed-in users see
// their email (a link to the account page) and Sign out. It reads `useCurrentUser`, the same
// cached user every other component reads, so login/logout updates the header with no prop-drilling.
export function Header() {
  const { data: user } = useCurrentUser();
  const logout = useLogout();
  const navigate = useNavigate();

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
        </div>
      </nav>
    </header>
  );
}
