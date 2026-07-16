# ADR-0008 — The clock is a port; stock is reserved, not decremented

**Status:** Accepted · **Sprint:** 06 · **Supersedes:** nothing · **Amends:** ADR-0006 (pricing)

> **Numbering note.** The sprint-06 playbook calls this ADR-0007. S05's cart decisions took that
> number first, because the playbooks were written before the course was built. The ADRs are
> sequential in reality, not in plan. Trust the repo.

## Context

Two complaints arrived together and turned out to be one sprint.

A flash sale ran 09:00–17:00 in the store's own timezone. At 09:30 a customer screenshotted the
product page: **the price was the sale price, and the badge said there was no sale.** One page, two
answers about one campaign at one instant. Support reported the badge appearing "later in the
afternoon".

And the shop sold four of a thing it had three of, because two people were in checkout at once.

## Decision 1 — the domain never reads the wall clock, and CI enforces it

`SaleCampaign.IsActive()` — a no-argument convenience shipped in S04 — read `DateTime.Now` and
compared it against `Window.Starts.DateTime`. It is deleted. Nothing replaced it: `PriceResolver`
had already filtered every candidate through `IsActiveAt(asOf)` using the instant the request read
from the injected `TimeProvider`, so *"is there a campaign here?"* and *"is a campaign running?"*
were the same question. **The convenience was a second, worse way of computing something the system
already knew**, and the fix for a duplicated question is to trust the answer, not to write a better
duplicate.

The deleted method got two things wrong, and only one of them is famous:

1. **It fetched an instant nobody asked it for.** So it could not be asked about any other moment,
   could not be tested at a moment of your choosing, and could not agree with the clock read beside
   it.
2. **It compared that instant to a wall-clock reading with the offset stripped off.**
   `2026-08-04T09:00+09:00` is a moment in the history of the universe. `.DateTime` is the number
   `09:00`, which means nine o'clock *somewhere unstated* and cannot be compared to anything.

(2) is why the bug fired on a UTC server as well, and why the reproduction is deterministic in CI
rather than dependent on a mis-zoned machine.

**The rule now has a fitness function.** `AmbientTimeBanTests` scans `src/Agora.Domain` for
`DateTime.Now`, `DateTime.UtcNow`, `DateTime.Today`, `DateTimeOffset.Now`, and
`DateTimeOffset.UtcNow`, and names the file and line. This asserts on source text, which normally
deserves suspicion; it earns it here because the rule is about what the domain may *ask for*, not
what it computes — the same shape as the dependency-rule tests, which check that Domain cannot reach
a framework rather than that it behaves as if it hasn't.

**`UtcNow` is banned too**, and that surprises people. UTC is not the problem; timezones are only
ever the symptom. A function that fetches the current instant for itself is untestable at any other
instant — and it was untestable *for exactly the reason it was wrong*. `IsActiveAt` has boundary
tests to the tick. `IsActive` had none, and nobody noticed, because writing one is impossible: ask
it about "now" and both answers are the same shade of nothing.

**Rejected: introduce an `IClock` port.** The sprint-06 playbook says to. Agora has had a clock port
since S01 — it is `TimeProvider`, injected everywhere, and the BCL's own abstraction. Wrapping it in
a hand-rolled `IClock` would add an interface, an adapter, and a registration to arrive at the
capability we already had. The playbook was written before the code existed and the code made the
better call. The problem was never a missing port; it was one function that went around it.

Time enters the pricing pipeline in exactly **two** places, and it should stay two:
`ProductPricing`'s single `clock.GetUtcNow()` per request, and the repository's `asOf` filter.
`IsActive()` was an uncounted third.

## Decision 2 — reserve stock, don't decrement it

Two obvious designs fail in opposite directions:

- **Decrement on add-to-cart.** A shopper who fills a cart and goes to lunch holds the last skillet
  hostage from people who would pay. The shop looks sold out and isn't, and nothing gives the stock
  back — "they closed the tab" is not an event anyone receives.
- **Decrement at payment.** Two people are both told yes, and one finds out at the packing bench.
  That is the reported bug.

A **reservation** is the honest middle: a claim on stock, held for **15 minutes**, that expires on
its own. It says yes to exactly one shopper and costs a short wait rather than a permanent hole.

**Availability is computed, never stored.** `available = on-hand − live claims, as of an instant`.
A single `Available` column that everything increments and decrements is tempting because reads get
trivial, and wrong because the column becomes a **running total**: every path that forgets to
decrement, double-decrements, or crashes halfway leaves it permanently and silently untrue, and
nothing can detect the drift because the column is its own source of truth. Then a human reconciles
it with a clipboard. Computed availability costs a join and cannot be wrong.

