import { useState } from 'react';
import type { CategoryDto } from '../../../api/client';
import { slugPreview, validateProduct, type FieldErrors, type ProductFormValues } from '../validation';

interface ProductFormProps {
  mode: 'create' | 'edit';
  categories: CategoryDto[];
  initial: ProductFormValues;
  submitting: boolean;
  submitLabel: string;
  // A server-side field error (e.g. a 422 the client didn't catch), keyed by field name.
  serverErrors?: FieldErrors;
  onSubmit: (values: ProductFormValues) => void;
}

// The form OWNS its field state and its client-side validation; the page owns the mutation and the
// conflict UX. On submit we validate locally first — a failing check never reaches the network — but
// the server re-checks everything and its errors (serverErrors) render right beside the client ones.
export function ProductForm({
  mode,
  categories,
  initial,
  submitting,
  submitLabel,
  serverErrors,
  onSubmit,
}: ProductFormProps) {
  const [values, setValues] = useState<ProductFormValues>(initial);
  const [errors, setErrors] = useState<FieldErrors>({});

  const isCreate = mode === 'create';

  const set = (field: keyof ProductFormValues, value: string) =>
    setValues((v) => ({ ...v, [field]: value }));

  const handleSubmit = (event: React.FormEvent) => {
    event.preventDefault();
    const found = validateProduct(values, isCreate);
    setErrors(found);
    if (Object.keys(found).length === 0) {
      onSubmit(values);
    }
  };

  // Client errors take precedence (they're instant); fall back to any server error for the field.
  const errorFor = (field: keyof ProductFormValues) => errors[field] ?? serverErrors?.[field];

  const fieldClass = (field: keyof ProductFormValues) =>
    [
      'mt-1 w-full rounded-md border px-3 py-2',
      errorFor(field) ? 'border-red-400' : 'border-neutral-300',
    ].join(' ');

  return (
    <form className="space-y-4" onSubmit={handleSubmit} noValidate>
      <Field label="Category" error={errorFor('categoryId')}>
        <select
          aria-label="Category"
          value={values.categoryId}
          onChange={(e) => set('categoryId', e.target.value)}
          className={fieldClass('categoryId')}
        >
          <option value="">Choose a category…</option>
          {categories.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </select>
      </Field>

      <Field label="Name" error={errorFor('name')}>
        <input
          aria-label="Name"
          value={values.name}
          onChange={(e) => set('name', e.target.value)}
          className={fieldClass('name')}
        />
      </Field>

      {isCreate && (
        <>
          <Field
            label="Slug (optional)"
            error={errorFor('slug')}
            hint={
              values.slug.trim()
                ? undefined
                : `Leave blank to generate: ${slugPreview(values.name) || '—'}`
            }
          >
            <input
              aria-label="Slug"
              value={values.slug}
              onChange={(e) => set('slug', e.target.value)}
              className={fieldClass('slug')}
            />
          </Field>

          <Field label="SKU" error={errorFor('sku')}>
            <input
              aria-label="SKU"
              value={values.sku}
              onChange={(e) => set('sku', e.target.value)}
              className={fieldClass('sku')}
            />
          </Field>
        </>
      )}

      {!isCreate && (
        // WHY the slug and SKU are shown but NOT editable: they are immutable identifiers (permalink
        // and warehouse code). Displaying them read-only tells the admin they exist and why they're
        // fixed, instead of pretending they were never there.
        <div className="grid grid-cols-2 gap-4 text-sm text-neutral-500">
          <p>
            Slug: <span className="font-mono text-neutral-700">{values.slug}</span>{' '}
            <span className="text-neutral-400">(permalink — fixed)</span>
          </p>
          <p>
            SKU: <span className="font-mono text-neutral-700">{values.sku}</span>{' '}
            <span className="text-neutral-400">(fixed)</span>
          </p>
        </div>
      )}

      <Field label="Description" error={errorFor('description')}>
        <textarea
          aria-label="Description"
          rows={4}
          value={values.description}
          onChange={(e) => set('description', e.target.value)}
          className={fieldClass('description')}
        />
      </Field>

      <div className="grid grid-cols-2 gap-4">
        <Field label="Price" error={errorFor('priceAmount')}>
          {/* WHY type=text, not number: the price is a decimal STRING sent verbatim to the server —
              a number input coerces through a JS float and can lose a cent. inputMode gives phones a
              numeric keypad without the coercion. */}
          <input
            aria-label="Price"
            type="text"
            inputMode="decimal"
            value={values.priceAmount}
            onChange={(e) => set('priceAmount', e.target.value)}
            className={fieldClass('priceAmount')}
          />
        </Field>

        <Field label="Currency" error={errorFor('priceCurrency')}>
          <input
            aria-label="Currency"
            value={values.priceCurrency}
            onChange={(e) => set('priceCurrency', e.target.value.toUpperCase())}
            maxLength={3}
            className={fieldClass('priceCurrency')}
          />
        </Field>
      </div>

      <button
        type="submit"
        disabled={submitting}
        className="rounded-md bg-neutral-900 px-4 py-2 text-sm font-medium text-white disabled:opacity-50"
      >
        {submitting ? 'Saving…' : submitLabel}
      </button>
    </form>
  );
}

function Field({
  label,
  error,
  hint,
  children,
}: {
  label: string;
  error?: string;
  hint?: string;
  children: React.ReactNode;
}) {
  return (
    <label className="block">
      <span className="text-sm font-medium text-neutral-700">{label}</span>
      {children}
      {hint && !error && <span className="mt-1 block text-xs text-neutral-500">{hint}</span>}
      {error && (
        <span role="alert" className="mt-1 block text-sm text-red-700">
          {error}
        </span>
      )}
    </label>
  );
}
