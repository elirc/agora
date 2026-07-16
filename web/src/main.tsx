import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryCache, QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from 'react-router-dom';
import { ApiError } from './api/client';
import { currentUserKey } from './features/auth/queries';
import { router } from './app/router';
import './index.css';

// LEARN: ONE global 401 handler instead of a check in every hook. If ANY query gets a 401, the
// session lapsed mid-visit — so we clear the cached user once, centrally. The Header re-renders to
// signed-out and RequireAuth bounces protected pages to /login, all from this single place.
// (useCurrentUser translates its own 401 to `null` before it reaches here, so this fires for the
// OTHER queries — the owned resources that arrive in later sprints.)
const queryClient = new QueryClient({
  queryCache: new QueryCache({
    onError: (error) => {
      if (error instanceof ApiError && error.status === 401) {
        queryClient.setQueryData(currentUserKey, null);
      }
    },
  }),
});

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </StrictMode>,
);
