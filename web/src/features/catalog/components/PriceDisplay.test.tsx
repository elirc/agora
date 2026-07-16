import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ProductDto } from '../../../api/client';
import { PriceDisplay } from './PriceDisplay';

// A product straight off the wire. Note what the SERVER decided: the effective price, the on-sale
// flag, and the badge number all arrive resolved — so these tests can state exactly what the
// component is responsible for, which is rendering and nothing else.
function product(overrides: Partial<ProductDto> = {}): ProductDto {
  return {
    id: 'id-1',
    categoryId: 'cat-1',
    name: 'Cast Iron Skillet',
    slug: 'cast-iron-skillet',
    sku: 'AGA-KIT-002',
    description: 'Pre-seasoned cast iron.',
    priceAmount: 50,
    priceCurrency: 'USD',
    effectivePriceAmount: 50,
    isOnSale: false,
    savePercent: 0,
    sale: null,
    createdAtUtc: '2026-07-16T10:00:00Z',
    ...overrides,
  };
}

describe('PriceDisplay', () => {
  it('shows one price and no badge when nothing is on sale', () => {
    render(<PriceDisplay product={product()} />);

    expect(screen.getByText('$50.00')).toBeInTheDocument();
    expect(screen.queryByText(/Save/)).not.toBeInTheDocument();
    expect(screen.queryByText('Regular price')).not.toBeInTheDocument();
  });

  it('strikes through the list price and shows the sale price and badge', () => {
    render(
      <PriceDisplay
        product={product({
          effectivePriceAmount: 40,
          isOnSale: true,
          savePercent: 20,
          sale: {
            campaignId: 'c-1',
            name: 'Weekend Sale',
            startsAtUtc: '2026-08-07T00:00:00Z',
            endsAtUtc: '2026-08-10T00:00:00Z',
          },
        })}
      />,
    );

    expect(screen.getByText('$40.00')).toBeInTheDocument();
    expect(screen.getByText('$50.00')).toBeInTheDocument();
    expect(screen.getByText('Save 20%')).toBeInTheDocument();
  });

  it('marks the struck-through price as the regular one for screen readers', () => {
    // The strike-through is MEANING, not decoration: a reader that only hears two dollar amounts has
    // no way to know which one it pays. Money is the wrong place to be ambiguous.
    const { container } = render(
      <PriceDisplay product={product({ effectivePriceAmount: 40, isOnSale: true, savePercent: 20 })} />,
    );

    expect(screen.getByText('Regular price')).toBeInTheDocument();
    expect(container.querySelector('s')).toHaveTextContent('$50.00');
  });

  it('renders the SERVER’s save percentage rather than recomputing it', () => {
    // ⭐ THE TEST THAT GUARDS THE RULE. A product at $19.99 with $2.00 off actually saves 10.005% —
    // the server rounds it, in decimal, to 10. If this component ever "helpfully" derived the badge
    // as Math.round((1 - 17.99/19.99) * 100), it would be doing money math in a JS double. Here the
    // server says 10 while the naive client arithmetic would say 10 too — so instead we send an
    // ABSURD value the client could never have computed. If the badge stops reading 99, somebody
    // added arithmetic to the component.
    render(
      <PriceDisplay
        product={product({ priceAmount: 19.99, effectivePriceAmount: 17.99, isOnSale: true, savePercent: 99 })}
      />,
    );

    expect(screen.getByText('Save 99%')).toBeInTheDocument();
  });

  it('trusts isOnSale rather than comparing the two prices itself', () => {
    // Same reasoning, other direction: the server is the only thing that decides a sale is on. If the
    // component inferred `effective < list` it would have a second opinion, and the day the two
    // disagreed nobody could say which was the bug. Ask; do not infer.
    render(<PriceDisplay product={product({ effectivePriceAmount: 40, isOnSale: false, savePercent: 0 })} />);

    expect(screen.getByText('$50.00')).toBeInTheDocument();
    expect(screen.queryByText('$40.00')).not.toBeInTheDocument();
    expect(screen.queryByText(/Save/)).not.toBeInTheDocument();
  });

  it('formats a decimal-string amount from the wire without arithmetic', () => {
    // The OpenAPI document types a decimal as number-or-string; the client parses at the display
    // boundary and never before.
    render(<PriceDisplay product={product({ priceAmount: '34.50', effectivePriceAmount: '34.50' })} />);

    expect(screen.getByText('$34.50')).toBeInTheDocument();
  });

  it('hides the badge when the saving rounds to nothing', () => {
    render(<PriceDisplay product={product({ effectivePriceAmount: 49.99, isOnSale: true, savePercent: 0 })} />);

    expect(screen.getByText('$49.99')).toBeInTheDocument();
    expect(screen.queryByText(/Save/)).not.toBeInTheDocument();
  });
});
