import { Link, useParams } from 'react-router-dom';
import { ApiError } from '../api/client';
import { formatPrice } from '../features/catalog/format';
import { useProduct } from '../features/catalog/queries';

export function ProductPage() {
  const { slug } = useParams<{ slug: string }>();
  const query = useProduct(slug ?? '');

  if (query.isPending) {
    return (
      <main className="mx-auto max-w-3xl px-4 py-8">
        <p className="text-neutral-500">Loading…</p>
      </main>
    );
  }

  if (query.isError) {
    // WHY: distinguish "this product does not exist" (a 404, an expected outcome) from a real
    // failure. The typed ApiError carries the status the server promised, so the UI can keep the
    // API's promise instead of guessing.
    const notFound = query.error instanceof ApiError && query.error.isNotFound;

    return (
      <main className="mx-auto max-w-3xl px-4 py-8" role="alert">
        <h1 className="text-xl font-semibold text-neutral-900">
          {notFound ? 'Product not found' : 'Something went wrong'}
        </h1>
        <p className="mt-2 text-neutral-600">
          {notFound
            ? 'This product may have been removed, or the link is wrong.'
            : 'We couldn’t load this product right now. Please try again.'}
        </p>
        <Link to="/" className="mt-4 inline-block text-sm text-neutral-800 underline">
          Back to the catalog
        </Link>
      </main>
    );
  }

  const product = query.data;

  return (
    <main className="mx-auto max-w-3xl px-4 py-8">
      <Link to="/" className="text-sm text-neutral-600 underline">
        ← Back to the catalog
      </Link>

      <h1 className="mt-4 text-2xl font-semibold text-neutral-900">{product.name}</h1>
      <p className="mt-2 text-lg text-neutral-800">
        {formatPrice(product.priceAmount, product.priceCurrency)}
      </p>
      <p className="mt-4 whitespace-pre-line text-neutral-700">{product.description}</p>
      <p className="mt-6 text-xs uppercase tracking-wide text-neutral-400">SKU {product.sku}</p>
    </main>
  );
}
