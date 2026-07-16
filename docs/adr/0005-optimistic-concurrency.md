# ADR 0005: Optimistic Concurrency, Soft Archive, and Immutable Slugs

- **Status:** Accepted
- **Date:** 2026-07-16 (Sprint 03)
- **Deciders:** [Author], [Reviewer]

## Context

Sprint 03 gives the Store Admin write access to the catalog: create, edit, and archive categories and
products. The moment two people can edit the same row, "CRUD" stops being trivial. Three decisions
define the sprint, and this ADR records all three because they interlock.

1. **How do two admins editing the same product avoid silently overwriting each other?**
2. **What does "delete a product" mean when past orders and live links still point at it?**
3. **When an admin renames a product, does its URL change?**

## Decision 1 — Optimistic concurrency via an explicit, code-managed `Version` token

Every `Product` and `Category` carries an `int Version`, born at `1` and incremented in the domain on
**every** mutation. It is configured as EF Core's concurrency token (`IsConcurrencyToken()`), so it
joins the `WHERE` clause of every generated `UPDATE`. A write only lands if the row is still at the
version the writer loaded; otherwise it is rejected.

Detection happens in **two layers**, both surfacing the same `409`:

- **Application pre-check (the common case).** An update use case loads the row, compares
  `entity.Version` to the caller's `expectedVersion`, and — on mismatch — throws
  `ConcurrencyConflictException` carrying the **current entity**. This is what powers the 409 body's
  current-state snapshot, and it is the path the tests and the React conflict UX exercise.
- **EF concurrency token (the true race).** If a second writer commits in the sliver of time between
  our load and our `SaveChanges`, the pre-check passed but the `UPDATE` matches zero rows and EF
  throws `DbUpdateConcurrencyException`. That EF type is translated to the same
  `ConcurrencyConflictException` in **one place** — `EfUnitOfWork.CommitAsync` — because EF types may
  not cross into the Api (the architecture test forbids it). No snapshot on this path; the client
  re-fetches.

### Why an explicit `int`, not SQL Server `rowversion` / Postgres `xmin`

The provider-native tokens are tempting — the database maintains them for free. But **the test suite
runs on in-memory SQLite**, which has neither. A `rowversion`/`xmin` design would leave the entire
concurrency story *untested on the provider the tests actually use*, which is exactly backwards for
the sprint whose whole point is concurrency. An explicit `int` we manage in the domain behaves
**identically on Postgres, SQLite, and in the pure in-memory fakes**. The cost — remembering to
`Version++` on each mutation — is paid once per mutator and covered by domain tests.

### Why `409` with the current state in the body

RFC 7807 lets a problem response carry extension members. Our `409` includes a `current` member — the
admin projection of the row as it stands now (`https://agora.example/problems/conflict`). A `409` the
client can't act on is barely better than a lost update: the user still doesn't know what changed. By
handing back the winning version, the UI can show a field-by-field diff and offer **reload** (take
theirs) or **overwrite** (rebase my edit onto their version and re-save). See
`docs/concepts/lost-update.md` for the exact interleaving.

### Why the expected version travels in the body, not as `If-Match` / ETag

Both are legitimate optimistic-concurrency transports. We chose **version-in-body** for minimal
ceremony:

- No ETag parsing, no weak/strong validator rules, no `If-Match` precondition wiring.
- The archive/restore POSTs carry the version the same way — one shape across every mutation.
- The React form binds an `expectedVersion` field directly; nothing reads response headers to save.

**When ETags would win:** if we also wanted conditional **GET**s (`If-None-Match` → `304`) and HTTP
cache revalidation, an ETag would do double duty as both cache validator and concurrency token. Agora
has no such need yet. If catalog read-caching arrives (S14 territory), revisit — an ETag derived from
`Version` would layer on without breaking the body contract.

## Decision 2 — Soft archive: a state, not a `DELETE`

Archiving sets `Product.ArchivedAtUtc`; `IsArchived` is computed from it (a single source of truth, so
it is deliberately unmapped — see `ModelMappingTests`). Archived products **leave the storefront**
(the list excludes them, detail-by-slug 404s) but **remain in the database**: past orders still
resolve them (S07), and their slug and SKU stay reserved so a create can't collide with a "deleted"
row that would reappear on restore. Restoring clears the timestamp.

The consequence rippled into the read ports: **every catalog query now names a `ProductStatusFilter`**
(`Active` / `Archived` / `All`). There is no ambient default — the storefront asks for `Active`, the
admin list defaults to `All`. Making "which rows?" an explicit argument is what stops an archived row
from silently leaking to a shopper, or a legitimate admin view from silently hiding one.

**Scope:** archive is product-only in S03. Categories are created and renamed but not archived — a
category with no active products is harmless, and cascading an archive across a category's products is
a larger decision deferred until it's needed.

## Decision 3 — Slugs (and SKUs) are immutable identifiers

A rename changes a product's `Name`, never its `Slug`. The slug is a **permalink**: external links,
bookmarks, and search-engine results point at `/products/{slug}`, and a rename that re-slugged would
`404` every one of them. The SKU is a warehouse identity printed on labels; it doesn't move because a
title changed. So `UpdateDetails` accepts neither. Slugs are **generated** from the name at creation
(`Slug.Generate`, or an explicit slug the admin supplies) and frozen thereafter. A true "change the
URL" operation is create-plus-redirect, not an edit, and is out of scope.

## Consequences

- `IUnitOfWork.CommitAsync` gains its first real failure translation (EF concurrency → application
  conflict). The seam introduced in S02 keeps earning its keep; the S10 outbox joins the same method.
- The authorization matrix grows its **first `403` rows**: every `/admin/*` endpoint is StoreAdmin-only
  (Ops included in the deny set — roles are capabilities, not a hierarchy).
- Two DTO shapes for one entity: the storefront `ProductDto` (no version, no archive) and the
  `AdminProductDto` (both). The mapping edge decides who sees what.
- A `Version` column defaulting to `0` for rows that pre-date the migration; rows created through the
  domain start at `1`. On a fresh/seeded database every product is `1`. A populated Postgres database
  migrated in place would want its existing rows backfilled to `1` (see the migration note).

## Rejected alternatives

- **Last-write-wins (no concurrency control).** The Reviewer's opening position; see the design-debate
  thread on the PR. Rejected: the lost update is both invisible and irreversible — the worst pairing —
  and the fix is one column plus one catch clause.
- **`rowversion` / `xmin`.** Rejected: untestable on SQLite; provider-specific mechanics for a
  cross-provider codebase.
- **Hard delete.** Rejected: breaks referential history (orders), frees a slug/SKU that a restore
  would need, and turns an "oops" into data loss.
- **`If-Match` / ETag concurrency.** Reasonable; deferred until conditional GETs / read-caching make an
  ETag pull double duty (Decision 1).
- **Regenerating slugs on rename.** Rejected: breaks every existing link — the entire reason a slug is
  a value worth freezing.
