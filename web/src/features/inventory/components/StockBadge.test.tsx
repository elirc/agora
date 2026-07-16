import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { StockBadge } from './StockBadge';
import * as client from '../../../api/client';

vi.mock('../../../api/client', async () => {
  const actual = await vi.importActual<typeof client>('../../../api/client');
  return { ...actual, getStock: vi.fn() };
});

const getStock = vi.mocked(client.getStock);

function renderBadge(verbose = false) {
  // Retry off so an error case fails once and renders, rather than backing off three times and
  // timing out the test — the component's error path is the thing under test, not Query's patience.
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  return render(
    <QueryClientProvider client={queryClient}>
      <StockBadge productId="p1" verbose={verbose} />
    </QueryClientProvider>,
  );
}

const AsOf = '2026-08-04T12:00:00Z';

describe('StockBadge', () => {
  beforeEach(() => vi.resetAllMocks());

  it('states plainly that a product is out of stock', async () => {
    getStock.mockResolvedValue({ available: 0, inStock: false, availableAt: AsOf });

    renderBadge();

    expect(await screen.findByTestId('stock-out')).toHaveTextContent('Out of stock');
  });

  it('nudges when only a few are left', async () => {
    getStock.mockResolvedValue({ available: 2, inStock: true, availableAt: AsOf });

    renderBadge();

    expect(await screen.findByTestId('stock-low')).toHaveTextContent('Only 2 left');
  });

  it('says nothing on a grid card when stock is comfortable', async () => {
    getStock.mockResolvedValue({ available: 40, inStock: true, availableAt: AsOf });

    renderBadge();

    await waitFor(() => expect(getStock).toHaveBeenCalled());

    // No badge at all: "40 in stock" on a card is noise, and the shopper only needs telling when the
    // number is a reason to hurry or a reason to stop.
    expect(screen.queryByTestId('stock-low')).not.toBeInTheDocument();
    expect(screen.queryByTestId('stock-in')).not.toBeInTheDocument();
  });

  it('shows the exact count on a product page when it is high enough not to matter', async () => {
    getStock.mockResolvedValue({ available: 12, inStock: true, availableAt: AsOf });

    renderBadge(true);

    expect(await screen.findByTestId('stock-in')).toHaveTextContent('12 in stock');
  });

  // ⚠️ THE ONE THAT MATTERS MOST, and it asserts an ABSENCE.
  //
  // When we cannot reach the stock service, the badge renders nothing. The tempting alternatives are
  // all worse: "In stock" is a guess made in the shop's favour about the one number the shopper is
  // deciding on; "Unavailable" turns our outage into their disappointment. Silence is the only option
  // that does not put words in the shop's mouth.
  it('says nothing rather than guessing when the count cannot be fetched', async () => {
    getStock.mockRejectedValue(new Error('stock service is down'));

    const { container } = renderBadge(true);

    await waitFor(() => expect(getStock).toHaveBeenCalled());

    expect(container).toBeEmptyDOMElement();
  });

  it('handles a count that arrives as a string', async () => {
    // The OpenAPI document types an int32 as number-or-string; a client that only handles `number`
    // renders "Only 2 left" as "Only 2 left" today and breaks on the day the server sends "2".
    getStock.mockResolvedValue({ available: '2', inStock: true, availableAt: AsOf });

    renderBadge();

    expect(await screen.findByTestId('stock-low')).toHaveTextContent('Only 2 left');
  });
});
