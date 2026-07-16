import { toInt } from '../../catalog/format';
import { useStock } from '../queries';

interface StockBadgeProps {
  productId: string;
  /** Show "12 in stock" as well as the low/out states. The product page wants this; a grid card doesn't. */
  verbose?: boolean;
}

/** How many are left — advisory, never a promise. */
// 📘 THE HARDEST DECISION IN THIS SPRINT IS A PIECE OF COPY, and it is worth more than the code
// under it. "3 left" is a read from a moving number: between this render and the shopper's click, a
// reservation can lapse and another can be taken. The number was true when it was computed. It is not
// true *now*, and it cannot be — there is no version of this component that fixes that.
//
// So the badge is ADVISORY, and the wording has to earn that. The rule we settled on (ADR-0008):
//
//   • "Out of stock" is stated FLATLY, because being wrong in that direction costs the shop a sale
//     and costs the shopper nothing. If it comes back in stock while they look, they refresh and are
//     pleasantly surprised. Nobody has ever been upset by that.
//   • "Only 3 left" is stated as URGENCY, not inventory — it is a nudge, it will be wrong sometimes,
//     and being wrong by one is survivable. What it must never do is read as a reservation.
//   • An exact count is shown only when it is HIGH ENOUGH not to matter ("12 in stock"), where being
//     off by one is invisible.
//   • **We never say "yours".** No "reserved for you", no countdown, no "hurry, 2 people are looking
//     at this". The shop makes exactly one promise about stock — the moment it writes a reservation
//     under the concurrency token — and a badge is not that moment.
//
// The dark-pattern version of this component is three lines away and considerably more profitable.
// The line we hold: the UI may create urgency about a fact, and may not invent the fact.
export function StockBadge({ productId, verbose = false }: StockBadgeProps) {
  const { data: stock, isPending, isError } = useStock(productId);

  // ⚠️ SILENT WHEN WE DO NOT KNOW. If the stock query is loading or has failed, this renders
  // nothing at all — because the alternatives are worse. "In stock" while loading is a guess we would
  // be making on the shop's behalf, in the shop's favour, about the one number a shopper is deciding
  // on. A skeleton shimmer would be honest but would push the price around on every card, and a red
  // "unavailable" turns our outage into their disappointment. Saying nothing costs a nudge.
  if (isPending || isError || !stock) {
    return null;
  }

  // The OpenAPI document types an int32 as number-or-string (.NET 10 emits both so a large integer
  // can travel as a string), so every server-sent count comes through toInt. Same helper the cart's
  // quantities use — one place that knows about the wire's quirk, rather than a `Number()` sprinkled
  // wherever a count is rendered.
  const available = toInt(stock.available);

  if (!stock.inStock) {
    return (
      <span
        className="inline-flex items-center rounded-full bg-stone-200 px-2 py-0.5 text-xs font-medium text-stone-600"
        data-testid="stock-out"
      >
        Out of stock
      </span>
    );
  }

  if (available <= LOW_STOCK_AT) {
    return (
      <span
        className="inline-flex items-center rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800"
        data-testid="stock-low"
      >
        Only {available} left
      </span>
    );
  }

  if (!verbose) {
    return null;
  }

  return (
    <span className="text-xs text-stone-500" data-testid="stock-in">
      {available} in stock
    </span>
  );
}

// WHY a constant here and not the server's threshold: this is a COPY decision (when does a number
// become a nudge?), not the shop's restocking policy. The admin's low-stock threshold answers "when
// should I reorder"; this answers "when is a count worth mentioning to a stranger". They are
// different questions that happen to have similar answers today, and coupling them would mean
// raising the reorder point silently changes what shoppers are told.
const LOW_STOCK_AT = 5;
