# Sprint 06 — Inventory & the Clock

**Goal:** stock that reserves at checkout and expires on a clock we control — and the sprint where a
time-handling defect shipped in an earlier chapter finally fails a test out loud.

## Scope

**In:** StockLevel per SKU (on-hand, reserved); **checkout reservations** with an expiry window (the
clock is a port); reservation release on expiry / on order placement; low-stock admin view; **the
harvest arc** (below); an `IClock` port introduced properly + a **source-scan fitness test banning
ambient time** in `Agora.Domain`; low-stock React admin view + stock badges on the catalog;
`docs/http/inventory.http`.

**Out:** multi-warehouse; backorders; the checkout that *consumes* reservations (S7 — reservations
exist first); restock / purchase-order workflows.

## Teaching focus

Date/time arithmetic is business logic, not utility code. The clock as a dependency. TOCTOU honesty
— reserving stock is a check-then-act stretched across time, and we name the race rather than pretend
the window closed it.

## The harvest arc — in-PR commits

1. **RED (bug report):** "a shopper reports a sale that ended yesterday still showing / a countdown
   off by a day." A test reproduces it with a fake clock in a non-server time zone; a time-windowed
   behavior resolves against the wall clock, not an injected one.
2. **RED (fitness test):** a source-scan of `Agora.Domain` for ambient time reads
   (`DateTime.Now`/`UtcNow`, `DateTimeOffset.Now`) goes red — the ban is now enforceable in CI.
3. **Fix:** `IClock` injected wherever time is read; the offending resolution moves onto the port;
   the source-scan and both RED tests go green (grep list in the commit body).

## Commit narrative (sketch)

1–3. The harvest arc above · 4. `feat(domain): StockLevel + Reservation with an expiry window (pure, clock-driven)`
· 5. `feat(application+infrastructure): reserve/release use cases, expiry sweep, persistence`
· 6. `feat(api+web): low-stock admin view, stock badges, reservation endpoints`
· 7. `docs: ADR-0007 the clock is a port, concepts/the-clock.md`

## Teaching placements

- 📘 on `IClock`: controllable time; static clocks make tests lie and bugs unreproducible.
- 📘 on the fitness test: you can ban a *category* of bug in CI — a source-scan that fails on
  ambient time.
- 🔍 on the RED commit: "read a bug fix commit-first — the failing test is the spec of the bug."
- ⚠️ on reservations: reserve is check-then-act; two checkouts racing for the last unit is a TOCTOU
  we name, not paper over.
- 🔗 on the `web/` stock badge: "3 left" is a read from a moving number — decide whether the badge
  is advisory or a promise, because the UI is where the race becomes visible to a customer.

## Debate

`[Reviewer]`: "Reservations with expiry are a scheduler you now have to operate — just decrement
stock at checkout and be done." `[Author]`: decrement-on-add oversells during long checkouts and
decrement-never oversells at the finish line; a short reservation is the honest middle, and its
expiry is testable precisely because the clock is a port. Reviewer accepts, demanding the expiry
sweep be idempotent and covered (foreshadows S10).

## Exercises

- Set the fake clock across a DST boundary and re-run the reservation-expiry test. Does the window
  still mean what you think?
- Add a fresh ambient-time read to `Agora.Domain` and watch the fitness test name the file — the
  locations of *your* mistakes are fair game.
- Two shoppers reserve the last unit. Write the test for what actually happens today, then argue
  whether it's acceptable.
