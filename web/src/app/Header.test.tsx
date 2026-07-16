import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { getCurrentUser } from '../api/client';
import { Header } from './Header';

vi.mock('../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/client')>();
  return { ...actual, getCurrentUser: vi.fn(), logout: vi.fn() };
});

function renderHeader() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <Header />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('Header', () => {
  afterEach(() => vi.clearAllMocks());

  it('offers sign in / register when anonymous', async () => {
    vi.mocked(getCurrentUser).mockResolvedValue(null);

    renderHeader();

    expect(await screen.findByRole('link', { name: 'Sign in' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Register' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Sign out' })).not.toBeInTheDocument();
  });

  it('shows the email and a sign-out button when signed in', async () => {
    vi.mocked(getCurrentUser).mockResolvedValue({ id: 'u1', email: 'ada@example.com', role: 'Customer' });

    renderHeader();

    expect(await screen.findByRole('link', { name: 'ada@example.com' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Sign in' })).not.toBeInTheDocument();
  });
});
