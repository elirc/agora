import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { getAdminProduct, listAdminProducts, type ProductStatus } from '../../api/client';

// LEARN: the admin product list is its own query key namespace (['admin','products', status]) — it
// must never share a cache entry with the public storefront list, because it returns DIFFERENT rows
// (archived included) and a different DTO (with version). Same cursor-walk shape, different audience.
const PAGE_SIZE = 20;

export function useAdminProducts(status: ProductStatus) {
  return useInfiniteQuery({
    queryKey: ['admin', 'products', status],
    queryFn: ({ pageParam }) =>
      listAdminProducts({ status, cursor: pageParam ?? undefined, limit: PAGE_SIZE }),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage) => lastPage.nextCursor,
  });
}

export function useAdminProduct(id: string) {
  return useQuery({
    queryKey: ['admin', 'product', id],
    queryFn: () => getAdminProduct(id),
    // A 404 (deleted/never-existed) is an answer, not a transient failure — don't retry it.
    retry: false,
  });
}

/** Every admin product query, for invalidation after a create/update/archive/restore. */
export const adminProductsKey = ['admin', 'products'] as const;
