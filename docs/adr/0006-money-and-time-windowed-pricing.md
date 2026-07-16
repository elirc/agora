# ADR 0006: Money as a Value Object, and Sale Prices as Time Windows

- **Status:** Accepted
- **Date:** 2026-07-16 (Sprint 04)
- **Deciders:** [Author], [Reviewer]

## Context

Sprint 04 is the first sprint whose subject is **business logic** rather than plumbing. Two questions
define it, and this ADR records both because they meet in one place — the price a shopper is quoted.

1. **What is a price?** Since S01, a product's price has been a `decimal PriceAmount` plus a
   `string PriceCurrency`: a value object in two trench coats, deliberately kept together and never
   leaked as a loose `decimal`, with a `// TRY:` note promising S04 would collect the debt.
2. **What is a sale?** The store owner wants to run a weekend sale — start it Friday, end it Monday,
   and not be awake for either.

The second question is the interesting one. The obvious design is a boolean (`IsOnSale`) plus a
`SalePrice`, flipped by whoever is around at midnight. It is obvious, it is smaller, and it is wrong
in a way that only shows up later — which is exactly the kind of decision worth writing down.

## Decision 1 — `Money` is a value object: decimal, currency-as-identity, one rounding policy

`Agora.Domain.Values.Money` is a `readonly record struct` of a `decimal Amount` and a `CurrencyCode
Currency`. Three properties define it:

- **`decimal`, never `double`.** A double is base-2 and cannot represent `0.10`; ten of them do not
  sum to `1.00`. `decimal` is base-10 and exact. Every field, intermediate, and operator is decimal.
- **Currency is part of identity.** `Money(5, USD) != Money(5, EUR)`, and `Add`/`Subtract`/`CompareTo`
  **throw** on a mismatch rather than pretending the numbers are commensurable. There is no implicit
  conversion and no ambient "default currency". Multi-currency is out of scope (§Out), so the correct
  behaviour today is to *refuse*, not to guess a rate.
- **Rounding is a policy, chosen once, written here.** Banker's rounding (`MidpointRounding.ToEven`)
  to the currency's minor units, applied in `Money.Of` — the type's only constructor. There is
  exactly one `Math.Round` for money in the codebase.

**Why `ToEven` and not "round half up":** half-up is *biased* — it rounds `.005` up every time, so
across a few hundred thousand order lines the house systematically gains a fraction of a cent per
tie. Half-to-even sends ties both ways and the error cancels. It is the banking default, the IEEE
default, and .NET's default. The cost is that `2.345 → 2.34` surprises someone once.

**Why rounding happens at construction:** the invariant "an `Amount` is always at its currency's
scale" then holds for *every instance that exists*, so nothing downstream re-rounds or wonders. The
visible consequence: an admin who types `19.999` gets `20.00` stored and echoed. The policy is
applied once, at the boundary, where it can be seen — rather than surfacing later inside some
multiplication nobody was watching.

**Why `Allocate` / `Distribute` ship now, with no production caller.** Splitting an amount across N
lines is where money bugs actually live: `10.00 / 3` naively gives `3.33 × 3 = 9.99` and a cent that
stopped existing. `Distribute` works in **minor units** (integers) and uses the **largest-remainder**
method, so the shares always sum to exactly the input. S05's cart is the first caller and S07's order
lines the second; the capability ships now because its **property tests** are the point of the
sprint, and because a guarantee proven before its first caller is a guarantee neither sprint has to
re-litigate. This is the one place we accept building slightly ahead of demand, and the design debate
on the PR is where the Reviewer conceded it — noting that allocation is precisely the part that earns
its property tests.

**Why CsCheck (a new NuGet dependency).** Example tests prove the cases the author already had in
mind, which are by construction the ones they already got right. Money's bugs are rare, silent, and
about money — the worst triple. `CsCheck` generates thousands of amount × weight combinations,
**shrinks** any counterexample to its smallest form, and prints a reproducing seed. The critical
property — *the shares always sum back to the amount* — is a claim about all inputs, so it is tested
as one. (Justification for `CsCheck 4.7.0`, `tests/Agora.Domain.Tests` only; it never ships.)

