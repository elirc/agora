import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, login } from '../api/client';
import { LoginPage } from './LoginPage';

// LEARN: mock at the API-CLIENT seam (../api/client), never at the query hook or component. The
// test drives the real LoginPage, the real useLogin mutation, the real form — only the network is
// faked. Same discipline as the backend's in-memory repository fakes.
vi.mock('../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/client')>();
  return { ...actual, login: vi.fn(), getCurrentUser: vi.fn() };
});

function renderLogin() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/login']}>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/account" element={<div>Account landed</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('LoginPage', () => {
  afterEach(() => vi.clearAllMocks());

  it('submits credentials and navigates on success', async () => {
    vi.mocked(login).mockResolvedValue({ id: 'u1', email: 'ada@example.com', role: 'Customer' });

    renderLogin();

    await userEvent.type(screen.getByLabelText('Email'), 'ada@example.com');
    await userEvent.type(screen.getByLabelText('Password'), 'a-long-enough-password');
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(login).toHaveBeenCalledWith({
      email: 'ada@example.com',
      password: 'a-long-enough-password',
    });
    // The default returnTo is /account — landing there proves the success navigation ran.
    expect(await screen.findByText('Account landed')).toBeInTheDocument();
  });

  it('shows the server error message when login is rejected', async () => {
    vi.mocked(login).mockRejectedValue(
      new ApiError(401, {
        type: 'https://agora.example/problems/unauthenticated',
        title: 'Authentication required',
        status: 401,
      }),
    );

    renderLogin();

    await userEvent.type(screen.getByLabelText('Email'), 'ada@example.com');
    await userEvent.type(screen.getByLabelText('Password'), 'wrong-password');
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    // The page surfaces the SERVER's uniform message — it doesn't invent its own.
    expect(await screen.findByRole('alert')).toHaveTextContent('Authentication required');
    expect(screen.queryByText('Account landed')).not.toBeInTheDocument();
  });
});
