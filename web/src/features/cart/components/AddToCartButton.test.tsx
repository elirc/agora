import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { addCartLine, type ProductDto } from '../../../api/client';
import { AddToCartButton } from './AddToCartButton';

vi.mock('../../../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../api/client')>();
  return { ...actual, addCartLine: vi.fn(), getCart: vi.fn() };
});

const skillet: ProductDto = {
  id: 'prod-1',
  categoryId: 'cat-1',
  name: 'Cast Iron Skillet',
  slug: 'cast-iron-skillet',
  sku: 'AGA-KIT-002',
  description: 'Pre-seasoned cast iron.',
  priceAmount: 34.5,
  priceCurrency: 'USD',
  effectivePriceAmount: 27.6,
  isOnSale: true,
  savePercent: 20,
  sale: null,
  createdAtUtc: '2026-07-16T10:00:00Z',
};

function renderButton() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <AddToCartButton product={skillet} />
    </QueryClientProvider>,
  );
}

describe('AddToCartButton', () => {
  afterEach(() => vi.clearAllMocks());

  // ⚠️ THE ASSERTION THAT MATTERS IS WHAT IS *NOT* IN THE CALL. The button is holding a product with
  // an `effectivePriceAmount` right there on it, and it sends an id and a quantity. Nothing else.
  // Post the price and the price of a thing becomes whatever a browser — on somebody else's
  // computer, running code anyone can edit — says it is. This test is the one that goes red if a
  // well-meaning refactor "saves a lookup".
  it('sends a product id and a quantity, and never a price', async () => {
    vi.mocked(addCartLine).mockResolvedValue({
      lines: [],
      subtotalAmount: 0,
      subtotalCurrency: 'USD',
    });

    renderButton();

    await userEvent.click(screen.getByRole('button', { name: 'Add to cart' }));

    expect(addCartLine).toHaveBeenCalledWith('prod-1', 1);
    expect(addCartLine).toHaveBeenCalledTimes(1);
  });

});
