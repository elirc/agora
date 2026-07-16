# Sprint 03 — Catalog Admin CRUD & the Lost-Update Problem

**Goal:** CRUD done excellently. Two admins editing the same product can't silently overwrite each
other, and validation is enforced on both sides of the wire.

## Scope

**In:** Category + Product create / update / **soft-archive** (StoreAdmin only); `Slug` value
object with generation + uniqueness; validation in the **domain** and mirrored in the React forms;
**optimistic concurrency via rowversion** → `409 problem+json` carrying the current server state +
a React conflict-resolution UX (reload / discard / overwrite); admin catalog UI (list, create/edit
forms, archive toggle); `docs/http/catalog-admin.http`.

**Out:** pricing & sale campaigns (S4 — products carry a plain price primitive until Money lands);
inventory (S6); product images beyond URLs.

## Teaching focus

The lost-update problem end to end — from the rowversion column, through the 409, to the moment the
UI tells one admin their edit is stale. Validation that lives in the domain and is *echoed*, not
re-invented, at the edge.

## Commit narrative (sketch)

1. `feat(domain): Slug value object + Category/Product with validation invariants`
2. `feat(domain): soft-archive semantics — archived leaves the catalog, keeps its history`
3. `feat(application): create/update/archive use cases + concurrency-token contract`
4. `feat(infrastructure): EF rowversion mapping; conflicts surface as a typed concurrency error`
5. `feat(api): admin endpoints, 409 problem+json with the current server state`
6. `feat(web): admin forms with mirrored validation + conflict UX on 409`
7. `docs: ADR-0004 optimistic concurrency, concepts/lost-update.md`

## Teaching placements

- 📘 on rowversion: the lost-update problem, and why last-write-wins is silent, unrecoverable data loss.
- 🔍 on the update use case: "trace two concurrent edits — which one gets the 409, and what does it see?"
- ⚠️ on soft-archive: archived is a *state*, not a `DELETE`; every catalog query must now declare
  whether it wants archived rows.
- 🔗 on the `web/` conflict UX: a 409 the UI swallows is worse than no concurrency control at all —
  the user must be shown the other admin's version before choosing.

## Debate

`[Reviewer]`: "Rowversion is premature — one store, one admin; last-write-wins is fine and the 409
UX is work nobody hits." `[Author]`: 'one admin' is an assumption that expires the day Ops gets edit
rights; the lost update is both invisible and irreversible, the worst pairing, and the cost is one
column plus one catch clause. Reviewer accepts, asking the concept one-pager to show the exact
interleaving that loses the write.

## Exercises

- Disable the rowversion check, PUT from two clients, and show which change vanished.
- Validate a slug in the domain and in the form, then make them disagree. Which error does the user
  see, and is it the right one?
- Archive a product that would sit in a cart (carts arrive in S5). Sketch what add-to-cart and
  merge must do about it.
