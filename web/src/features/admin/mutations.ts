import { useMutation, useQueryClient } from '@tanstack/react-query';
import {
  archiveProduct,
  createProduct,
  restoreProduct,
  updateProduct,
  type AdminProductDto,
  type CreateProductRequest,
  type UpdateProductRequest,
} from '../../api/client';
import { adminProductsKey } from './queries';

// WHY invalidate ['admin','products'] on every success: a create adds a row, an edit changes one,
// archive/restore move a row between the active/archived tabs — all of which the cached list must
// re-fetch to reflect. We invalidate the whole namespace so every status tab refreshes.
function useAdminInvalidation() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: adminProductsKey });
}

export function useCreateProduct() {
  const invalidate = useAdminInvalidation();
  return useMutation({
    mutationFn: (body: CreateProductRequest) => createProduct(body),
    onSuccess: invalidate,
  });
}

export function useUpdateProduct() {
  const invalidate = useAdminInvalidation();
  return useMutation({
    // NOTE: the error is NOT swallowed here — a 409 conflict propagates to the caller so the edit
    // page can render the conflict banner with the server's current state. onSuccess only fires on
    // an accepted write; a conflict is a rejected write the UI must handle, not a silent failure.
    mutationFn: ({ id, body }: { id: string; body: UpdateProductRequest }) =>
      updateProduct(id, body),
    onSuccess: invalidate,
  });
}

export function useArchiveProduct() {
  const invalidate = useAdminInvalidation();
  return useMutation({
    mutationFn: ({ id, expectedVersion }: { id: string; expectedVersion: number }) =>
      archiveProduct(id, expectedVersion),
    onSuccess: invalidate,
  });
}

export function useRestoreProduct() {
  const invalidate = useAdminInvalidation();
  return useMutation({
    mutationFn: ({ id, expectedVersion }: { id: string; expectedVersion: number }) =>
      restoreProduct(id, expectedVersion),
    onSuccess: invalidate,
  });
}

// Coerce the version field, which the generated type widens to `number | string` (the OpenAPI
// document types an int32 as number-or-string). The wire is honest; we normalise at the seam.
export function versionOf(product: AdminProductDto): number {
  return Number(product.version);
}
