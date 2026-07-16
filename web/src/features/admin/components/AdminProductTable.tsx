import { Link } from 'react-router-dom';
import type { AdminProductDto } from '../../../api/client';
import { formatPrice } from '../../catalog/format';

interface AdminProductTableProps {
  products: AdminProductDto[];
  busyId: string | null;
  onArchive: (product: AdminProductDto) => void;
  onRestore: (product: AdminProductDto) => void;
}

export function AdminProductTable({ products, busyId, onArchive, onRestore }: AdminProductTableProps) {
  if (products.length === 0) {
    return <p className="text-neutral-500">No products here.</p>;
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[40rem] border-collapse text-sm">
        <thead>
          <tr className="border-b border-neutral-200 text-left text-neutral-500">
            <th className="py-2 pr-4 font-medium">Name</th>
            <th className="py-2 pr-4 font-medium">SKU</th>
            <th className="py-2 pr-4 font-medium">Price</th>
            <th className="py-2 pr-4 font-medium">Status</th>
            {/* WHY show the version: it is the value the admin's next edit is guarded against, so it
                belongs in the admin's mental model — and it makes the concurrency story visible. */}
            <th className="py-2 pr-4 font-medium">Version</th>
            <th className="py-2 font-medium">Actions</th>
          </tr>
        </thead>
        <tbody>
          {products.map((product) => (
            <tr key={product.id} className="border-b border-neutral-100">
              <td className="py-2 pr-4 font-medium text-neutral-900">{product.name}</td>
              <td className="py-2 pr-4 font-mono text-neutral-600">{product.sku}</td>
              <td className="py-2 pr-4 text-neutral-700">
                {formatPrice(product.priceAmount, product.priceCurrency)}
              </td>
              <td className="py-2 pr-4">
                <StatusChip archived={product.isArchived} />
              </td>
              <td className="py-2 pr-4 font-mono text-neutral-500">{String(product.version)}</td>
              <td className="py-2">
                <div className="flex gap-3">
                  <Link to={`/admin/products/${product.id}`} className="text-neutral-700 underline">
                    Edit
                  </Link>
                  {product.isArchived ? (
                    <button
                      type="button"
                      onClick={() => onRestore(product)}
                      disabled={busyId === product.id}
                      className="text-emerald-700 underline disabled:opacity-50"
                    >
                      Restore
                    </button>
                  ) : (
                    <button
                      type="button"
                      onClick={() => onArchive(product)}
                      disabled={busyId === product.id}
                      className="text-red-700 underline disabled:opacity-50"
                    >
                      Archive
                    </button>
                  )}
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function StatusChip({ archived }: { archived: boolean }) {
  return archived ? (
    <span className="rounded-full bg-neutral-200 px-2 py-0.5 text-xs font-medium text-neutral-600">
      Archived
    </span>
  ) : (
    <span className="rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-medium text-emerald-700">
      Active
    </span>
  );
}
