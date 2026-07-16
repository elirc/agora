# Sprint 04 — Money & Time-Windowed Pricing (Business-Logic Flagship #1)

**Goal:** money becomes a type, and a sale becomes a *time-windowed fact* — a price is what it is at
a given instant, not a boolean someone flipped.

## Scope

**In:** `Money` value object (decimal amount + currency, explicit rounding policy, no cross-currency
arithmetic without a rate, `allocate`/`distribute` without lost pennies); Product list price as
`Money`; `DateRange` value object; **SaleCampaign** with a start/end window, a discounted price, and
an `IsActive` convenience; effective-price resolution ("price as of now") feeding the catalog read;
**CsCheck property tests** for Money arithmetic + allocation; price-display React components (list,
sale, strike-through, "save X%"); `docs/http/pricing.http`.

**Out:** cart-level totals (S5); the promotions/coupon engine (S11 — campaigns are per-product sale
prices, not a rules engine); multi-currency.

## Teaching focus

Value objects as bug repellent. Money discipline: decimal, currency as identity, rounding chosen and
written down. Time as an *input* to price resolution, never an ambient fact read from the wall.

## Commit narrative (sketch)

1. `feat(domain): Money — construction, arithmetic, rounding policy` (+ CsCheck property tests, same commit)
2. `feat(domain): allocate/distribute — cents split across lines, largest-remainder, sums preserved` (+ property tests)
3. `feat(domain): DateRange + SaleCampaign with an effective-price resolver`
4. `feat(application): price-resolution query feeding the catalog read`
5. `feat(infrastructure+api): Money persistence (amount+currency), sale fields in catalog DTOs`
6. `feat(web): price-display components (list/sale/strike-through/save-badge)`
7. `docs: ADR-0005 money & time-windowed pricing, concepts/value-objects.md`

## Teaching placements

- 📘 on `Money`: why decimal, why currency is part of identity, why no implicit conversion.
- 📘 on `allocate`: distributing a discount across lines without inventing or losing a cent —
  proven, not asserted, by property test.
- 🔍 on the effective-price resolver: "a sale price is a function of time — find every place time
  enters this pipeline and name it."
- ⚠️ on Money formatting in `web/`: never do money math in TypeScript — the server sends the
  resolved amount and its currency; the client renders it and nothing more.
- 🔗 `DateRange` → foreshadow S6 reservations: the same window shape, a harder clock question.

## Debate

`[Reviewer]`: "A `Money` type is gold-plating — a `decimal` and a currency string on the DTO is
enough; you're wrapping a primitive to feel clever." `[Author]`: the primitive is exactly where
penny bugs breed — mixed-currency addition, silent rounding, allocation drift — and the type makes
the illegal operations refuse to compile while pinning the rounding policy to one audited place.
Cost: one struct. Reviewer concedes, noting allocation is the part that earns its property tests.

## Exercises

- Try `usd + eur` in the domain. Read the compiler error; explain why it beats a runtime check in
  one endpoint.
- A campaign starts "at midnight." Write the test that pins whose midnight that is. Are you sure?
- Allocate a 10% discount across three lines priced 3.33 / 3.33 / 3.34. Prove the discounted lines
  still sum to the discounted total.
