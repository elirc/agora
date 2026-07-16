import type { components } from './schema';

// LEARN: these types are GENERATED from the same OpenAPI document the backend drift-tests
// (docs/openapi/agora-v1.json). The API is one contract with two enforced consumers — the C#
// server and this client. Rename a field on the server, regenerate, and TypeScript breaks HERE,
// at compile time, instead of the user finding out at runtime.
export type CategoryDto = components['schemas']['CategoryDto'];
export type ProductDto = components['schemas']['ProductDto'];
export type ProblemDetails = components['schemas']['ProblemDetails'];

/** One page of results plus the cursor for the next one (mirrors the backend's Page<T>). */
export interface Page<T> {
  items: T[];
  nextCursor: string | null;
}

/**
 * A non-2xx response carrying an RFC 7807 problem. Callers switch on `status` / `problem.type`,
 * never on message text — the same contract the server promises (ADR 0002).
 */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly problem: ProblemDetails | null,
  ) {
    super(problem?.title ?? `Request failed with status ${status}`);
    this.name = 'ApiError';
  }

  get isNotFound(): boolean {
    return this.status === 404;
  }
}

// WHY: this module is the ONE place in the app allowed to call fetch — the ESLint boundary rule
// exempts src/api/ and forbids it everywhere else. Components ask for data through the query
// hooks; the network lives behind this seam.
async function request(path: string): Promise<Response> {
  const response = await fetch(path, { headers: { Accept: 'application/json' } });

  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as ProblemDetails | null;
    throw new ApiError(response.status, problem);
  }

  return response;
}

function queryString(params: Record<string, string | number | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== '') {
      search.set(key, String(value));
    }
  }
  const rendered = search.toString();
  return rendered ? `?${rendered}` : '';
}

export interface ListProductsParams {
  category?: string;
  cursor?: string;
  limit?: number;
}

export async function listProducts(params: ListProductsParams = {}): Promise<Page<ProductDto>> {
  const response = await request(`/api/v1/products${queryString({ ...params })}`);
  return {
    items: (await response.json()) as ProductDto[],
    // LEARN: the next-page token rides the X-Next-Cursor response header, not the body — so the
    // body stays a clean array. This is the client half of the header-cursor decision in ADR 0002.
    nextCursor: response.headers.get('X-Next-Cursor'),
  };
}

export async function getProduct(slug: string): Promise<ProductDto> {
  const response = await request(`/api/v1/products/${encodeURIComponent(slug)}`);
  return (await response.json()) as ProductDto;
}

export interface ListCategoriesParams {
  cursor?: string;
  limit?: number;
}

export async function listCategories(
  params: ListCategoriesParams = {},
): Promise<Page<CategoryDto>> {
  const response = await request(`/api/v1/categories${queryString({ ...params })}`);
  return {
    items: (await response.json()) as CategoryDto[],
    nextCursor: response.headers.get('X-Next-Cursor'),
  };
}

export async function getCategory(slug: string): Promise<CategoryDto> {
  const response = await request(`/api/v1/categories/${encodeURIComponent(slug)}`);
  return (await response.json()) as CategoryDto;
}
