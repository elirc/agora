import { useMutation, useQueryClient } from '@tanstack/react-query';
import { login, logout, register, type Credentials, type MeDto } from '../../api/client';
import { cartKey } from '../cart/queries';
import { currentUserKey } from './queries';

// 📘 SIGNING IN CHANGES YOUR CART, SO SIGNING IN INVALIDATES YOUR CART. That one added line is the
// entire frontend of this sprint's flagship feature — and its smallness is the finding.
//
// The server merged the anonymous cart into the owned one during the login request itself, so by the
// time this runs, "what is in my cart" already has a different answer. The client's job is not to
// work out what changed — it cannot, and it must not try: the merge rule lives in the domain, and a
// copy of it here would be a second policy, in a browser, drifting. Its job is to STOP BELIEVING THE
// OLD ANSWER. Invalidate; refetch; the header badge and the drawer both update themselves, because
// they read the same key.
//
// ⚠️ what happens without it: the shopper signs in, the merge succeeds, and the badge still says 2.
// Nothing is broken on the server — the UI is showing a fact that stopped being true mid-request.
// Cache invalidation on identity change is not a detail; it is the correctness condition for every
// cached thing scoped to a user.
function useIdentityChange() {
  const queryClient = useQueryClient();

  return (user: MeDto | null) => {
    // The user is SEEDED, not invalidated: the response we just got IS the answer to "who am I", so
    // re-asking /me would be a round trip to learn what we were already told. (S02's call; unchanged.)
    queryClient.setQueryData(currentUserKey, user);

    // The cart is INVALIDATED, not seeded: the response says nothing about it, and only the server
    // knows what the merge produced. Seed what you were told; refetch what you weren't.
    queryClient.invalidateQueries({ queryKey: cartKey });
  };
}

// WHY wrap the client fns instead of passing them as mutationFn directly: TanStack Query v5 calls
// mutationFn with a SECOND context argument, and we don't want that leaking into the typed client
// call. The one-arg wrapper keeps the seam exactly as narrow as the client function's signature.
export function useLogin() {
  const identityChanged = useIdentityChange();
  return useMutation({
    mutationFn: (credentials: Credentials) => login(credentials),
    // WHY the user is WRITTEN to the cache and the cart is only INVALIDATED: the server just told us
    // exactly who we are in the login response, so we seed it — no extra /me round trip to learn
    // what we were already handed. The cart is the opposite case: the response says nothing about
    // it, and only the server knows what the merge produced. Seed what you were told; refetch what
    // you weren't.
    onSuccess: (user: MeDto) => identityChanged(user),
  });
}

export function useRegister() {
  const identityChanged = useIdentityChange();
  return useMutation({
    mutationFn: (credentials: Credentials) => register(credentials),
    // Register merges too (see AuthEndpoints): "create an account to check out" is the oldest funnel
    // in retail, so this path is if anything more likely to be carrying a full cart than login is.
    onSuccess: (user: MeDto) => identityChanged(user),
  });
}

export function useLogout() {
  const queryClient = useQueryClient();
  const identityChanged = useIdentityChange();
  return useMutation({
    mutationFn: () => logout(),
    onSuccess: () => {
      // Signed out: clear the user and drop any user-scoped cached data, so it cannot be read by
      // whoever signs in next on this browser. S05 makes the cart the first real entry on that list
      // — and the one it would be most embarrassing to get wrong, because a stale cart in the cache
      // is a stranger's shopping rendered in the drawer of a shared laptop. The server agrees:
      // logout mints no cart cookie, so the refetch comes back honestly empty. Orders join in S07.
      identityChanged(null);

      // WHY logout ALSO re-asks /me while login doesn't: the asymmetry is real. A login response
      // tells us who we are; a logout response tells us nothing except that it worked, so "am I
      // anonymous now?" is a question only the server can answer. Belt and braces on the way out —
      // the direction where being wrong means showing someone else's data. (S02's call; unchanged.)
      queryClient.invalidateQueries({ queryKey: currentUserKey });
    },
  });
}
