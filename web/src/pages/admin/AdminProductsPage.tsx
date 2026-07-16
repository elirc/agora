import { useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { isConflict, type AdminProductDto, type ProductStatus } from '../../api/client';
import { AdminProductTable } from '../../features/admin/components/AdminProductTable';
import { useArchiveProduct, useRestoreProduct, versionOf } from '../../features/admin/mutations';
import { useAdminProducts } from '../../features/admin/queries';

const TABS: ReadonlyArray<{ value: ProductStatus; label: string }> = [
  { value: 'all', label: 'All' },
  { value: 'active', label: 'Active' },
  { value: 'archived', label: 'Archived' },
];

export function AdminProductsPage() {
  const [params, setParams] = useSearchParams();
  const status = (params.get('status') as ProductStatus | null) ?? 'all';

  const query = useAdminProducts(status);
  const archive = useArchiveProduct();
  const restore = useRestoreProduct();
  const [notice, setNotice] = useState<string | null>(null);

  const busyId = archive.isPending
    ? archive.variables?.id ?? null
    : restore.isPending
      ? restore.variables?.id ?? null
      : null;

  const products = query.data?.pages.flatMap((page) => page.items) ?? [];

  // WHY archive/restore surface a lighter conflict message than the edit form's banner: there is no
  // "your edits" to diff for a one-click toggle. On a 409 we just tell the admin the row moved and
  // refetch — the table (with fresh versions) is the recovery. The edit form is where the full
  // rebase-or-discard choice lives.
  const runAction = (
    mutate: (
      vars: { id: string; expectedVersion: number },
      opts: { onSuccess: () => void; onError: (e: unknown) => void },
    ) => void,
    product: AdminProductDto,
    verb: string,
  ) => {
    setNotice(null);
    mutate(
      { id: product.id, expectedVersion: versionOf(product) },
      {
        onSuccess: () => setNotice(`“${product.name}” ${verb}.`),
        onError: (error) => {
          if (isConflict(error)) {
            setNotice(
              `“${product.name}” was changed by someone else — the list has been refreshed. Try again.`,
            );
            void query.refetch();
          } else {
            setNotice(`Could not ${verb.replace(/d$/, '')} “${product.name}”.`);
          }
        },
      },
    );
  };

  return (
    <main className="mx-auto max-w-5xl px-4 py-8">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold text-neutral-900">Catalog · Products</h1>
        <Link
          to="/admin/products/new"
          className="rounded-md bg-neutral-900 px-3 py-1.5 text-sm font-medium text-white hover:bg-neutral-700"
        >
          New product
        </Link>
      </div>

      <div className="mt-6 flex gap-2" role="group" aria-label="Filter by status">
        {TABS.map((tab) => (
          <button
            key={tab.value}
            type="button"
            onClick={() => setParams(tab.value === 'all' ? {} : { status: tab.value })}
            className={[
              'rounded-full border px-3 py-1 text-sm transition',
              status === tab.value
                ? 'border-neutral-900 bg-neutral-900 text-white'
                : 'border-neutral-300 text-neutral-700 hover:border-neutral-500',
            ].join(' ')}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {notice && (
        <p role="status" className="mt-4 rounded-md bg-neutral-100 px-3 py-2 text-sm text-neutral-700">
          {notice}
        </p>
      )}

      <div className="mt-6">
        {query.isPending ? (
          <p className="text-neutral-500">Loading products…</p>
        ) : query.isError ? (
          <p role="alert" className="text-red-700">
            We couldn’t load the catalog. Please try again.
          </p>
        ) : (
          <>
            <AdminProductTable
              products={products}
              busyId={busyId}
              onArchive={(p) => runAction(archive.mutate, p, 'archived')}
              onRestore={(p) => runAction(restore.mutate, p, 'restored')}
            />

            {query.hasNextPage && (
              <div className="mt-6 flex justify-center">
                <button
                  type="button"
                  onClick={() => query.fetchNextPage()}
                  disabled={query.isFetchingNextPage}
                  className="rounded-md border border-neutral-300 px-4 py-2 text-sm font-medium text-neutral-800 hover:border-neutral-500 disabled:opacity-50"
                >
                  {query.isFetchingNextPage ? 'Loading…' : 'Load more'}
                </button>
              </div>
            )}
          </>
        )}
      </div>
    </main>
  );
}
