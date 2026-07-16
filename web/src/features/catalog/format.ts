// ⚠️ THE RULE THIS FILE EXISTS TO ENFORCE: MONEY NEVER TOUCHES A JS FLOAT.
//
// JavaScript has exactly one number type — an IEEE-754 double — and it cannot represent 0.10. So
// `19.99 * 0.9` is 17.991000000000003 here, and `0.1 + 0.2` is 0.30000000000000004. Not "close
// enough": wrong, in a way that survives whatever rounding you bolt on afterwards, and that surfaces
// as a penny on a receipt a customer photographs and emails to support.
//
// So the discipline is a boundary, not a habit. The SERVER resolves every amount in C# `decimal` —
// the list price, the sale price, and even the "Save X%" badge number — and this module only ever
// FORMATS what it was handed. Notice what is not in this file and must never be: no multiplication,
// no subtraction, no percentage. If a component ever needs a number the API did not send, the fix is
// a field on the DTO, not a calculation here. The one `Number()` below is a PARSE, not arithmetic.
//
// 🔗 the same rule one layer up: ProductForm keeps the price a decimal STRING from the input to the
// wire (`type="text"`, never `type="number"`), so the browser never coerces it through a double on
// the way IN either. Money arrives as text, leaves as text, and is only ever a number inside C#.

/**
 * Format an amount the server resolved, in the currency the server named.
 *
 * @param amount the exact amount from the API — never a value computed in this app
 * @param currency the ISO 4217 code the same response carried
 */
export function formatPrice(amount: number | string, currency: string): string {
  // GOTCHA: the OpenAPI document types a decimal as `number | string` (a decimal on the wire can
  // serialize either way), so coerce once, here, at the display boundary and nowhere else. This is
  // the LAST thing that happens to a price — parse, format, render — so the double it briefly
  // becomes has no arithmetic left to get wrong.
  const value = typeof amount === 'string' ? Number(amount) : amount;
  return new Intl.NumberFormat('en-US', { style: 'currency', currency }).format(value);
}

/** Coerce a server-sent integer (the OpenAPI document types an int32 as number-or-string). */
export function toInt(value: number | string): number {
  return typeof value === 'string' ? Number(value) : value;
}

/** Format the end of a sale window for a shopper: "Ends Sun, Aug 9". */
// WHY the browser's timezone here and NOT the server's: this is the one place a local clock is the
// right answer. "Ends Sunday" means Sunday where the SHOPPER is — the instant came from the server
// as an ISO offset, and Intl renders that instant in the reader's zone. Compare the price path,
// where the shopper's clock is never consulted: the same instant, but WHO is asking changes what the
// honest rendering is. Display is local; truth is UTC.
export function formatSaleEnds(endsAtUtc: string): string {
  return new Intl.DateTimeFormat('en-US', {
    weekday: 'short',
    month: 'short',
    day: 'numeric',
  }).format(new Date(endsAtUtc));
}
