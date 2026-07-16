import { useSearchParams } from 'react-router-dom';
import { CategoryChips } from '../features/catalog/components/CategoryChips';
import { ProductGrid } from '../features/catalog/components/ProductGrid';
import { useCategories, useProducts } from '../features/catalog/queries';

export function HomePage() {
  // WHY: the selected category lives in the URL, not component state — a filtered catalog is a
  // shareable, bookmarkable, back-button-able page. URL is the UI state that outlives a render.
  const [searchParams, setSearchParams] = useSearchParams();
  const category = searchParams.get('category');

  const categoriesQuery = useCategories();
  const productsQuery = useProducts(category ?? undefined);

  const selectCategory = (slug: string | null) => {
    setSearchParams(slug ? { category: slug } : {});
  };

  // LEARN: the pages array is the cursor walk made visible — each "Load more" pushes another
  // Page onto it, and we flatten them into one grid. The component holds none of this itself.
  const products = productsQuery.data?.pages.flatMap((page) => page.items) ?? [];

  return (
    <main className="mx-auto max-w-5xl px-4 py-8">
      <header className="mb-6">
        <h1 className="text-2xl font-semibold text-neutral-900">Agora</h1>
        <p className="text-neutral-600">A small shop of well-made things.</p>
      </header>

      {categoriesQuery.data && (
        <div className="mb-6">
          <CategoryChips
            categories={categoriesQuery.data.items}
            selected={category}
            onSelect={selectCategory}
          />
        </div>
      )}

      {productsQuery.isPending ? (
        <p className="text-neutral-500">Loading the catalog…</p>
      ) : productsQuery.isError ? (
        <p role="alert" className="text-red-700">
          We couldn&apos;t load the catalog right now. Please try again.
        </p>
      ) : (
        <>
          <ProductGrid products={products} />

          {productsQuery.hasNextPage && (
            <div className="mt-8 flex justify-center">
              <button
                type="button"
                onClick={() => productsQuery.fetchNextPage()}
                disabled={productsQuery.isFetchingNextPage}
                className="rounded-md border border-neutral-300 px-4 py-2 text-sm font-medium text-neutral-800 transition hover:border-neutral-500 disabled:opacity-50"
              >
                {productsQuery.isFetchingNextPage ? 'Loading…' : 'Load more'}
              </button>
            </div>
          )}
        </>
      )}
    </main>
  );
}