**Rejected: `long` minor units as the storage type.** Fowler's classic Money stores cents as a
`long`. It removes rounding questions from the field but pushes them into every read, and it makes
the database column an integer that no reporting tool, DBA, or `SELECT` reads correctly without a
mental divide-by-100. `decimal(18,2)` is exact, self-describing, and already what the column was.

## Decision 2 — a sale is a **time window**, not a boolean

`SaleCampaign` is a product-scoped entity holding a `Discount` (percentage **or** fixed amount) and a
`DateRange` window. Nothing switches it on. `PriceResolver.Resolve(listPrice, campaigns, asOf)`
answers *what does this cost at this instant*, and the storefront asks with the instant from its
injected `TimeProvider`.

**Why a window beats a flag.** A boolean records only *now*, and *now* is the one moment you never
need to look up afterwards. A window answers every question the store actually asks from the same
row: what was this priced at last Tuesday (an order says $34.50 — was that the sale or a mistake?);
what is scheduled for the weekend; why was it still discounted three days after the sale ended. It
also deletes an entire class of operational failure — nobody has to be awake at midnight, and nobody
can forget to switch it off.

**Half-open windows, `[Starts, Ends)`.** A closed interval makes the last instant belong to both a
window and its successor, so back-to-back campaigns overlap for one tick and "which price?" has two
answers. Half-open makes windows tile perfectly. This is `DateRange`, and S06's stock reservations
reuse the same shape.

**Overlapping campaigns: the shopper gets the best price.** Two campaigns can cover one instant (a
storewide weekend sale and a clearance on one item). The resolver applies each and takes the lowest,
breaking ties deterministically by `(CreatedAtUtc, Id)`. This is the only rule that is defensible out
loud, cannot be gamed by database row order, and never has to be explained to a customer who saw a
lower number a minute ago. **This is not a promotion engine** — S11 builds that (stacking,
exclusivity, priorities, coupons), and `Discount` is the tiny closed type it will subsume. Building
the engine now, for two cases with no rules to compose, would be predicting the shape of a problem we
have not met.

**Time enters this pipeline in exactly two places, and both are named:**

1. `ProductPricing.PriceAsync` — one `clock.GetUtcNow()` **per request**, not per product. A page
   that read the clock per row would price the top of the grid at 23:59:59 and the bottom at 00:00:00
   the night a campaign starts.
2. `ISaleCampaignRepository.ListActiveForProductsAsync(ids, asOf, ct)` — the same instant, pushed
   into the `WHERE` clause so a decade of history is not loaded to throw away.

Everything below those two (`PriceResolver`, `SaleCampaign.IsActiveAt`, `DateRange.Contains`) takes
the instant as an **argument** and has no clock at all. That is what makes the boundary behaviour
pinnable in a test, forever, in any timezone.

**Whose midnight?** `CreateSaleCampaignRequest` carries two `DateTimeOffset`s, not two dates. "Starts
2026-08-01" is not an instant until someone says whose midnight they meant — so the decision is made
in the **browser**, the only party that knows the admin's timezone, which resolves its
`datetime-local` input and sends a real instant. The server never guesses with its own locale.

## Decision 3 — the contract grows by addition; the schema does not move at all

`ProductDto` keeps `priceAmount` + `priceCurrency` unchanged and **adds** `effectivePriceAmount`,
`isOnSale`, `savePercent`, and a nested `sale`. Every S01 client still works and simply never learns
there was a sale (ADR-0002, rule 3).

The database is untouched: `Money` maps as an EF **complex property** flattened back onto the same
`PriceAmount decimal(18,2)` + `PriceCurrency char(3)` columns the catalog has had since S01. The
`AddPricing` migration therefore only creates `sale_campaigns`. This is the payoff S01 predicted when
it kept the primitive pair *contained*: a value object is a fact about your **code**, and the storage
can stay exactly as boring as it was. A test (`Money_maps_to_the_two_price_columns_the_catalog_has_
always_had`) pins that claim, because a claim in three comments is worth less than one assertion.

