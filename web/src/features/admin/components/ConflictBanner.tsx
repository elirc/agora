import type { AdminProductDto, CategoryDto } from '../../../api/client';
import { formatPrice } from '../../catalog/format';
import type { ProductFormValues } from '../validation';

// 🔗 THE POINT OF THE 409. A conflict the UI swallows is worse than no concurrency control at all:
// the admin would think their save worked. So when the server rejects a stale write, we do NOT just
// show "try again" — we show, field by field, what the OTHER admin changed (from the 409's current
// state) next to what THIS admin typed, then let them choose. Reload discards their edits for the
// latest; Overwrite keeps their edits but rebases onto the current version so the re-save is legal.
interface ConflictBannerProps {
  current: AdminProductDto;
  mine: ProductFormValues;
  categories: CategoryDto[];
  onReload: () => void;
  onOverwrite: () => void;
  overwriting: boolean;
}

interface DiffRow {
  label: string;
  mine: string;
  theirs: string;
}

export function ConflictBanner({
  current,
  mine,
  categories,
  onReload,
  onOverwrite,
  overwriting,
}: ConflictBannerProps) {
  const categoryName = (id: string) => categories.find((c) => c.id === id)?.name ?? id;

  const rows: DiffRow[] = [
    { label: 'Name', mine: mine.name, theirs: current.name },
    { label: 'Description', mine: mine.description, theirs: current.description },
    {
      label: 'Price',
      mine: formatPrice(mine.priceAmount, mine.priceCurrency || current.priceCurrency),
      theirs: formatPrice(current.priceAmount, current.priceCurrency),
    },
    { label: 'Category', mine: categoryName(mine.categoryId), theirs: categoryName(current.categoryId) },
  ].filter((row) => row.mine !== row.theirs);

  return (
    <section
      role="alert"
      className="mb-6 rounded-lg border border-amber-300 bg-amber-50 p-4 text-amber-900"
    >
      <h2 className="text-base font-semibold">Someone else edited this product</h2>
      <p className="mt-1 text-sm">
        Your changes were not saved — another admin saved first (their version is{' '}
        <span className="font-mono">{String(current.version)}</span>, you were editing an older one).
        Compare below, then choose.
      </p>

      {rows.length > 0 ? (
        <div className="mt-3 overflow-x-auto">
          <table className="w-full min-w-[28rem] border-collapse text-sm">
            <thead>
              <tr className="text-left text-amber-800">
                <th className="py-1 pr-4 font-medium">Field</th>
                <th className="py-1 pr-4 font-medium">Your edit</th>
                <th className="py-1 font-medium">Current (theirs)</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.label} className="border-t border-amber-200 align-top">
                  <td className="py-1 pr-4 font-medium">{row.label}</td>
                  <td className="py-1 pr-4">{row.mine || <em className="text-amber-700">(empty)</em>}</td>
                  <td className="py-1">{row.theirs || <em className="text-amber-700">(empty)</em>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <p className="mt-3 text-sm">
          Your edits match the current values — reloading is enough to clear this.
        </p>
      )}

      <div className="mt-4 flex flex-wrap gap-3">
        <button
          type="button"
          onClick={onReload}
          className="rounded-md border border-amber-400 bg-white px-3 py-1.5 text-sm font-medium text-amber-900 hover:bg-amber-100"
        >
          Reload latest (discard my edits)
        </button>
        <button
          type="button"
          onClick={onOverwrite}
          disabled={overwriting}
          className="rounded-md bg-amber-700 px-3 py-1.5 text-sm font-medium text-white hover:bg-amber-800 disabled:opacity-50"
        >
          {overwriting ? 'Saving…' : `Overwrite with mine (rebase to v${String(current.version)})`}
        </button>
      </div>
    </section>
  );
}
