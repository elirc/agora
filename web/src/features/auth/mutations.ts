import { useMutation, useQueryClient } from '@tanstack/react-query';
import { login, logout, register, type Credentials, type MeDto } from '../../api/client';
import { currentUserKey } from './queries';

// WHY wrap the client fns instead of passing them as mutationFn directly: TanStack Query v5 calls
// mutationFn with a SECOND context argument, and we don't want that leaking into the typed client
// call. The one-arg wrapper keeps the seam exactly as narrow as the client function's signature.
export function useLogin() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (credentials: Credentials) => login(credentials),
    // WHY write the cache directly instead of invalidating: the server just told us exactly who
    // we are in the login response, so we seed the cache with it — no extra /me round trip to
    // learn what we already know. The Header re-renders to signed-in immediately.
    onSuccess: (user: MeDto) => queryClient.setQueryData(currentUserKey, user),
  });
}

export function useRegister() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (credentials: Credentials) => register(credentials),
    onSuccess: (user: MeDto) => queryClient.setQueryData(currentUserKey, user),
  });
}

export function useLogout() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => logout(),
    onSuccess: () => {
      // Signed out: clear the user, then drop any user-scoped cached data so it can't be read by
      // whoever signs in next on this browser. Today that's just the user; carts and orders join
      // this list as they arrive (S05, S07).
      queryClient.setQueryData(currentUserKey, null);
      queryClient.invalidateQueries({ queryKey: currentUserKey });
    },
  });
}
