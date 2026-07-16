import { Link } from 'react-router-dom';
import type { ProductDto } from '../../../api/client';
import { PriceDisplay } from './PriceDisplay';

export function ProductCard({ product }: { product: ProductDto }) {
  return (
    <Link
      to={`/products/${product.slug}`}
      className="flex h-full flex-col justify-between rounded-lg border border-neutral-200 bg-white p-4 transition hover:border-neutral-400 hover:shadow-sm"
    >
      <h3 className="font-medium text-neutral-900">{product.name}</h3>

      {/* S04: this card used to call formatPrice itself. It now asks a component that knows what a
          sale looks like, so the grid and the product page cannot drift into rendering prices two
          different ways — the bug where a card says $40 and the page you land on says $50. */}
      <div className="mt-3">
        <PriceDisplay product={product} />
      </div>
    </Link>
  );
}
