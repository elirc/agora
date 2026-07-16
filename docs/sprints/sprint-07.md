# Sprint 07 — Checkout & Orders: State Machine, Snapshots, Ownership

**Goal:** a cart becomes an order — an immutable record of what was sold, at what price, moving
through a guarded state machine.

## Scope

**In:** Address (customer-owned); shipping methods (flat options, v1); order placement: cart → Order
with **immutable OrderLines** carrying price snapshots (list + sale resolved at placement) + SKU +
name; **order state machine** `placed → paid → fulfilled → delivered` with `cancelled` / `refunded`
exits, guards per transition; collision-safe human-readable **order numbers**; reservation
consumption (S6) at placement; customer "my orders" (own only) + Ops order pipeline; **checkout
wizard UI** (address → shipping → review → place) + confirmation; `docs/http/checkout.http`.

**Out:** payment capture (S9 — placement produces an *unpaid* order; `paid` is entered by a stub);
refund execution (S9/S15); tax and real shipping rates.

## Teaching focus

State machines as guarded transitions, not status-column writes. Price snapshots — the order
remembers what it cost even after the catalog changes. Resource ownership: a customer sees *their*
orders and nothing else.

## Commit narrative (sketch)

1. `feat(domain): Address + Order/OrderLine with immutable snapshots`
2. `feat(domain): order state machine + transition table (pure)`
3. `test(domain): exhaustive transition matrix — legal and illegal exits`
4. `feat(domain): order-number generator (collision-safe, monotonic-enough)`
5. `feat(application): place-order use case — consume reservation, snapshot prices, emit OrderPlaced`
6. `feat(infrastructure+api): persistence, order endpoints (my-orders by owner, Ops pipeline)`
7. `feat(web): checkout wizard + confirmation + my-orders page`
8. `docs: ADR-0008 order state machine & immutability, concepts/what-an-order-is.md`

## Teaching placements

- 📘 on the transition table: state changes with business meaning get named transitions, not
  PATCH-to-status.
- 📘 on price snapshots: an order is an immutable record; the catalog is a moving target — prove
  the snapshot with a test that edits the product afterward.
- 🔍 on the my-orders read: "read this as an attacker — is the owner check in the query, or in your
  head? An order id in the URL is not permission to read it."
- ⚠️ on the transition matrix: illegal transitions must be unrepresentable, not merely rejected at
  the edge.
- 🔗 on the `web/` checkout wizard: the client's step order is UX; the server re-checks every
  invariant — a forged "review" step must still fail server-side.

## Debate

`[Reviewer]`: "`placed→paid→fulfilled→delivered` plus `cancelled`/`refunded` is a lot of states for
v1 — a status enum and a few guards ships faster." `[Author]`: the states are the order's real life,
and each transition carries different data and different authorization (who may cancel ≠ who may
fulfil); collapsing them loses the per-transition contract and the audit trail. Reviewer accepts the
named transitions but wins a smaller point — `refunded` is reachable only from `paid`/`fulfilled`/
`delivered`, never `placed`; a follow-up commit tightens the table.

## Exercises

- Place an order, then change the product's price and name. Fetch the order and prove nothing moved.
- Add a `returned` terminal state reachable only from `delivered`. How many files? Why so few?
- Try to fetch another customer's order by guessing its id. Write the test for what happens today,
  and keep it.
