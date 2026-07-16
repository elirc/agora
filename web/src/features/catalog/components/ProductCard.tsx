import { Link } from 'react-router-dom';
import type { ProductDto } from '../../../api/client';
import { formatPrice } from '../format';

export function ProductCard({ product }: { product: ProductDto }) {
  return (
    <Link
      to={`/products/${product.slug}`}
      className="flex h-full flex-col justify-between rounded-lg border border-neutral-200 bg-white p-4 transition hover:border-neutral-400 hover:shadow-sm"
    >
      <h3 className="font-medium text-neutral-900">{product.name}</h3>
      <p className="mt-3 text-sm text-neutral-600">
        {formatPrice(product.priceAmount, product.priceCurrency)}
      </p>
    </Link>
  );
}
