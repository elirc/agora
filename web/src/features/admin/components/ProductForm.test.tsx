import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { CategoryDto } from '../../../api/client';
import { ProductForm } from './ProductForm';
import type { ProductFormValues } from '../validation';

const categories: CategoryDto[] = [
  { id: 'c1', name: 'Kitchen', slug: 'kitchen', createdAtUtc: '2026-07-16T10:00:00Z' },
];

const EMPTY: ProductFormValues = {
  categoryId: '',
  name: '',
  slug: '',
  sku: '',
  description: '',
  priceAmount: '',
  priceCurrency: 'USD',
};

describe('ProductForm validation', () => {
  afterEach(() => vi.clearAllMocks());

  it('blocks submit and shows per-field errors when required fields are empty', async () => {
    const onSubmit = vi.fn();
    render(
      <ProductForm
        mode="create"
        categories={categories}
        initial={EMPTY}
        submitting={false}
        submitLabel="Create product"
        onSubmit={onSubmit}
      />,
    );

    await userEvent.click(screen.getByRole('button', { name: 'Create product' }));

    // Client-side validation caught the empty fields — nothing hit the network.
    expect(onSubmit).not.toHaveBeenCalled();
    expect(screen.getByText('Name is required.')).toBeInTheDocument();
    expect(screen.getByText('SKU is required.')).toBeInTheDocument();
    expect(screen.getByText('Price is required.')).toBeInTheDocument();
    expect(screen.getByText('Choose a category.')).toBeInTheDocument();
  });

  it('rejects a badly-shaped price without doing float math', async () => {
    const onSubmit = vi.fn();
    render(
      <ProductForm
        mode="create"
        categories={categories}
        initial={{ ...EMPTY, categoryId: 'c1', name: 'Skillet', sku: 'AGA-KIT-9', priceAmount: '19.999' }}
        submitting={false}
        submitLabel="Create product"
        onSubmit={onSubmit}
      />,
    );

    await userEvent.click(screen.getByRole('button', { name: 'Create product' }));

    expect(onSubmit).not.toHaveBeenCalled();
    expect(screen.getByText(/Enter a price like 19\.99/)).toBeInTheDocument();
  });

  it('surfaces a server field error (a mapped 422) alongside the field', async () => {
    const onSubmit = vi.fn();
    render(
      <ProductForm
        mode="create"
        categories={categories}
        initial={{ ...EMPTY, categoryId: 'c1', name: 'Skillet', sku: 'AGA-KIT-1', priceAmount: '19.99' }}
        submitting={false}
        submitLabel="Create product"
        serverErrors={{ sku: "A product with the SKU 'AGA-KIT-1' already exists." }}
        onSubmit={onSubmit}
      />,
    );

    expect(
      screen.getByText("A product with the SKU 'AGA-KIT-1' already exists."),
    ).toBeInTheDocument();
  });

  it('submits the typed values when the form is valid', async () => {
    const onSubmit = vi.fn();
    render(
      <ProductForm
        mode="create"
        categories={categories}
        initial={{
          ...EMPTY,
          categoryId: 'c1',
          name: 'Cast Iron Skillet',
          sku: 'AGA-KIT-2',
          priceAmount: '34.50',
        }}
        submitting={false}
        submitLabel="Create product"
        onSubmit={onSubmit}
      />,
    );

    await userEvent.click(screen.getByRole('button', { name: 'Create product' }));

    expect(onSubmit).toHaveBeenCalledWith(
      expect.objectContaining({ name: 'Cast Iron Skillet', priceAmount: '34.50', sku: 'AGA-KIT-2' }),
    );
  });
});
