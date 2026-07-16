import type { CartLineDto } from '../../../api/client';
import { formatPrice, toInt } from '../../catalog/format';
import { useRemoveCartLine, useSetCartLineQuantity } from '../mutations';

const MIN_QUANTITY = 1;
// Mirrors the server's CartLine.MaxQuantity. GOTCHA: this is a DUPLICATE of a server rule, and the
// honest thing is to say so rather than pretend it is the source of truth. It disables the + button
// so the shopper never sends a request that can only be refused — a courtesy, not a boundary. The
// server still says 422 if this drifts, which is the correct division: the client's copy of a rule is
// a hint, and the day it is wrong the shopper sees an error rather than a wrong cart. (If this list
// of duplicated constants ever grows past one, it belongs in the contract.)
const MAX_QUANTITY = 99;

export function CartLineRow({ line }: { line: CartLineDto }) {
  const setQuantity = useSetCartLineQuantity();
  const remove = useRemoveCartLine();

  const quantity = toInt(line.quantity);
  const busy = setQuantity.isPending || remove.isPending;

  return (
    <li className="flex gap-3 border-b border-neutral-100 py-4" aria-busy={busy}>
      <div className="min-w-0 flex-1">
        <p className="truncate font-medium text-neutral-900">{line.name}</p>
        <p className="mt-0.5 text-xs text-neutral-500">
          {formatPrice(line.unitPriceAmount, line.currency)} each
        </p>

        {/* 📘 THE SECOND TEACHING AXIS, RENDERED. The line remembers what it was quoted; the cart
            charges what the store charges TODAY. When those disagree, the shopper is told — here,
            in the drawer, before checkout — rather than discovering it on a receipt.
            WHY the wording is neutral ("was", not "price increased"): the server sends a
            directionless `priceChanged` on purpose, because cheaper and dearer are the same fact to
            a shopper — "this is not the number you last saw" — and both numbers are on screen for
            them to read. We report; we don't editorialise. */}
        {line.priceChanged && (
          <p className="mt-1 text-xs text-amber-700">
            Was{' '}
            <s>{formatPrice(line.priceAtAdditionAmount, line.currency)}</s> when you added it
          </p>
        )}

        <div className="mt-2 flex items-center gap-2">
          <QuantityButton
            label={`Decrease quantity of ${line.name}`}
            symbol="−"
            disabled={busy || quantity <= MIN_QUANTITY}
            onClick={() => setQuantity.mutate({ lineId: line.id, quantity: quantity - 1 })}
          />

          {/* WHY a <span> with an aria-label and not an <input type="number">: a number input invites
              a keystroke-per-request storm, accepts "e" and "-" in most browsers, and — the real
              reason — `parseInt(input.value) || 0` on an emptied box sends 0, which the server
              rightly refuses as a 422. The stepper can only ever send quantities that exist. */}
          <span
            className="min-w-8 text-center text-sm tabular-nums text-neutral-900"
            aria-label={`Quantity of ${line.name}`}
          >
            {quantity}
          </span>

          <QuantityButton
            label={`Increase quantity of ${line.name}`}
            symbol="+"
            disabled={busy || quantity >= MAX_QUANTITY}
            onClick={() => setQuantity.mutate({ lineId: line.id, quantity: quantity + 1 })}
          />

          {/* ⚠️ the accessible name is "Remove Cast Iron Skillet", not "Remove". A cart has several
              lines, so a screen-reader user tabbing through hears "Remove, Remove, Remove" with no
              way to tell which is which — the visible word is only unambiguous because you can SEE
              which row it sits in. Same rule as the steppers above. (This is also why the drawer's
              test can ask for a specific button by name; an ambiguous query is usually a real
              ambiguity for somebody.) */}
          <button
            type="button"
            aria-label={`Remove ${line.name}`}
            onClick={() => remove.mutate(line.id)}
            disabled={busy}
            className="ml-2 text-xs text-neutral-500 underline hover:text-neutral-800 disabled:opacity-50"
          >
            Remove
          </button>
        </div>
      </div>

      {/* The line total is the SERVER's arithmetic, rendered. Not `unitPrice * quantity` — see
          format.ts for why that multiplication is a bug in this language. */}
      <p className="text-sm font-medium tabular-nums text-neutral-900">
        {formatPrice(line.lineTotalAmount, line.currency)}
      </p>
    </li>
  );
}

function QuantityButton({
  label,
  symbol,
  disabled,
  onClick,
}: {
  label: string;
  symbol: string;
  disabled: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      aria-label={label}
      onClick={onClick}
      disabled={disabled}
      className="h-7 w-7 rounded border border-neutral-300 text-sm leading-none text-neutral-700 hover:border-neutral-500 disabled:opacity-40"
    >
      {symbol}
    </button>
  );
}
