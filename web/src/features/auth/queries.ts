import { useQuery } from '@tanstack/react-query';
import { getCurrentUser } from '../../api/client';

// LEARN: the signed-in user is SERVER state, so it lives in TanStack Query like any other query —
// not in a bespoke React Context. One cache key, one source of truth; the Header, the route guard,
// and the account page all read the SAME cached user, and a login/logout updates it in one place.
// (We considered an AuthContext; deriving from this query is less machinery and cannot drift out
// of sync with the server — see docs/adr/0004.)
export const currentUserKey = ['currentUser'] as const;

export function useCurrentUser() {
  return useQuery({
    queryKey: currentUserKey,
    queryFn: getCurrentUser,
    // WHY retry:false — a 401 is already translated to `null` in the client, and a real failure
    // shouldn't be hammered three times before the UI can react. Auth state should settle fast.
    retry: false,
    staleTime: 60_000,
  });
}
