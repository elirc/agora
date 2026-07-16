import { useQuery } from '@tanstack/react-query';
import { getStock, listLowStock } from '../../api/client';

/**
 * How many of a product a shopper could claim right now.
 */
// ⚠️ `staleTime: 0` AND `refetchOnWindowFocus` — the opposite of what every other query in this app
// wants, and the reason is the sprint's whole point.
//
// A product's NAME does not change while you are looking at it, so the catalog is happily cached. A
// stock count changes because of what other people are doing, second by second, and a shopper staring
// at a stale "3 left" is being told a comfortable lie by our cache rather than by our server.
//
// So this query treats its answer as expired the moment it arrives, and refetches when the tab
// regains focus — because "I opened this tab an hour ago" is the single most common way to see an
// old number. That is more requests, on purpose. See ADR-0008.
export function useStock(productId: string | undefined) {
  return useQuery({
    queryKey: ['stock', productId],
    queryFn: () => getStock(productId!),
    enabled: productId !== undefined,
    staleTime: 0,
    refetchOnWindowFocus: true,
  });
}

/** The shop's own view: what is running out. */
export function useLowStock(threshold?: number) {
  return useQuery({
    queryKey: ['low-stock', threshold ?? 'default'],
    queryFn: () => listLowStock(threshold),
    staleTime: 0,
  });
}