**Nothing runs to make expiry work.** `ExpiresAtUtc` passes, `IsActiveAt(now)` goes false,
availability stops counting it. The sweep (`ReleaseExpiredReservations`) is a **janitor** that marks
rows which already stopped mattering — so queries stay small and a human can read the table. If it
is switched off for a week the shop is *correct and untidy*. A background job whose failure is a mess
rather than an outage is the one to aim for; the alternative (availability reads a counter only the
sweep decrements) turns a stuck job into silent overselling.

The sweep is **idempotent by construction**: `Release` is "make this released", not "do a release".
Overlapping passes cannot double-release, because there is no such thing.

## Decision 3 — the race we keep, and what actually closes it

`ReserveStock` is a **check-then-act**: read availability, decide, write. Two requests can both read
"1 available" a microsecond apart and both be correct — that is what a check *is*, and no amount of
re-checking closes it. A narrower window is still a window.

**The `if` gives the friendly answer; `StockLevel.Version` gives the correct one.** Reserving bumps
the stock row's concurrency token even though no skillet moves — because what changed is *what the
next shopper may conclude*, which is exactly the thing two racing requests must not both conclude.
The second writer's `UPDATE ... WHERE Version = @loaded` matches zero rows, EF throws, and the
request is told to retry — at which point it re-reads and is honestly told the skillet is gone.

This is the same layered shape as S02's unique index on `users.email` and S05's unique
`(CartId, ProductId)`: **the domain is polite; the database is right.**

S05's cart deliberately has **no** version, and that was not laziness either time. A cart is raced by
one person in two tabs over something that cannot run out. Stock is raced by strangers over something
that can. The question was never "should we use optimistic concurrency" — it was "does a silent
overwrite destroy something somebody wants".

### What is still possible, stated plainly

- **Oversell by stock take.** A count can find two skillets where three are already promised.
  `Available` clamps at zero rather than reporting −1; the shop owes one of those shoppers an
  apology, and no `Math.Max` fixes that. S15's runbook gets a procedure.
- **A held unit is not a sold unit.** A reservation expiring during a slow checkout is possible.
  Fifteen minutes is long enough to find a wallet; it is not infinite.
- **The badge is advisory.** See below.

## Decision 4 — "3 left" is a nudge, not a promise

The storefront badge is a read of a moving number. The rules:

- **"Out of stock"** is stated flatly — being wrong that way costs a sale and costs the shopper
  nothing.
- **"Only 3 left"** is urgency, not inventory. Wrong by one is survivable; reading as a reservation
  is not.
- **An exact count** only when it is high enough not to matter ("12 in stock").
- **Never "yours".** No countdown, no "2 people are looking at this". The shop makes exactly one
  promise about stock — when it writes a reservation under the token — and a badge is not that
  moment. *The UI may create urgency about a fact; it may not invent the fact.*
- **Silence when unknown.** If the count can't be fetched the badge renders nothing: "In stock" would
  be a guess in the shop's favour about the one number a shopper is deciding on.

## Decision 5 — every product has a shelf

`CreateProduct` now creates a `StockLevel` at zero, in the same unit of work. Without it, "no stock
row" was a state the system could not agree about: `GET /stock` answered "0 available" while
`POST /cart/reservations` answered "404, no such thing". Same product, same instant, two
incompatible stories — the exact contradiction this sprint spent three commits deleting from the
pricing code. Now "no stock row" means "no product", the 404 is true rather than merely usual, and
0-on-hand means what it says.

Found by `AuthorizationMatrixTests`, which had no opinion about inventory and tripped over it anyway.

## Consequences

- The domain cannot read a clock; a new ambient read fails CI with a file and a line.
- Reservations accumulate rows. The sweep marks them; pruning is not yet implemented and joins the
  **abandoned-cart reaper already owed at S15** (ADR-0007 §Consequences) as housekeeping the shop
  will need.
- `ListLowStockAsync` over-fetches by design: SQL narrows on on-hand (reservations only ever *remove*
  availability, so the approximation is safe in one direction) and the domain decides. 🔗 S14 turns
  this into a read model, and it will be a good example *because* the rule lives in one testable place.
- API tests cannot reproduce a genuine concurrent race: the suite runs on one in-memory SQLite
  connection, which is not safe for concurrent use. The interleaving is written out explicitly with
  two `DbContext`s instead — the same sequence, minus the threads. This is the SQLite/Postgres
  honesty ADR-0001 already owes.
