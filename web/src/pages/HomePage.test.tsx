import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { listCategories, listProducts, type ProductDto } from '../api/client';
import { HomePage } from './HomePage';

// LEARN: we mock at the API-CLIENT seam (../api/client), never at component internals. The test
// exercises the real HomePage, the real query hooks, the real grid — only the network is faked.
// That is the frontend mirror of the backend's in-memory repository fakes.
vi.mock('../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/client')>();
  return {
    ...actual,
    listCategories: vi.fn(),
    listProducts: vi.fn(),
    getProduct: vi.fn(),
  };
});

let nextId = 0;

function product(name: string, slug: string): ProductDto {
  nextId += 1;
  return {
    id: `id-${nextId}`,
    categoryId: 'cat-1',
    name,
    slug,
    sku: `AGA-${slug}`,
    description: `${name} description`,
    priceAmount: 19.99,
    priceCurrency: 'USD',
    // S04: the contract grew sale fields, and TypeScript made every fixture say what it means about
    // them. That is the generated types earning their keep — this file broke at COMPILE time the
    // moment the server's shape moved, rather than at runtime in front of a shopper.
    effectivePriceAmount: 19.99,
    isOnSale: false,
    savePercent: 0,
    sale: null,
    createdAtUtc: '2026-07-16T10:00:00Z',
  };
}

function renderHome() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <HomePage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('HomePage catalog grid', () => {
  beforeEach(() => {
    vi.mocked(listCategories).mockResolvedValue({ items: [], nextCursor: null });
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it('renders the products returned by the client', async () => {
    vi.mocked(listProducts).mockResolvedValue({
      items: [product('Skillet', 'skillet'), product('Board', 'board')],
      nextCursor: null,
    });

    renderHome();

    expect(await screen.findByText('Skillet')).toBeInTheDocument();
    expect(screen.getByText('Board')).toBeInTheDocument();
    // No next cursor → no Load more button.
    expect(screen.queryByRole('button', { name: 'Load more' })).not.toBeInTheDocument();
  });

  it('appends the next page when Load more is clicked', async () => {
    vi.mocked(listProducts)
      .mockResolvedValueOnce({
        items: [product('Skillet', 'skillet'), product('Board', 'board')],
        nextCursor: 'CURSOR-1',
      })
      .mockResolvedValueOnce({
        items: [product('Plates', 'plates')],
        nextCursor: null,
      });

    renderHome();

    expect(await screen.findByText('Skillet')).toBeInTheDocument();
    expect(screen.queryByText('Plates')).not.toBeInTheDocument();

    await userEvent.click(await screen.findByRole('button', { name: 'Load more' }));

    // Page 2 appears AND page 1 is still there — pages append, they don't replace.
    expect(await screen.findByText('Plates')).toBeInTheDocument();
    expect(screen.getByText('Skillet')).toBeInTheDocument();
    expect(screen.getByText('Board')).toBeInTheDocument();

    // Second page had no cursor → the button is gone.
    expect(screen.queryByRole('button', { name: 'Load more' })).not.toBeInTheDocument();
  });
});
