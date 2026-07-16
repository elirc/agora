import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  clearCart,
  getCart,
  removeCartLine,
  setCartLineQuantity,
  type CartDto,
  type CartLineDto,
} from '../../../api/client';
import { CartDrawer } from './CartDrawer';

// LEARN: mocked at the API-CLIENT seam, never at component internals. The real drawer, the real query
// hooks, the real mutations and the real optimistic patch all run — only the network is faked.
vi.mock('../../../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../api/client')>();
  return {
    ...actual,
    getCart: vi.fn(),
    setCartLineQuantity: vi.fn(),
    removeCartLine: vi.fn(),
    clearCart: vi.fn(),
  };
});

// ⚠️ EVERY AMOUNT IN THESE FIXTURES IS A LITERAL, and that is not laziness — it is the rule the whole
// course is about, applied to test data. Writing `lineTotalAmount: 19.99 * 3` here would compute
// 59.97000000000001, because this file is JavaScript too. A fixture that does money math is a fixture
// that disagrees with the server it is pretending to be. The server sends numbers; so do we.
//
// The two lines are also deliberately priced so that no line total collides with a unit price or with
// the subtotal. A one-line cart whose subtotal equals its only line total makes `getByText('$69.00')`
// ambiguous — and the fix for an ambiguous query is better data, not a looser query.
function skillet(overrides: Partial<CartLineDto> = {}): CartLineDto {
  return {
    id: 'line-1',
    productId: 'prod-1',
    name: 'Cast Iron Skillet',
    slug: 'cast-iron-skillet',
    sku: 'AGA-KIT-002',
    quantity: 2,
    currency: 'USD',
    unitPriceAmount: 34.5,
    lineTotalAmount: 69,
    priceAtAdditionAmount: 34.5,
    priceChanged: false,
    addedAtUtc: '2026-07-16T10:00:00Z',
    ...overrides,
  };
}

function board(overrides: Partial<CartLineDto> = {}): CartLineDto {
  return {
    id: 'line-2',
    productId: 'prod-2',
    name: 'Bamboo Cutting Board',
    slug: 'bamboo-cutting-board',
    sku: 'AGA-KIT-003',
    quantity: 3,
    currency: 'USD',
    unitPriceAmount: 19.99,
    lineTotalAmount: 59.97,
    priceAtAdditionAmount: 19.99,
    priceChanged: false,
    addedAtUtc: '2026-07-16T10:05:00Z',
    ...overrides,
  };
}

function cart(overrides: Partial<CartDto> = {}): CartDto {
  return {
    lines: [skillet(), board()],
    subtotalAmount: 128.97,
    subtotalCurrency: 'USD',
    ...overrides,
  };
}

const emptyCart: CartDto = { lines: [], subtotalAmount: 0, subtotalCurrency: 'USD' };

function renderDrawer() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <CartDrawer open onClose={() => {}} />
    </QueryClientProvider>,
  );
}

// 📘 A TINY STATEFUL FAKE, AND IT IS NOT AN ELABORATION — IT IS THE ONLY HONEST SHAPE. Every cart
// mutation ends with `onSettled: invalidate`, so a refetch always follows the write. A `getCart` that
// resolves the SAME fixture forever is a server that forgets what it was just told: the mutation
// seeds the new cart, the refetch lands, and the drawer reverts to a stale answer no real server
// would give. The test would then be measuring the mock.
//
// So the fake holds one variable and each write updates it — which is exactly what the backend's
// in-memory repositories do (see InMemoryCartRepository). Same instinct on both sides of the wire: a
// fake may lie about HOW (a variable, not Postgres), never about WHAT.
let serverCart: CartDto;

function serverNowHas(next: CartDto): CartDto {
  serverCart = next;
  return next;
}

