import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { archiveProduct, listAdminProducts, type AdminProductDto } from '../../api/client';
import { AdminProductsPage } from './AdminProductsPage';

vi.mock('../../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/client')>();
  return { ...actual, listAdminProducts: vi.fn(), archiveProduct: vi.fn(), restoreProduct: vi.fn() };
});

function product(overrides: Partial<AdminProductDto>): AdminProductDto {
  return {
    id: 'p1',
    categoryId: 'c1',
    name: 'Cast Iron Skillet',
    slug: 'cast-iron-skillet',
    sku: 'AGA-KIT-002',
    description: 'Pre-seasoned.',
    priceAmount: 34.5,
    priceCurrency: 'USD',
    version: 2,
    isArchived: false,
    archivedAtUtc: null,
    createdAtUtc: '2026-07-16T10:00:00Z',
    ...overrides,
  };
}

function renderList() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/admin/products']}>
        <AdminProductsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('AdminProductsPage', () => {
  afterEach(() => vi.clearAllMocks());

  it('renders each product with a status chip and its version, and archives on click', async () => {
    vi.mocked(listAdminProducts).mockResolvedValue({
      items: [
        product({ id: 'p1', name: 'Cast Iron Skillet', version: 2, isArchived: false }),
        product({ id: 'p2', name: 'Bamboo Board', version: 5, isArchived: true, archivedAtUtc: '2026-07-16T11:00:00Z' }),
      ],
      nextCursor: null,
    });
    vi.mocked(archiveProduct).mockResolvedValue(product({ id: 'p1', version: 3, isArchived: true }));

    renderList();

    const activeRow = (await screen.findByText('Cast Iron Skillet')).closest('tr')!;
    expect(within(activeRow).getByText('Active')).toBeInTheDocument();
    expect(within(activeRow).getByText('2')).toBeInTheDocument(); // version column

    const archivedRow = screen.getByText('Bamboo Board').closest('tr')!;
    expect(within(archivedRow).getByText('Archived')).toBeInTheDocument();
    // The archived row offers Restore, not Archive.
    expect(within(archivedRow).getByRole('button', { name: 'Restore' })).toBeInTheDocument();

    // Archiving the active product presents its current version as the concurrency guard.
    await userEvent.click(within(activeRow).getByRole('button', { name: 'Archive' }));
    expect(archiveProduct).toHaveBeenCalledWith('p1', 2);
  });
});
