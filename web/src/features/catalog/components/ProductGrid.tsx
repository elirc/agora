import type { ProductDto } from '../../../api/client';
import { ProductCard } from './ProductCard';

export function ProductGrid({ products }: { products: ProductDto[] }) {
  if (products.length === 0) {
    return <p className="text-neutral-500">No products here yet.</p>;
  }

  return (
    <ul className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
      {products.map((product) => (
        <li key={product.id}>
          <ProductCard product={product} />
        </li>
      ))}
    </ul>
  );
}