describe('CartDrawer', () => {
  beforeEach(() => {
    serverCart = cart();
    vi.mocked(getCart).mockImplementation(() => Promise.resolve(serverCart));
  });

  afterEach(() => vi.clearAllMocks());

  it('renders each line and the server-resolved subtotal', async () => {
    renderDrawer();

    expect(await screen.findByText('Cast Iron Skillet')).toBeInTheDocument();
    expect(screen.getByText('Bamboo Cutting Board')).toBeInTheDocument();
    expect(screen.getByText('$34.50 each')).toBeInTheDocument();
    expect(screen.getByText('$69.00')).toBeInTheDocument();
    expect(screen.getByText('$59.97')).toBeInTheDocument();
    // The subtotal is the server's number. Nothing in this app knows how to compute it.
    expect(screen.getByText('$128.97')).toBeInTheDocument();
    expect(screen.getByLabelText('Quantity of Cast Iron Skillet')).toHaveTextContent('2');
  });

  it('shows an empty state rather than an error when nothing is in the cart', async () => {
    // An empty cart is a cart — the server 200s, and this is a state, not a failure.
    serverCart = emptyCart;

    renderDrawer();

    expect(await screen.findByText(/Your cart is empty/)).toBeInTheDocument();
    expect(screen.queryByText('Subtotal')).not.toBeInTheDocument();
  });

  // 🔗 THE OPTIMISTIC-UI TEST, AND THE SECOND ASSERTION IS THE ONE THAT MATTERS. The stepper reads 3
  // immediately — the client authored that number, so it is not a guess. But the subtotal it renders
  // is the one the SERVER sent back, not `34.50 × 3` computed here. The drawer trusts the mutation's
  // response; it never does the arithmetic itself.
  it('bumps the quantity optimistically and takes the subtotal from the response', async () => {
    vi.mocked(setCartLineQuantity).mockImplementation(() =>
      Promise.resolve(
        serverNowHas(
          cart({
            lines: [skillet({ quantity: 3, lineTotalAmount: 103.5 }), board()],
            subtotalAmount: 163.47,
          }),
        ),
      ),
    );

    renderDrawer();
    await screen.findByText('Cast Iron Skillet');

    await userEvent.click(screen.getByLabelText('Increase quantity of Cast Iron Skillet'));

    expect(setCartLineQuantity).toHaveBeenCalledWith('line-1', 3);
    await waitFor(() => expect(screen.getByText('$163.47')).toBeInTheDocument());
    expect(screen.getByLabelText('Quantity of Cast Iron Skillet')).toHaveTextContent('3');
  });

  // ⚠️ THE ROLLBACK. A refused write (the server caps a line at 99) must put the stepper back where
  // it was, or the UI is showing a quantity the cart does not have and the shopper is the last to
  // know. Optimism without rollback is just being wrong quickly.
  it('rolls the optimistic quantity back when the write is refused', async () => {
    vi.mocked(setCartLineQuantity).mockRejectedValue(new Error('nope'));

    renderDrawer();
    await screen.findByText('Cast Iron Skillet');

    await userEvent.click(screen.getByLabelText('Increase quantity of Cast Iron Skillet'));

    await waitFor(() =>
      expect(screen.getByLabelText('Quantity of Cast Iron Skillet')).toHaveTextContent('2'),
    );
    expect(screen.getByText('$128.97')).toBeInTheDocument();
  });

  // 📘 THE SPRINT'S SECOND TEACHING AXIS, at the client seam: the shopper added it during a sale, the
  // sale ended, and the drawer says so — in the drawer, not on the receipt. Note the price it charges
  // is TODAY's ($34.50); the snapshot is rendered as history, struck through.
  it('tells the shopper when a price has moved since they added the line', async () => {
    serverCart = cart({
      lines: [skillet({ priceChanged: true, priceAtAdditionAmount: 27.6 })],
      subtotalAmount: 69,
    });

    renderDrawer();

    expect(await screen.findByText(/when you added it/)).toBeInTheDocument();
    expect(screen.getByText('$27.60')).toBeInTheDocument();
    expect(screen.getByText('$34.50 each')).toBeInTheDocument();
  });

  it('removes a line and empties the cart through the client', async () => {
    vi.mocked(removeCartLine).mockImplementation(() =>
      Promise.resolve(serverNowHas(cart({ lines: [board()], subtotalAmount: 59.97 }))),
    );
    vi.mocked(clearCart).mockImplementation(() => Promise.resolve(serverNowHas(emptyCart)));

    renderDrawer();
    await screen.findByText('Cast Iron Skillet');

    await userEvent.click(screen.getByRole('button', { name: 'Remove Cast Iron Skillet' }));

    expect(removeCartLine).toHaveBeenCalledWith('line-1');
    await waitFor(() => expect(screen.queryByText('Cast Iron Skillet')).not.toBeInTheDocument());

    await userEvent.click(screen.getByRole('button', { name: 'Empty cart' }));

    expect(clearCart).toHaveBeenCalled();
    await waitFor(() => expect(screen.getByText(/Your cart is empty/)).toBeInTheDocument());
  });
});
