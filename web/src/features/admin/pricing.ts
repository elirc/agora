import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  createSaleCampaign,
  endSaleCampaign,
  listSaleCampaigns,
  type CreateSaleCampaignRequest,
} from '../../api/client';

// The campaign list for one product. Its own key namespace, keyed by product — a campaign belongs to
// exactly one, and two products' panels must never share a cache entry.
export const campaignsKey = (productId: string) => ['admin', 'campaigns', productId] as const;

export function useSaleCampaigns(productId: string) {
  return useQuery({
    queryKey: campaignsKey(productId),
    queryFn: () => listSaleCampaigns(productId),
  });
}

// WHY both mutations invalidate the CAMPAIGN list and the storefront PRODUCT queries: scheduling a
// sale that starts now, or ending one, changes what a shopper sees on the grid and the product page
// — and those are different cache entries holding the same underlying fact. Invalidate the campaign
// list and leave the catalog stale and the admin sees their change while the storefront tab beside
// them still shows the old price, which looks exactly like a bug in the server.
function useCampaignInvalidation(productId: string) {
  const queryClient = useQueryClient();

  return async () => {
    await queryClient.invalidateQueries({ queryKey: campaignsKey(productId) });
    await queryClient.invalidateQueries({ queryKey: ['products'] });
    await queryClient.invalidateQueries({ queryKey: ['product'] });
  };
}

export function useCreateSaleCampaign(productId: string) {
  const invalidate = useCampaignInvalidation(productId);

  return useMutation({
    mutationFn: (body: CreateSaleCampaignRequest) => createSaleCampaign(productId, body),
    onSuccess: invalidate,
  });
}

export function useEndSaleCampaign(productId: string) {
  const invalidate = useCampaignInvalidation(productId);

  return useMutation({
    // NOTE the error is not swallowed: ending an already-ended campaign is a 422 the panel shows,
    // because "nothing happened and we didn't say so" is the failure mode worth avoiding.
    mutationFn: (campaignId: string) => endSaleCampaign(campaignId),
    onSuccess: invalidate,
  });
}
