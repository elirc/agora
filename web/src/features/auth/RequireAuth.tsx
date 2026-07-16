import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useCurrentUser } from './queries';

// 🔗 the guard is UX, not security. It stops an ANONYMOUS shopper from seeing a half-rendered
// account page and sends them to sign in — but it is NOT an authorization boundary. The SERVER
// re-checks every request (a 401/403 from the API is the real gate); a hidden route or a disabled
// button protects nothing on its own. Never move an authorization decision into the client because
// it "looks" protected here. See docs/adr/0004 and the authorization-matrix test on the backend.
export function RequireAuth({ children }: { children: ReactNode }) {
  const { data: user, isPending } = useCurrentUser();
  const location = useLocation();

  if (isPending) {
    return <p className="mx-auto max-w-3xl px-4 py-8 text-neutral-500">Checking your session…</p>;
  }

  if (!user) {
    // WHY carry returnTo: after signing in, send the shopper back to the page they wanted, not to
    // a generic home. `replace` so the guard doesn't add a history entry to bounce back through.
    const returnTo = encodeURIComponent(location.pathname + location.search);
    return <Navigate to={`/login?returnTo=${returnTo}`} replace />;
  }

  return <>{children}</>;
}
