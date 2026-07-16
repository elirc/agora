import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { getCart, getCurrentUser, login, type CartDto } from '../api/client';
import { useLogin } from '../features/auth/mutations';
import { Header } from './Header';

vi.mock('../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/client')>();
  return {
    ...actual,
    getCurrentUser: vi.fn(),
    logout: vi.fn(),
    getCart: vi.fn(),
    login: vi.fn(),
  };
});

const emptyCart: CartDto = { lines: [], subtotalAmount: 0, subtotalCurrency: 'USD' };

function cartOf(quantity: number): CartDto {
  return {
    lines: [
      {
        id: 'line-1',
        productId: 'prod-1',
        name: 'Cast Iron Skillet',
        slug: 'cast-iron-skillet',
        sku: 'AGA-KIT-002',
        quantity,
        currency: 'USD',
        unitPriceAmount: 34.5,
        lineTotalAmount: 34.5 * quantity,
        priceAtAdditionAmount: 34.5,
        priceChanged: false,
        addedAtUtc: '2026-07-16T10:00:00Z',
      },
    ],
    subtotalAmount: 34.5 * quantity,
    subtotalCurrency: 'USD',
  };
}

function renderHeader(children?: ReactNode) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <Header onOpenCart={() => {}} />
        {children}
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('Header', () => {
  beforeEach(() => {
    vi.mocked(getCart).mockResolvedValue(emptyCart);
  });

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

  // 📘 THE CART BADGE IS THERE FOR A SHOPPER WITH NO ACCOUNT — the sprint's thesis, rendered. A cart
  // does not require an identity, so the button lives outside the signed-in branch. If this test ever
  // goes red because someone tidied the header, "add to cart" has just become a login wall.
  it('shows the cart badge to an anonymous shopper', async () => {
    vi.mocked(getCurrentUser).mockResolvedValue(null);
    vi.mocked(getCart).mockResolvedValue(cartOf(3));

    renderHeader();

    expect(await screen.findByLabelText('3 items in cart')).toBeInTheDocument();
  });

  // 🔗 THE MERGE, MADE VISIBLE — the client half of the sprint's flagship, and notice how little of it
  // there is. The server did the merge during the login request; this app's entire contribution is
  // that `useLogin` invalidates the cart key, so the badge refetches and changes on its own. The
  // Header does not know a merge happened, imports nothing from features/auth's internals, and is
  // handed no number. That is what one cache of server state buys.
  it('refetches the cart after signing in, so a merged cart shows up in the badge', async () => {
    vi.mocked(getCurrentUser).mockResolvedValue(null);
    // Before: the anonymous cart holds one skillet. After the merge, the server's cart holds three.
    vi.mocked(getCart).mockResolvedValueOnce(cartOf(1)).mockResolvedValue(cartOf(3));
    vi.mocked(login).mockResolvedValue({ id: 'u1', email: 'ada@example.com', role: 'Customer' });

    renderHeader(<SignInButton />);

    expect(await screen.findByLabelText('1 item in cart')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Sign in now' }));

    expect(await screen.findByLabelText('3 items in cart')).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.getByRole('link', { name: 'ada@example.com' })).toBeInTheDocument(),
    );
  });
});

// A stand-in for the login form: this test is about what the HEADER does when identity changes, so
// the trigger is the real `useLogin` hook and nothing else from the login page.
function SignInButton() {
  const signIn = useLogin();
  return (
    <button
      type="button"
      onClick={() => signIn.mutate({ email: 'ada@example.com', password: 'a-long-enough-password' })}
    >
      Sign in now
    </button>
  );
}
