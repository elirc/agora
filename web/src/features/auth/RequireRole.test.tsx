import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { getCurrentUser } from '../../api/client';
import { RequireRole } from './RequireRole';

vi.mock('../../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/client')>();
  return { ...actual, getCurrentUser: vi.fn() };
});

function renderGuardedAdmin() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/admin/products']}>
        <Routes>
          <Route
            path="/admin/products"
            element={
              <RequireRole role="StoreAdmin">
                <div>Back-office catalog</div>
              </RequireRole>
            }
          />
          <Route path="/login" element={<div>Login page</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('RequireRole', () => {
  afterEach(() => vi.clearAllMocks());

  it('shows a friendly denied page to a signed-in Customer (a 403, not a login bounce)', async () => {
    vi.mocked(getCurrentUser).mockResolvedValue({ id: 'u1', email: 'shopper@example.com', role: 'Customer' });

    renderGuardedAdmin();

    expect(await screen.findByText('Store Admins only')).toBeInTheDocument();
    expect(screen.queryByText('Back-office catalog')).not.toBeInTheDocument();
    // A wrong role is NOT sent to login — logging in as themselves wouldn't help.
    expect(screen.queryByText('Login page')).not.toBeInTheDocument();
  });

  it('renders the back-office for a StoreAdmin', async () => {
    vi.mocked(getCurrentUser).mockResolvedValue({ id: 'u2', email: 'admin@agora.test', role: 'StoreAdmin' });

    renderGuardedAdmin();

    expect(await screen.findByText('Back-office catalog')).toBeInTheDocument();
    expect(screen.queryByText('Store Admins only')).not.toBeInTheDocument();
  });

  it('bounces an anonymous visitor to /login', async () => {
    vi.mocked(getCurrentUser).mockResolvedValue(null);

    renderGuardedAdmin();

    expect(await screen.findByText('Login page')).toBeInTheDocument();
  });
});
