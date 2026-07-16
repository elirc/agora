import type { ReactNode } from 'react';
import { Link, Navigate, useLocation } from 'react-router-dom';
import { useCurrentUser } from './queries';

// 🔗 like RequireAuth, this guard is UX, not security. It spares a non-admin a broken back-office and
// an anonymous visitor a redirect to sign in — but the SERVER re-checks the role on every /admin/*
// request (that is the real 403, enumerated in AuthorizationMatrixTests). A hidden route protects
// nothing; never move the authorization decision here because it "looks" gated. See ADR-0004/0005.
// 📘 S06 WIDENED THIS FROM ONE ROLE TO A LIST, and the shape is not an accident: the server's
// `ICurrentUser.RequireRole(params Role[] anyOf)` has taken a list since S02, because roles are
// CAPABILITIES, not a hierarchy. The client mirrors that. The first route needing it is the low-stock
// view, which Ops and StoreAdmin both reach — Ops packs the boxes, so Ops is exactly who needs to
// know what is running out, and still may not touch a price.
//
// The tempting shortcut was `user.role === 'StoreAdmin' || user.role === 'Ops'` inline in the route.
// It reads fine and it is a second authorization vocabulary: the day a third role appears, the server
// gains a list entry and the client gains an `||` somewhere nobody greps for.
export function RequireRole({ anyOf, children }: { anyOf: readonly string[]; children: ReactNode }) {
  const { data: user, isPending } = useCurrentUser();
  const location = useLocation();

  if (isPending) {
    return <p className="mx-auto max-w-3xl px-4 py-8 text-neutral-500">Checking your session…</p>;
  }

  // Anonymous → send to sign in (logging in COULD fix it). This mirrors the 401-then-403 order the
  // backend uses: we don't know who you are yet, so we ask you to identify first.
  if (!user) {
    const returnTo = encodeURIComponent(location.pathname + location.search);
    return <Navigate to={`/login?returnTo=${returnTo}`} replace />;
  }

  // Signed in but wrong role → a friendly denied page, NOT a redirect to login (logging in as
  // yourself again changes nothing — that is precisely a 403, not a 401).
  if (!user.role || !anyOf.includes(user.role)) {
    return <AccessDenied />;
  }

  return <>{children}</>;
}

function AccessDenied() {
  return (
    <main className="mx-auto max-w-3xl px-4 py-16 text-center" role="alert">
      <h1 className="text-2xl font-semibold text-neutral-900">Not your part of the shop</h1>
      <p className="mt-2 text-neutral-600">
        Your account doesn’t have access to this page. If you think this is a mistake, ask a Store
        Admin.
      </p>
      <Link to="/" className="mt-6 inline-block text-sm text-neutral-800 underline">
        Back to the storefront
      </Link>
    </main>
  );
}
