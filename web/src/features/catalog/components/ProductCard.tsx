import { Link } from 'react-router-dom';
import type { ProductDto } from '../../../api/client';
import { AddToCartButton } from '../../cart/components/AddToCartButton';
import { PriceDisplay } from './PriceDisplay';

// ⚠️ S05 CHANGED THE SHAPE OF THIS CARD, AND THE REASON IS NOT COSMETIC. Until now the whole card was
// one big <Link>. It cannot stay that way once it has a second action, because a <button> inside an
// <a> is INVALID HTML — the spec forbids interactive content inside an anchor, and what browsers
// actually do with it varies: a click may follow the link, may fire the button, may do both. Screen
// readers announce a link containing a button and offer no reliable way to reach either.
//
// So the card became an <article> with a linked title, and it is worth noticing WHAT FORCED THAT: not
// a design review — the card acquiring a second thing to do. "The whole card is a link" works right
// up until it has to hold two verbs, and then the honest structure is one element per action. The
// keyboard story is better for it: Tab now reaches "Skillet" and "Add to cart" as two stops, which is
// what they are.
export function ProductCard({ product }: { product: ProductDto }) {
  return (
    <article className="flex h-full flex-col justify-between rounded-lg border border-neutral-200 bg-white p-4 transition hover:border-neutral-400 hover:shadow-sm">
      <div>
        <h3 className="font-medium text-neutral-900">
          <Link to={`/products/${product.slug}`} className="hover:underline">
            {product.name}
          </Link>
        </h3>

        {/* S04: this card used to call formatPrice itself. It now asks a component that knows what a
            sale looks like, so the grid and the product page cannot drift into rendering prices two
            different ways — the bug where a card says $40 and the page you land on says $50. */}
        <div className="mt-3">
          <PriceDisplay product={product} />
        </div>
      </div>

      {/* Add to cart straight from the grid: a shopper who already knows what they want should not
          have to visit a product page to buy it. Note what the button sends — an id and a quantity,
          never the price this card happens to be holding. See AddToCartButton. */}
      <div className="mt-4">
        <AddToCartButton product={product} />
      </div>
    </article>
  );
}
