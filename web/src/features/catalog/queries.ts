import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { getProduct, listCategories, listProducts } from '../../api/client';

// LEARN: server state lives in TanStack Query, never in component useState. These hooks are the
// only thing components import to read data — they own caching, loading/error flags, and (for
// products) the cursor walk. Notice how little state the components below end up owning.
const PAGE_SIZE = 8;

export function useCategories() {
  return useQuery({
    queryKey: ['categories'],
    queryFn: () => listCategories({ limit: 50 }),
  });
}

export function useProducts(category?: string) {
  return useInfiniteQuery({
    // WHY: the category is part of the key — switch the filter and Query treats it as a distinct
    // list with its own pages and cache, so "Load more" never bleeds across filters.
    queryKey: ['products', category ?? 'all'],
    queryFn: ({ pageParam }) =>
      listProducts({ category, cursor: pageParam ?? undefined, limit: PAGE_SIZE }),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage) => lastPage.nextCursor,
  });
}

export function useProduct(slug: string) {
  return useQuery({
    queryKey: ['product', slug],
    queryFn: () => getProduct(slug),
    // WHY: a 404 is an answer, not a transient failure — don't retry it three times before
    // showing the not-found state.
    retry: false,
  });
}
