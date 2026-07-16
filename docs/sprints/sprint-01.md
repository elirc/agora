# Sprint 01 — Walking Skeleton

**Goal:** the thinnest end-to-end slice — a read-only catalog served over real architecture, and
consumed by a real React app — with every quality gate already armed on day one.

## Scope

**In:** `Agora.slnx` (5 src + 4 test projects); Vite + React 19 + TS SPA scaffold in `web/`;
Product/Category read-only vertical slice (seeded, list + get) with a `Sku` value object;
EF Core (SQLite-for-tests / Postgres-for-dev); `Agora.ArchitectureTests` (NetArchTest)
dependency-rule suite as a CI gate; OpenAPI generated + committed at `docs/openapi/agora-v1.json`
+ a drift contract test; frontend generated types (`web/src/api/schema.d.ts` via
`openapi-typescript`, `npm run api-types:check`); ProblemDetails mapping (`agora.example/problems/*`);
cursor pagination (`?cursor=`, `?limit=`, `X-Next-Cursor`); CI with **backend and frontend jobs**;
docker-compose Postgres; seed command; ESLint components-don't-fetch boundary rule; typed API
client + a TanStack Query hook; catalog list + product detail pages; `docs/http/catalog.http`.

**Out:** auth (S2 — endpoints are anonymous for one sprint and say so loudly); any writes (S3).

## Teaching focus

The hexagon as *executable* rules, not a diagram. Fitness functions. One contract, two enforced
consumers (backend OpenAPI drift + frontend generated-types drift). Why the skeleton carries
conventions — pagination, ProblemDetails, contract drift — before it carries features.

## Commit narrative (sketch)

1. `chore: solution + 5 src/4 test projects, dependency rule wired` — ArchitectureTests
   RED-capable (one deliberately failing assertion demoed in the body, then fixed).
2. `feat(domain): Product + Category + Sku value object (read-only shape)`
3. `feat(application): catalog queries + repository ports + cursor pagination primitives`
4. `feat(infrastructure): EF Core model, SQLite/Postgres providers, seed command`
5. `feat(api): catalog endpoints, ProblemDetails, OpenAPI generate + drift contract test`
6. `feat(web): React scaffold, typed client + schema.d.ts, catalog list/detail, boundary lint`
7. `docs: ADR-0001 hexagonal architecture, ADR-0002 API conventions, concepts/fitness-functions.md`

## Teaching placements

- 📘 on ArchitectureTests: fitness functions — architecture you can fail in CI, not just draw.
- 📘 on `Sku`: value objects vs primitive obsession, first contact.
- 🔍 on the endpoint file: "an endpoint should read like a table of contents."
- ⚠️ on EF configuration: why `Agora.Domain` never references EF Core — and what that costs.
- 🔗 on `web/src/api/schema.d.ts`: one OpenAPI document, two drift-checked consumers; the
  frontend's types trace straight back to ADR-0002 — the API contract is a promise, twice enforced.

## Debate

`[Reviewer]`: "Nine projects and a whole SPA for two read-only tables is ceremony. Fold Contracts
into Api, Application into Domain, serve HTML, and revisit when it hurts." `[Author]` defends:
boundaries are cheapest on day one; the pivot sprint (S12) will invoice the difference in a diff
we can measure. Reviewer concedes conditionally — "if S12's diff isn't local, I was right."

## Exercises

- Add a `GET /api/v1/categories/{slug}` read without touching Infrastructure. What blocked you?
  (Nothing — explain why.)
- Make Application reference Infrastructure and read the exact architecture-test failure.
- Break `docs/openapi/agora-v1.json` on purpose. Which drift test fails first — backend or
  frontend — and why do both exist?
