# Sprint 05 — Cart: Anonymous, Owned, and Merged → v0.5.0

**Goal:** a shopper holds a cart before they have an account, and it becomes theirs on login — the
MVP ships.

## Scope

**In:** Cart + CartLine aggregate; **anonymous cart** via an unprivileged cookie token, kept
separate from the session cookie; **owned carts** once logged in; **merge on login** (anonymous ⊕
owned reconciled by SKU, quantity rules applied); quantity rules (min 1, per-line max,
archived/unavailable SKUs rejected); price-snapshot vs live-price note; **domain events via an
`IEventSink` port** (`CartItemAdded`, `CartMerged` — logging fake for now); cart drawer UI +
add-to-cart from the catalog + line quantity controls; `docs/http/cart.http`;
**tag `v0.5.0` + GitHub Release after merge**.

**Out:** inventory reservation (S6); checkout / orders (S7); webhooks & the outbox (S10 — the
`IEventSink` is the seam).

## Teaching focus

Identity boundaries: a cart is a *resource*, a session is an *identity* — two cookies for a reason.
Merge as reconciliation, a policy you choose. Events as facts about what happened, emitted from the
domain.

## Commit narrative (sketch)

1. `feat(domain): Cart + CartLine, quantity rules as guarded methods`
2. `feat(domain): merge policy — anonymous ⊕ owned, SKU-wise, quantity caps respected`
3. `feat(application): cart use cases + IEventSink port (CartItemAdded, CartMerged)`
4. `feat(infrastructure): cart persistence, anonymous cookie token, session→cart binding on login`
5. `feat(api): cart endpoints (anonymous + owned), merge-on-login hook`
6. `feat(web): cart drawer, add-to-cart, quantity controls, optimistic UI backed by a mutation`
7. `docs: ADR-0006 carts & domain events, concepts/events-as-facts.md; release notes`

## Teaching placements

- 📘 on the two cookies: an identity cookie and a resource cookie — conflating them is how carts
  leak between users.
- 📘 on merge: reconciliation is a policy (sum? max? prefer-owned?) — pick one, write it down, test
  the collisions.
- 🔍 on the merge use case: "the interesting cases are the collisions — the same SKU in both carts."
- ⚠️ on `IEventSink`: an event is a fact, emitted once the fact is *true* — its ordering against
  persistence matters more than it looks.
- 🔗 on the `web/` cart drawer: optimistic UI must reconcile with the authoritative cart — the
  drawer trusts the mutation's response, not its own guess.

## Debate

`[Reviewer]`: "Anonymous carts are a landfill of abandoned junk — hold the cart in `localStorage`
and skip the server table until checkout." `[Author]`: a client-only cart can't enforce quantity
rules, can't survive a device switch, and can't merge honestly on login; the server cart is the
source of truth the moment money is near. Reviewer accepts, extracting a promise: an abandoned-cart
reaper (S15 runbook material).

## Post-merge

Tag `v0.5.0`, publish a GitHub Release: the S1–S5 MVP arc, what Phase 2 adds, a study-order refresher.

## Exercises

- Add the same SKU to an anonymous cart and an owned cart, then log in. What quantity results — and
  was that the policy you chose?
- Archive a product (S3) that sits in a live cart. What should add-to-cart, merge, and checkout each
  do? Write the test for one.
- Make the drawer's price and the catalog's price disagree (a sale ends between renders). Which is right?
