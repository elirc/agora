import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, getProduct } from '../api/client';
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
    vi.mocked(getProduct).mockResolvedValue({
      id: 'id-1',
      categoryId: 'cat-1',
      name: 'Cast Iron Skillet',
      slug: 'cast-iron-skillet',
      sku: 'AGA-KIT-002',
      description: 'Pre-seasoned cast iron.',
      priceAmount: 34.5,
      priceCurrency: 'USD',
      createdAtUtc: '2026-07-16T10:00:00Z',
    });

    renderAt('cast-iron-skillet');

    expect(
      await screen.findByRole('heading', { name: 'Cast Iron Skillet' }),
    ).toBeInTheDocument();
    expect(screen.getByText('$34.50')).toBeInTheDocument();
  });
});