**Why complex properties and not `OwnsOne`:** `Money`, `DateRange`, and `Discount` are structs with no
identity. EF's complex types model exactly that — flattened into the owner's row, no key, no separate
tracking. `OwnsOne` demands a reference type and invents a hidden key, i.e. models a value as an
entity, which is the thing value objects exist to stop.

**Why `savePercent` is computed on the server and derived from the amounts:** JavaScript has no
decimal type, so `(1 - 17.99/19.99) * 100` in the browser is a double disagreeing with the server's
decimal at exactly the boundaries that matter. And it is derived from the two *amounts*, not from the
campaign's percentage, because a fixed-amount campaign has no percentage at all and a 25%-off
campaign's real saving after rounding to cents is 24.98%. One rule, right for both kinds.

## Consequences

- **A currency-shape rule got looser, on purpose.** S03's `Product.ValidCurrency` rejected `"usd"`;
  `CurrencyCode.From` **normalises** case at the door, exactly as `Slug` and `Sku` already did.
  `"usd"` and `"USD"` were never two currencies. `"DOLLAR"` and `"US$"` are still refused.
- **`ModelMappingTests` learned a third way of being mapped.** Its sweep asked the model for
  properties and navigations — complete until complex properties existed. It would have reported
  `Product.Price` and `SaleCampaign.Window` as *unmapped*: a false alarm on the two best-mapped
  properties in the sprint. The fix was to teach the test, not to add an allowlist entry — an
  allowlist entry would have put `Product.Price` beyond the guard forever. **The S04 allowlist gains
  nothing; it is still just `Product.IsArchived`.**
- **The SQLite UTC-ticks converter had to learn about complex types too.** `AgoraDbContext`'s sweep
  walked top-level properties only; a campaign's `StartsAtUtc`/`EndsAtUtc` hang off the complex type,
  and without the converter the storefront's window filter throws on the test provider. A sweep over
  "everything" is only as good as its definition of everything.
- **⚠️ EF Core 10 cannot index a complex type's properties.** The query wants
  `(ProductId, StartsAtUtc)`; both `HasIndex(c => new { c.ProductId, c.Window.Starts })` and the
  string overload throw. We index `ProductId` alone and accept a small scan — genuinely fine, since a
  product accumulates a handful of campaigns in its life, not thousands. The escape hatch, if that
  ever changes, is one `migrationBuilder.Sql("CREATE INDEX …")`: the columns are there, only the model
  builder cannot name them. **This is the one concrete cost the value object charged us.**
- The catalog's cursor still walks `(CreatedAtUtc, Id)`. An effective price is neither stable (it
  changes by itself at a window boundary — rows would shuffle mid-walk) nor in the database. "Sort by
  sale price" is a read-model question (S14), and the answer is a projection, not an `ORDER BY`.
- Storefront reads now return `PricedProduct`, not `Product`, so no endpoint *can* serve a list price
  as though it were the price in force. The type carries the requirement.
- Every currency assumption is behind `CurrencyCode.MinorUnits` (hardcoded `2`). JPY has 0 and KWD
  has 3 — the day a second currency ships, the ISO table lands in that one method, and
  `Discount.AmountOff` grows a `Money` so Money's own currency gate does the refusing.

## Rejected alternatives

- **`decimal` + `string` on the entity (the status quo).** The Reviewer's opening position; see the
  design-debate thread. Rejected: the primitive pair is exactly where penny bugs breed — mixed-currency
  addition, silent rounding, allocation drift — and the type makes the illegal operations refuse to
  compile while pinning the rounding policy to one audited place. Cost: one struct.
- **`IsOnSale` boolean + `SalePrice` on `Product`.** Rejected: records only *now*; answers no question
  about the past or the future; requires a human at midnight, twice.
- **A promotion/rules engine now.** Deferred to S11 by the curriculum, and rightly: two cases and no
  composition rules is not enough information to design a pipeline against.
- **`?asOf=` as a storefront query param.** Rejected: a shopper does not get to ask what a page costs
  next Tuesday. Time is an input to the *pipeline*, not to the API.
- **Multi-currency.** Out of scope. `Money` is built so that adding it is a widening (an ISO table, a
  rate port) rather than a rewrite — which is the entire return on the type.
