import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, getProduct, type ProductDto } from '../api/client';
import { ProductPage } from './ProductPage';

vi.mock('../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/client')>();
  return {
    ...actual,
    getProduct: vi.fn(),
    listProducts: vi.fn(),
    listCategories: vi.fn(),
  };
});

function renderAt(slug: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[`/products/${slug}`]}>
        <Routes>
          <Route path="/products/:slug" element={<ProductPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('ProductPage', () => {
  afterEach(() => {
    vi.clearAllMocks();
  });

  it('shows a not-found state when the API returns a 404', async () => {
    // WHY: the client throws a typed ApiError carrying the status the server promised, so the
    // page can render a specific "not found" state rather than a generic error.
    vi.mocked(getProduct).mockRejectedValue(
      new ApiError(404, {
        type: 'https://agora.example/problems/not-found',
        title: 'Resource not found',
        status: 404,
      }),
    );

    renderAt('ghost');

    expect(await screen.findByText('Product not found')).toBeInTheDocument();
  });

  it('renders the product on success', async () => {
    vi.mocked(getProduct).mockResolvedValue(skillet());

    renderAt('cast-iron-skillet');

    expect(
      await screen.findByRole('heading', { name: 'Cast Iron Skillet' }),
    ).toBeInTheDocument();
    expect(screen.getByText('$34.50')).toBeInTheDocument();
  });

  it('shows the sale price and when the sale ends', async () => {
    // S04: the page renders what the server resolved — the sale price, the struck-through list
    // price, the badge — plus the campaign's name and end date, which is the one thing here rendered
    // in the SHOPPER's timezone rather than the server's.
    vi.mocked(getProduct).mockResolvedValue(
      skillet({
        effectivePriceAmount: 27.6,
        isOnSale: true,
        savePercent: 20,
        sale: {
          campaignId: 'c-1',
          name: 'Cast Iron Week',
          startsAtUtc: '2026-08-07T00:00:00Z',
          endsAtUtc: '2026-08-10T12:00:00Z',
        },
      }),
    );

    renderAt('cast-iron-skillet');

    expect(await screen.findByText('$27.60')).toBeInTheDocument();
    expect(screen.getByText('$34.50')).toBeInTheDocument();
    expect(screen.getByText('Save 20%')).toBeInTheDocument();
    expect(screen.getByText(/Cast Iron Week · Ends /)).toBeInTheDocument();
  });
});

function skillet(overrides: Partial<ProductDto> = {}): ProductDto {
  return {
    id: 'id-1',
    categoryId: 'cat-1',
    name: 'Cast Iron Skillet',
    slug: 'cast-iron-skillet',
    sku: 'AGA-KIT-002',
    description: 'Pre-seasoned cast iron.',
    priceAmount: 34.5,
    priceCurrency: 'USD',
    effectivePriceAmount: 34.5,
    isOnSale: false,
    savePercent: 0,
    sale: null,
    createdAtUtc: '2026-07-16T10:00:00Z',
    ...overrides,
  };
}
