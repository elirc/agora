# Sprint 14 — Performance: Read Models, N+1, Caching

**Goal:** answer catalog browse/search fast, without letting the write model dictate query shape —
then find and fix the queries that were lying about their cost.

## Scope

**In:** **catalog read models** (SQL projections, not entity traversal): browse-by-category with
facets/price, search by name/description, a best-sellers/sales rollup; browse/search endpoints with
explicit whitelisted query params (cursor pagination reused); **the N+1 hunt** — a query-counting EF
interceptor turned into evidence, capturing the worst offenders from earlier sprints (cart-with-
promotions, order lists, moderation inbox) with before/after query counts in the commit bodies;
**response caching** for the read models + a **cache invalidation ledger** (each entry: trigger +
staleness budget), evicted on catalog/price writes; frontend React Query tuning (`staleTime`, query
keys) + **route code-splitting** + search UI; `docs/http/catalog-search.http`.

**Out:** a search engine (Elastic/OpenSearch); materialized views; background report generation; BI
tooling.

## Teaching focus

Query shape ≠ object shape — the aggregate that's right for writes is wrong for browse. Measure
before optimizing (interceptor counts as commit-body evidence). Caching as a liability with
invalidation written down *before* the cache exists.

## Commit narrative (sketch)

1. `feat(application): catalog read models as SQL projections + IReadModelQueries port`
2. `feat(api): browse/search endpoints + explicit query params`
3. `perf: cart-with-promotions N+1 — N queries → 2` (interceptor log before/after in the body)
4. `perf: order list + moderation inbox — same treatment`
5. `feat(infrastructure): response caching for read models + invalidation ledger (evict on write)`
6. `feat(web): React Query tuning + route code-splitting + search UI`
7. `docs: ADR-0017 read models & the cache ledger, concepts/query-shape.md`

## Teaching placements

- 📘 on read models: CQRS-lite without the tax — separate the read path, skip the message bus.
- 📘 on the cache ledger: every entry documented with its invalidation trigger and staleness budget;
  a cache without both is a bug on a timer.
- 🔍 on commit 3: "perf commits without measurements are vibes — before/after query counts or it
  didn't happen."
- ⚠️ on invalidation: a sale that ends must evict the cached price — stale money is a support
  incident; name the eviction path.
- 🔗 on the `web/` React Query tuning: server cache and client cache are two caches with two
  staleness budgets — set `staleTime` deliberately, or the UI shows a price the server already changed.

## Debate

`[Reviewer]`: "Raw SQL projections abandon the type safety we've preached for 13 sprints."
`[Author]`: the safety we preach is *domain* safety; read models are read-only views where the schema
is the contract, and EF's LINQ-to-SQL is its own bug class at this complexity. SQL lives behind
`IReadModelQueries` with result records, integration-tested on SQLite and Postgres-dialect-audited.
Reviewer extracts a promise: any read used for a *decision inside the system* moves back to the
domain. Recorded.

## Exercises

- Add "revenue by category, last 30 days" as a fourth read model. SQL-first or domain-first? Why?
- Turn on the query interceptor and find an N+1 we didn't fix (there's at least one). Fix it.
- Delete one cache's invalidation line. Describe the blast radius and how long until someone notices.
