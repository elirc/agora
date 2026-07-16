import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  ApiError,
  getAdminProduct,
  listCategories,
  updateProduct,
  type AdminProductDto,
  type ProblemDetails,
} from '../../api/client';
import { AdminProductEditPage } from './AdminProductEditPage';

// Mock at the API-CLIENT seam: the real page, form, mutation, and conflict logic all run; only the
// network is faked. Same discipline as every other Agora test.
vi.mock('../../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/client')>();
  return {
    ...actual,
    getAdminProduct: vi.fn(),
    listCategories: vi.fn(),
    updateProduct: vi.fn(),
  };
});

function productAt(version: number, name: string): AdminProductDto {
  return {
    id: 'p1',
    categoryId: 'c1',
    name,
    slug: 'cast-iron-skillet',
    sku: 'AGA-KIT-002',
    description: 'Pre-seasoned.',
    priceAmount: 34.5,
    priceCurrency: 'USD',
    version,
    isArchived: false,
    archivedAtUtc: null,
    createdAtUtc: '2026-07-16T10:00:00Z',
  };
}

function renderEdit() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/admin/products/p1']}>
        <Routes>
          <Route path="/admin/products/:id" element={<AdminProductEditPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('AdminProductEditPage — the lost-update conflict UX', () => {
  afterEach(() => vi.clearAllMocks());

  it('shows the conflict banner with the current server state on a 409, then rebases and saves on overwrite', async () => {
    vi.mocked(listCategories).mockResolvedValue({
      items: [
        { id: 'c1', name: 'Kitchen', slug: 'kitchen', createdAtUtc: '2026-07-16T10:00:00Z' },
      ],
      nextCursor: null,
    });
    // The admin loads version 1.
    vi.mocked(getAdminProduct).mockResolvedValue(productAt(1, 'Cast Iron Skillet'));

    // First save → 409 carrying the CURRENT state (another admin renamed it and it is now v2).
    const theirs = productAt(2, 'Cast Iron Skillet PRO');
    const conflictBody = {
      type: 'https://agora.example/problems/conflict',
      title: 'Version conflict',
      status: 409,
      detail: 'This product was changed by someone else.',
      current: theirs,
    } as unknown as ProblemDetails;

    vi.mocked(updateProduct)
      .mockRejectedValueOnce(new ApiError(409, conflictBody))
      .mockResolvedValueOnce(productAt(3, 'My New Name'));

    renderEdit();

    // Form loaded with the current name.
    const nameInput = await screen.findByLabelText('Name');
    await userEvent.clear(nameInput);
    await userEvent.type(nameInput, 'My New Name');
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }));

    // First save presented the version we loaded (1).
    expect(updateProduct).toHaveBeenNthCalledWith(
      1,
      'p1',
      expect.objectContaining({ expectedVersion: 1, name: 'My New Name' }),
    );

    // The conflict banner appears and shows BOTH sides of the diff: my edit and theirs.
    const banner = await screen.findByRole('alert');
    expect(banner).toHaveTextContent('Someone else edited this product');
    expect(within(banner).getByText('My New Name')).toBeInTheDocument();
    expect(within(banner).getByText('Cast Iron Skillet PRO')).toBeInTheDocument();

    // Overwrite: keep my edit, rebase onto their version (2).
    await userEvent.click(
      within(banner).getByRole('button', { name: /Overwrite with mine \(rebase to v2\)/ }),
    );

    expect(updateProduct).toHaveBeenNthCalledWith(
      2,
      'p1',
      expect.objectContaining({ expectedVersion: 2, name: 'My New Name' }),
    );

    // Save confirmed at the new version.
    expect(await screen.findByText(/Saved\. This product is now at version 3\./)).toBeInTheDocument();
  });

  it('reload latest discards my edits and adopts the server version', async () => {
    vi.mocked(listCategories).mockResolvedValue({
      items: [
        { id: 'c1', name: 'Kitchen', slug: 'kitchen', createdAtUtc: '2026-07-16T10:00:00Z' },
      ],
      nextCursor: null,
    });
    vi.mocked(getAdminProduct).mockResolvedValue(productAt(1, 'Cast Iron Skillet'));

    const theirs = productAt(2, 'Cast Iron Skillet PRO');
    const conflictBody = {
      type: 'https://agora.example/problems/conflict',
      title: 'Version conflict',
      status: 409,
      current: theirs,
    } as unknown as ProblemDetails;
    vi.mocked(updateProduct).mockRejectedValueOnce(new ApiError(409, conflictBody));

    renderEdit();

    const nameInput = await screen.findByLabelText('Name');
    await userEvent.clear(nameInput);
    await userEvent.type(nameInput, 'Losing Edit');
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }));

    const banner = await screen.findByRole('alert');
    await userEvent.click(
      within(banner).getByRole('button', { name: /Reload latest/ }),
    );

    // The form remounts to the server's current name; my edit is gone and the banner is dismissed.
    expect(await screen.findByDisplayValue('Cast Iron Skillet PRO')).toBeInTheDocument();
    expect(screen.queryByText('Someone else edited this product')).not.toBeInTheDocument();
  });
});
