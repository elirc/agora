import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { getCurrentUser } from '../../api/client';
import { RequireAuth } from './RequireAuth';

vi.mock('../../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/client')>();
  return { ...actual, getCurrentUser: vi.fn() };
});

function renderGuardedAccount() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/account']}>
        <Routes>
          <Route
            path="/account"
            element={
              <RequireAuth>
                <div>Secret account area</div>
              </RequireAuth>
            }
          />
          <Route path="/login" element={<div>Login page</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('RequireAuth', () => {
  afterEach(() => vi.clearAllMocks());

  it('redirects an anonymous visitor to /login', async () => {
    vi.mocked(getCurrentUser).mockResolvedValue(null);

    renderGuardedAccount();

    expect(await screen.findByText('Login page')).toBeInTheDocument();
    expect(screen.queryByText('Secret account area')).not.toBeInTheDocument();
  });

  it('renders the protected content for a signed-in user', async () => {
    vi.mocked(getCurrentUser).mockResolvedValue({ id: 'u1', email: 'ada@example.com', role: 'Customer' });

    renderGuardedAccount();

    expect(await screen.findByText('Secret account area')).toBeInTheDocument();
    expect(screen.queryByText('Login page')).not.toBeInTheDocument();
  });
});
