import { useState } from 'react';
import { Link } from 'react-router-dom';
import { toInt } from '../../features/catalog/format';
import { useLowStock } from '../../features/inventory/queries';

/** The shop's own view: what is running out, and why. */
export function AdminStockPage() {
  const [threshold, setThreshold] = useState(3);
  const { data: lines, isPending, isError } = useLowStock(threshold);

  return (
    <main className="mx-auto max-w-4xl px-4 py-8">
      <div className="flex items-baseline justify-between">
        <h1 className="text-2xl font-semibold text-neutral-900">Running low</h1>
        <Link to="/admin/products" className="text-sm text-neutral-600 underline">
          Catalog
        </Link>
      </div>

      <label className="mt-4 flex items-center gap-2 text-sm text-neutral-600">
        Show products with
        <input
          type="number"
          min={0}
          value={threshold}
          onChange={(event) => setThreshold(Math.max(0, Number(event.target.value)))}
          className="w-20 rounded border border-neutral-300 px-2 py-1"
          aria-label="Stock threshold"
        />
        or fewer available
      </label>

      {isPending && <p className="mt-8 text-neutral-500">Counting…</p>}
      {isError && (
        <p className="mt-8 text-red-700" role="alert">
          Couldn’t load stock levels.
        </p>
      )}

      {lines && lines.length === 0 && (
        <p className="mt-8 text-neutral-500">Nothing is running low. Good.</p>
      )}

      {lines && lines.length > 0 && (
        <table className="mt-6 w-full text-left text-sm">
          <thead className="border-b border-neutral-200 text-neutral-500">
            <tr>
              <th className="py-2 font-medium">Product</th>
              <th className="py-2 font-medium">SKU</th>
              {/* 📘 THREE COLUMNS, NOT ONE, and this is the whole reason the view exists. "2
                  available" is an emergency if two are on the shelf and a non-event if nine are on
                  the shelf with seven in people's carts — same number, opposite actions. Showing
                  only availability sends someone to the stockroom to count skillets that are already
                  there. When a figure is DERIVED and a human will act on it, show the inputs. */}
              <th className="py-2 text-right font-medium">On hand</th>
              <th className="py-2 text-right font-medium">In carts</th>
              <th className="py-2 text-right font-medium">Available</th>
            </tr>
          </thead>
          <tbody>
            {lines.map((line) => {
              const available = toInt(line.available);
              return (
              <tr key={line.productId} className="border-b border-neutral-100">
                <td className="py-2 text-neutral-900">{line.name}</td>
                <td className="py-2 font-mono text-xs text-neutral-500">{line.sku}</td>
                <td className="py-2 text-right tabular-nums text-neutral-700">{toInt(line.onHand)}</td>
                <td className="py-2 text-right tabular-nums text-neutral-700">{toInt(line.reserved)}</td>
                <td
                  className={`py-2 text-right tabular-nums font-medium ${
                    available === 0 ? 'text-red-700' : 'text-amber-700'
                  }`}
                >
                  {available}
                </td>
              </tr>
              );
            })}
          </tbody>
        </table>
      )}

      {/* The honest footnote. Every number above was true when the page loaded and is a read of
          something other people are moving. Saying so costs one line and stops somebody treating a
          report as an inventory. */}
      <p className="mt-6 text-xs text-neutral-400">
        Counts are as of when this page loaded. Reservations expire on their own.
      </p>
    </main>
  );
}
