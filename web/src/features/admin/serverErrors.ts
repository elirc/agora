import { ApiError } from '../../api/client';
import type { FieldErrors } from './validation';

// LEARN: the server's 422 is a single human message (problem.detail), not a per-field map — the
// domain raises one DomainRuleException at a time. So here we do a best-effort routing of that
// message to the field it's about, purely for nicer UX. The message text is NOT a contract (clients
// switch on `type`, not words — ADR-0002), so this is a heuristic with a safe fallback: anything we
// can't place shows as a form-level error, never gets dropped.
const KEYWORD_TO_FIELD: ReadonlyArray<[RegExp, keyof FieldErrors]> = [
  [/\bslug\b/i, 'slug'],
  [/\bsku\b/i, 'sku'],
  [/\bcategory\b/i, 'categoryId'],
  [/\bcurrency\b/i, 'priceCurrency'],
  [/\bprice\b/i, 'priceAmount'],
  [/\bname\b/i, 'name'],
  [/\bdescription\b/i, 'description'],
];

export interface MappedServerErrors {
  fields: FieldErrors;
  /** A message that couldn't be placed on a specific field — show it form-level. */
  form: string | null;
}

export function mapServerErrors(error: unknown): MappedServerErrors {
  if (!(error instanceof ApiError) || error.status !== 422) {
    return { fields: {}, form: null };
  }

  const detail = error.problem?.detail ?? error.problem?.title ?? 'The server rejected this change.';

  for (const [pattern, field] of KEYWORD_TO_FIELD) {
    if (pattern.test(detail)) {
      return { fields: { [field]: detail }, form: null };
    }
  }

  return { fields: {}, form: detail };
}
