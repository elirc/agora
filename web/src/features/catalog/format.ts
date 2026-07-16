// GOTCHA: the OpenAPI document types priceAmount as `number | string` (a decimal on the wire can
// serialize either way), so coerce once, here, before formatting. When S04's Money value object
// lands, price display gets its own honest component — this is the seam it will grow from.
export function formatPrice(amount: number | string, currency: string): string {
  const value = typeof amount === 'string' ? Number(amount) : amount;
  return new Intl.NumberFormat('en-US', { style: 'currency', currency }).format(value);
}
