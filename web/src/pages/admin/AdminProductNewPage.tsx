import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import type { CreateProductRequest } from '../../api/client';
import { ProductForm } from '../../features/admin/components/ProductForm';
import { useCreateProduct } from '../../features/admin/mutations';
import { mapServerErrors, type MappedServerErrors } from '../../features/admin/serverErrors';
import type { ProductFormValues } from '../../features/admin/validation';
import { useCategories } from '../../features/catalog/queries';

const EMPTY: ProductFormValues = {
  categoryId: '',
  name: '',
  slug: '',
  sku: '',
  description: '',
  priceAmount: '',
  priceCurrency: 'USD',
};

export function AdminProductNewPage() {
  const categoriesQuery = useCategories();
  const create = useCreateProduct();
  const navigate = useNavigate();
  const [serverErrors, setServerErrors] = useState<MappedServerErrors>({ fields: {}, form: null });

  const handleSubmit = (values: ProductFormValues) => {
    setServerErrors({ fields: {}, form: null });
    const body: CreateProductRequest = {
      categoryId: values.categoryId,
      name: values.name,
      // A blank slug tells the server to generate one from the name.
      slug: values.slug.trim() === '' ? null : values.slug,
      sku: values.sku,
      description: values.description,
      // The price stays a string on the wire — no float coercion (see validation.ts).
      priceAmount: values.priceAmount as unknown as number,
      priceCurrency: values.priceCurrency,
    };
    create.mutate(body, {
      onSuccess: (product) => navigate(`/admin/products/${product.id}`),
      onError: (error) => setServerErrors(mapServerErrors(error)),
    });
  };

  return (
    <main className="mx-auto max-w-2xl px-4 py-8">
      <Link to="/admin/products" className="text-sm text-neutral-600 underline">
        ← Back to products
      </Link>
      <h1 className="mt-4 text-2xl font-semibold text-neutral-900">New product</h1>

      {serverErrors.form && (
        <p role="alert" className="mt-4 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">
          {serverErrors.form}
        </p>
      )}

      <div className="mt-6">
        {categoriesQuery.isPending ? (
          <p className="text-neutral-500">Loading categories…</p>
        ) : (
          <ProductForm
            mode="create"
            categories={categoriesQuery.data?.items ?? []}
            initial={EMPTY}
            submitting={create.isPending}
            submitLabel="Create product"
            serverErrors={serverErrors.fields}
            onSubmit={handleSubmit}
          />
        )}
      </div>
    </main>
  );
}
