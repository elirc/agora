import type { ProductDto } from '../../../api/client';
import { formatPrice, toInt } from '../format';

/**
 * A product's price as a shopper sees it: one number, or a struck-through list price beside the sale
 * price and a "Save X%" badge.
 */
// ⚠️ READ WHAT THIS COMPONENT DOES NOT DO. It does not multiply, subtract, or compute a percentage.
// Every number below arrived resolved from the server: `priceAmount` (the list price),
// `effectivePriceAmount` (what it costs right now), and `savePercent` (the badge). It would be so
// easy — and so wrong — to write `Math.round((1 - effective / list) * 100)` here and drop a field
// from the DTO. JavaScript has no decimal type; that expression is a double, and it disagrees with
// the server's decimal at exactly the boundaries that matter. One resolution, in one language, in
// one layer. The client renders. See ../format.ts.
//
// 📘 and note the SHAPE of the sale state: the component asks `isOnSale`, it does not compare
// `effectivePriceAmount < priceAmount`. Deriving the state here would mean the UI has its own second
// opinion about whether a sale is on, and the day it disagrees with the server, nobody can say which
// one is the bug. Ask; do not infer.
export function PriceDisplay({
  product,
  size = 'sm',
}: {
  product: ProductDto;
  size?: 'sm' | 'lg';
}) {
  const listPrice = formatPrice(product.priceAmount, product.priceCurrency);
  const salePrice = formatPrice(product.effectivePriceAmount, product.priceCurrency);

  if (!product.isOnSale) {
    return <span className={size === 'lg' ? 'text-lg text-neutral-800' : 'text-sm text-neutral-600'}>{listPrice}</span>;
  }

  return (
    <span className="flex flex-wrap items-baseline gap-2">
      <span className={size === 'lg' ? 'text-lg font-semibold text-rose-700' : 'text-sm font-semibold text-rose-700'}>
        {salePrice}
      </span>

      {/* WHY a <s> element and not just `line-through`: the strike-through is MEANING, not decoration
          — "this price no longer applies" — and a screen reader that only sees a CSS class reads out
          two prices with no way to tell which one the shopper pays. The visually-hidden label spells
          it out for the same reason. Money is exactly the wrong place to be ambiguous. */}
      <s className={size === 'lg' ? 'text-sm text-neutral-500' : 'text-xs text-neutral-500'}>
        <span className="sr-only">Regular price </span>
        {listPrice}
      </s>

      <SaveBadge percent={toInt(product.savePercent)} />
    </span>
  );
}

// The badge number is the SERVER's arithmetic, rendered. It is derived there from the two amounts
// rather than from the campaign's percentage, because a fixed-amount sale ($5 off) has no percentage
// at all — see EffectivePrice.SavedPercent.
function SaveBadge({ percent }: { percent: number }) {
  if (percent <= 0) {
    return null;
  }

  return (
    <span className="rounded bg-rose-50 px-1.5 py-0.5 text-xs font-medium text-rose-700">
      Save {percent}%
    </span>
  );
}
