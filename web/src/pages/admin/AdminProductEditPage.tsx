import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import {
  ApiError,
  conflictState,
  type AdminProductDto,
  type UpdateProductRequest,
} from '../../api/client';
import { ConflictBanner } from '../../features/admin/components/ConflictBanner';
import { ProductForm } from '../../features/admin/components/ProductForm';
import { SaleCampaignPanel } from '../../features/admin/components/SaleCampaignPanel';
import { useUpdateProduct, versionOf } from '../../features/admin/mutations';
import { useAdminProduct } from '../../features/admin/queries';
import { mapServerErrors, type MappedServerErrors } from '../../features/admin/serverErrors';
import type { ProductFormValues } from '../../features/admin/validation';
import { useCategories } from '../../features/catalog/queries';

function toFormValues(dto: AdminProductDto): ProductFormValues {
  return {
    categoryId: dto.categoryId,
    name: dto.name,
    slug: dto.slug,
    sku: dto.sku,
    description: dto.description,
    priceAmount: String(dto.priceAmount),
    priceCurrency: dto.priceCurrency,
  };
}

export function AdminProductEditPage() {
  const { id = '' } = useParams<{ id: string }>();
  const productQuery = useAdminProduct(id);
  const categoriesQuery = useCategories();
  const update = useUpdateProduct();

  // `baseline` is the server state the form is editing FROM — its version is what our next save is
  // guarded against. It starts as the loaded product and moves forward on a successful save or when
  // the admin reloads the latest during a conflict.
  const [baseline, setBaseline] = useState<AdminProductDto | null>(null);
  const [conflict, setConflict] = useState<AdminProductDto | null>(null);
  const [mine, setMine] = useState<ProductFormValues | null>(null);
  const [serverErrors, setServerErrors] = useState<MappedServerErrors>({ fields: {}, form: null });
  const [saved, setSaved] = useState(false);

  // Seed the baseline once the product loads (or if the id changes to a different product).
  const loaded = productQuery.data;
  if (loaded && (baseline === null || baseline.id !== loaded.id)) {
    setBaseline(loaded);
  }

  if (productQuery.isPending || categoriesQuery.isPending) {
    return <Shell><p className="text-neutral-500">Loading…</p></Shell>;
  }

  if (productQuery.isError || !baseline) {
    const notFound = productQuery.error instanceof ApiError && productQuery.error.isNotFound;
    return (
      <Shell>
        <p role="alert" className="text-red-700">
          {notFound ? 'That product no longer exists.' : 'We couldn’t load this product.'}
        </p>
      </Shell>
    );
  }

  const categories = categoriesQuery.data?.items ?? [];

  const save = (values: ProductFormValues, expectedVersion: number) => {
    setSaved(false);
    setServerErrors({ fields: {}, form: null });
    setMine(values);

    const body: UpdateProductRequest = {
      categoryId: values.categoryId,
      name: values.name,
      description: values.description,
      priceAmount: values.priceAmount as unknown as number, // decimal string, sent verbatim
      priceCurrency: values.priceCurrency,
      expectedVersion,
    };

    update.mutate(
      { id, body },
      {
        onSuccess: (updated) => {
          // Accepted: adopt the new server state (its version is our new baseline) and clear conflict.
          setBaseline(updated);
          setConflict(null);
          setMine(null);
          setSaved(true);
        },
        onError: (error) => {
          const current = conflictState(error);
          if (current) {
            // Rejected as stale: keep the admin's edits on screen and show what the other admin did.
            setConflict(current);
          } else {
            setServerErrors(mapServerErrors(error));
          }
        },
      },
    );
  };

  return (
    <Shell>
      <h1 className="mt-4 text-2xl font-semibold text-neutral-900">Edit product</h1>

      {saved && (
        <p role="status" className="mt-4 rounded-md bg-emerald-50 px-3 py-2 text-sm text-emerald-800">
          Saved. This product is now at version {String(baseline.version)}.
        </p>
      )}

      {conflict && mine && (
        <ConflictBanner
          current={conflict}
          mine={mine}
          categories={categories}
          overwriting={update.isPending}
          onReload={() => {
            // Discard my edits, adopt theirs: the form remounts (keyed on version) to the latest.
            setBaseline(conflict);
            setConflict(null);
            setMine(null);
          }}
          onOverwrite={() => {
            // Keep my edits but REBASE onto their version so the re-save is legal. This is a genuine
            // overwrite: the other admin's change is replaced — the admin chose that, eyes open,
            // having just seen the diff. That informed choice is the whole point of the 409 UX.
            save(mine, versionOf(conflict));
          }}
        />
      )}

      {serverErrors.form && (
        <p role="alert" className="mt-4 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">
          {serverErrors.form}
        </p>
      )}

      <div className="mt-6">
        <ProductForm
          // Remount when the baseline version changes (successful save or reload) so the form's
          // initial values track the server. During a conflict the baseline is unchanged, so the
          // form keeps the admin's in-progress edits.
          key={`${baseline.id}:${String(baseline.version)}`}
          mode="edit"
          categories={categories}
          initial={toFormValues(baseline)}
          submitting={update.isPending}
          submitLabel="Save changes"
          serverErrors={serverErrors.fields}
          onSubmit={(values) => save(values, versionOf(baseline))}
        />
      </div>

      {/* 🔗 pricing lives on the product's own page, below the form, because a sale is a fact ABOUT
          this product — not a separate thing an admin navigates to and re-finds the product from.
          Note it is NOT part of the form: scheduling a sale is its own request with its own success
          and its own failure, and folding it into the product's optimistic-concurrency save would
          mean a stale name could reject a perfectly good sale. Different facts, different writes. */}
      <SaleCampaignPanel
        productId={baseline.id}
        priceAmount={baseline.priceAmount}
        priceCurrency={baseline.priceCurrency}
      />
    </Shell>
  );
}

function Shell({ children }: { children: React.ReactNode }) {
  return (
    <main className="mx-auto max-w-2xl px-4 py-8">
      <Link to="/admin/products" className="text-sm text-neutral-600 underline">
        ← Back to products
      </Link>
      {children}
    </main>
  );
}
