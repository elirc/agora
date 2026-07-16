// LEARN: this MIRRORS the domain rules (Product / Sku / Slug in Agora.Domain), it does not REPLACE
// them. The server re-validates every field and has the final say — this exists so an admin gets
// instant, per-field feedback instead of a round trip. Keep the two in step: the limits below are
// the same constants the C# domain enforces. Anything the browser checks, the API checks again.

export const MAX_NAME_LENGTH = 200;
export const MAX_DESCRIPTION_LENGTH = 4000;
export const MAX_SKU_LENGTH = 40;
export const MAX_SLUG_LENGTH = 120;

export interface ProductFormValues {
  categoryId: string;
  name: string;
  slug: string; // optional on create; ignored on edit (slug is immutable)
  sku: string; // create only
  description: string;
  priceAmount: string; // a DECIMAL STRING — never a JS number (see below)
  priceCurrency: string;
}

export type FieldErrors = Partial<Record<keyof ProductFormValues, string>>;

// WHY the price is a string all the way to the wire: JavaScript numbers are IEEE-754 doubles and
// cannot represent 0.10 exactly — do float math on money and cents drift. The form holds the raw
// string the admin typed, we only CHECK its shape here, and it is sent as a string that the server
// binds to a C# decimal. The client formats money; it never computes with it.
const DECIMAL = /^\d+(\.\d{1,2})?$/;
const SLUG = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;
const SKU = /^[A-Za-z0-9-]+$/;
const CURRENCY = /^[A-Za-z]{3}$/;

/** Validate the fields common to create and edit. Pass `forCreate` to also require slug/sku shape. */
export function validateProduct(values: ProductFormValues, forCreate: boolean): FieldErrors {
  const errors: FieldErrors = {};

  if (!values.categoryId) {
    errors.categoryId = 'Choose a category.';
  }

  const name = values.name.trim();
  if (name.length === 0) {
    errors.name = 'Name is required.';
  } else if (name.length > MAX_NAME_LENGTH) {
    errors.name = `Name must be ${MAX_NAME_LENGTH} characters or fewer.`;
  }

  if (values.description.trim().length > MAX_DESCRIPTION_LENGTH) {
    errors.description = `Description must be ${MAX_DESCRIPTION_LENGTH} characters or fewer.`;
  }

  const price = values.priceAmount.trim();
  if (price.length === 0) {
    errors.priceAmount = 'Price is required.';
  } else if (!DECIMAL.test(price)) {
    errors.priceAmount = 'Enter a price like 19.99 (no negative values).';
  }

  if (!CURRENCY.test(values.priceCurrency.trim())) {
    errors.priceCurrency = 'Use a 3-letter currency code, e.g. USD.';
  }

  if (forCreate) {
    const sku = values.sku.trim();
    if (sku.length === 0) {
      errors.sku = 'SKU is required.';
    } else if (sku.length > MAX_SKU_LENGTH || !SKU.test(sku)) {
      errors.sku = 'SKU uses letters, digits and hyphens only.';
    }

    // Slug is OPTIONAL on create — blank means "generate it from the name" (server-side). We only
    // validate the shape when the admin chose to supply one.
    const slug = values.slug.trim();
    if (slug.length > 0 && (slug.length > MAX_SLUG_LENGTH || !SLUG.test(slug))) {
      errors.slug = 'Slug uses lowercase words joined by single hyphens, e.g. trail-shoes.';
    }
  }

  return errors;
}

/** A URL-safe slug preview, mirroring Slug.Generate on the server (a suggestion, not the source of truth). */
export function slugPreview(name: string): string {
  return name
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
}
