# Sprint 11 — Promotion Engine as Plugins (The Thesis Proof)

**Goal:** promotions become plugins behind a registry — then two more promo types are added as
new files only, with a `git diff --stat` proving the core never moved.

## Scope

**In:** `IPromotionRule` contract + registry / discovery (assembly scan of a promotions namespace);
three initial rules migrated behind it — **percentage-off, fixed-amount-off, threshold
free-shipping**; **Coupon** entry (code → applicable rules, validity window, usage limits);
deterministic cart recomputation applying promotions with a defined + tested precedence; **the
proof:** two additional promo types (e.g. buy-X-get-Y and category-percentage) added as **pack files
only**, PR body carries `git diff --stat` per layer showing core untouched; coupon-entry UI +
promotion breakdown in cart/checkout; `docs/http/promotions.http`.

**Out:** stacking beyond the defined precedence (kept simple, written down); per-customer targeting /
segments; a promotion-scheduling UI.

## Teaching focus

The open-closed principle with receipts. Plugin contracts: how wide is too wide. The course's thesis
proved with a diff, not a promise.

## Commit narrative (sketch)

1. `refactor(domain): IPromotionRule contract + registry; percentage/fixed/free-shipping rewired, behavior pinned`
2. `feat(domain): promotion application pipeline — precedence + Money allocation across lines` (+ CsCheck property tests)
3. `feat(application): Coupon + apply-coupon use case (validity window, usage limits)`
4. `feat(domain/promotions): add buy-X-get-Y as a pack file only` ← the thesis payoff
5. `feat(domain/promotions): add category-percentage as a pack file only`
6. `feat(api+web): coupon entry + promotion breakdown in cart/checkout`
7. `docs: ADR-0013 promotion engine as plugins, concepts/open-closed.md` (+ diff-stat table in PR body)

## Teaching placements

- 📘 on `IPromotionRule`: designing a plugin contract — minimal surface, versioned expectations.
- 📘 on the application pipeline: promotions compose over `Money` — allocation and precedence are
  where the pennies hide.
- 🔍 on commits 4–5' diffs: "two new promotions, zero core edits — the `diff --stat` is the argument."
- ⚠️ on assembly scanning: discovery magic vs explicit registration — what we chose and its cost.
- 🔗 on the `web/` promotion breakdown: the UI must show *why* a price dropped (which rule, how much)
  — an opaque discount is a support ticket waiting to open.

## Debate

`[Reviewer]`: "A registry with assembly scanning is spooky action at a distance — a promotion goes
live because a file exists. Register rules explicitly in the composition root." `[Author]`: explicit
registration is honest but re-introduces a core edit per promotion — exactly the coupling this sprint
disproves; scanning is bounded to one namespace and the active set is logged at startup so 'what's
loaded' stays observable. Reviewer partially wins: rules are scanned, but the active set is asserted
in a manifest test — a follow-up commit adds it.

## Exercises

- Add a third new promotion (your choice) as pack files only. Attach your own `diff --stat`. Zero core?
- Stack a coupon and a sale on the same line. Which precedence fires, and do the pennies still sum?
- Rename a method on `IPromotionRule`. Count how many failures name the problem clearly.
