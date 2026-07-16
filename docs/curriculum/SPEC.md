# Agora — Product Spec & 15-Sprint Curriculum

**Version:** 1.0 · **Date:** 2026-07-16 · **Repo:** `elirc/agora` · **Course:** 9 (first full-stack .NET + React course)

---

## 1. Purpose

Two goals; the second is the real one:

1. **Product:** a single-store ecommerce web application — storefront, cart, checkout, orders,
   admin back-office — in the mold of a small Shopify-powered shop. Functionality and domain
   only; never branding or assets.
2. **Learning:** every sprint is a GitHub PR engineered as a chapter of a **C#/.NET + React**
   curriculum for junior software engineers, focused on **web application development end to
   end** — business logic, API design, and the frontend that consumes it — organized around one
   thesis:

> **CRUD is never just CRUD.** The distance between a demo and a product is what happens at the
> edges of every create/read/update/delete: validation, authorization, concurrency, money, time,
> and failure. Excellence lives at the edges.

The product is the textbook. The PRs are the chapters. The architecture tests are the proctor.
The React app is where the API's promises are kept — or broken in front of the user.

## 2. Personas

| Persona | Role in the app |
|---|---|
| **Shopper** | Anonymous: browses catalog, holds a cart |
| **Customer** | Registered: cart merges on login, checks out, sees own orders |
| **Store Admin** | Back-office: catalog, pricing, inventory, promotions |
| **Ops** | Fulfillment: order pipeline, refunds, webhook subscriptions |

There is one store (no multi-tenancy). The authorization seam that matters is
**resource ownership**: a customer may see *their* orders, *their* addresses, *their* cart —
and nothing else. That seam is exercised, tested, and (once) deliberately broken.

## 3. Domain model (summary)

- **Product / Category / Sku** — catalog CRUD done excellently: slugs, soft archive, validation
  on both sides of the wire, optimistic concurrency (rowversion) so two admins can't silently
  overwrite each other.
- **Money** — a value object from S04 onward; all arithmetic in `decimal`, allocation without
  lost pennies. Prices, sale prices with **time-windowed** campaigns.
- **Cart / CartLine** — anonymous carts (cookie token) + owned carts, merge on login; quantity
  rules; price snapshots vs live prices.
- **StockLevel / Reservation** — inventory with checkout-time reservation + expiry (the clock is
  a port).
- **Order / OrderLine / Address** — checkout state machine
  `placed → paid → fulfilled → delivered` with `cancelled` / `refunded` exits; order numbers;
  the order is an immutable record of what was sold at what price.
- **Payment / PaymentIntent** — mock PSP (**PayFlow**) behind an `IPaymentGateway`
  anti-corruption layer; closed result union; vendor faults are a feature of the curriculum.
- **Promotion / Coupon** — a pluggable promotion engine (percentage, fixed-amount, threshold
  free-shipping); adding a promotion type adds files, never edits core (proved in S11).
- **Review** — product reviews through a generic, extracted **moderation workflow** (S08's
  reuse-by-extraction arc).
- **WebhookSubscription / OutboxMessage / IdempotencyRecord** — the reliability surface (S10).
- **Read models** — catalog search/browse projections, sales dashboards (S14).

## 4. API conventions (ADR-0002 expands)

- Versioned base path `/api/v1/...`; evolution by addition or new version, never breaking
  (S12 demonstrates v2 of one resource).
- Errors: RFC 7807 `application/problem+json` with stable `type` URIs
  (`https://agora.example/problems/*`).
- Pagination: cursor-based (`?cursor=`, `?limit=`), `X-Next-Cursor` response header.
- Filtering/sorting: explicit whitelisted query params per resource.
- `Idempotency-Key` honored on money-moving POSTs (mandatory from S10 middleware).
- OpenAPI committed at `docs/openapi/agora-v1.json`; a backend contract test regenerates and
  diffs it — drift fails CI. The **frontend consumes generated types** from the same document
  (`web/src/api/schema.d.ts` via `openapi-typescript`); a CI step regenerates and diffs those
  too. One contract, two enforced consumers.
- Auth: cookie-session auth for users (hand-rolled first, hardened in S13); anonymous cart token
  is a separate, unprivileged cookie.

## 5. Architecture

Backend: hexagonal / ports-and-adapters, single solution, modular monolith.
Frontend: a Vite + React + TypeScript SPA in `web/`, talking only to the versioned API.

```
src/Agora.Domain           entities, value objects (Money, Sku, Slug, DateRange), state machines,
                           promotion policies. ZERO framework dependencies.
src/Agora.Application      use cases + ports (TimeProvider, IPaymentGateway, repos, IEventSink).
src/Agora.Contracts        versioned DTOs + webhook payloads. No logic.
src/Agora.Infrastructure   EF Core (Postgres dev / SQLite tests), adapters, outbox dispatcher.
src/Agora.Api              ASP.NET Core host: endpoints, auth, ProblemDetails, OpenAPI.
tests/                     Domain.Tests · Application.Tests · Api.Tests · ArchitectureTests
web/                       Vite + React 19 + TypeScript + React Router + TanStack Query
                           + Tailwind. Vitest + Testing Library. Generated API types.
```

**The dependency rule is CI-enforced from Sprint 1** (`Agora.ArchitectureTests`, NetArchTest):
Domain references nothing; Application references only Domain; Api never touches Infrastructure
types directly (composition root excepted); Contracts stay logic-free. Architecture tests are
never weakened; if they fight, the design is wrong.

Frontend discipline mirrors it: components never call `fetch` directly — data flows through a
typed API client layer (`web/src/api/`) and TanStack Query hooks (`web/src/features/*/queries.ts`);
an ESLint boundary rule enforces this from S01.

Testing: xUnit; CsCheck property tests for Money arithmetic/allocation and promotion math.
Backend CI is DB-less, Docker-less, network-less (SQLite in-memory). Frontend CI: typecheck,
lint, Vitest, contract-types drift.

## 6. The 15 sprints

**Phase 1 — MVP (S1–S5) → `v0.5.0`** (a shopper can browse and hold a cart)

| # | Headline | Teaching focus |
|---|---|---|
| 01 | Walking skeleton: solution + React app, catalog list/get slice, arch tests, OpenAPI + generated FE types, CI | Hexagonal layout; fitness functions; one contract, two consumers |
| 02 | Identity: register/login, cookie sessions, roles, authz matrix; React auth context + guarded routes | AuthN vs authZ; ownership as a policy, not an if-statement |
| 03 | Catalog admin CRUD: categories, products, slugs, soft archive, optimistic concurrency; admin UI | "CRUD done excellently"; the lost-update problem on both ends of the wire |
| 04 | Money & pricing: Money VO, sale-price campaigns with time windows; price display components | Value objects; money discipline; time as input |
| 05 | Cart: anonymous + owned carts, merge on login, domain events → **v0.5.0** | Identity boundaries; sessions vs resources; events as facts |

**Phase 2 — Full product (S6–S15) → `v1.0.0`**

| # | Headline | Teaching focus |
|---|---|---|
| 06 | Inventory: stock levels, reservations with expiry, low-stock admin view | The clock as a port (harvest arc); TOCTOU honesty |
| 07 | Checkout & orders: addresses, shipping, order state machine, immutable order lines | State machines; price snapshots; what an order *is* |
| 08 | Reviews via extracted generic moderation workflow | Reuse by extraction, not prediction |
| 09 | Payments: PayFlow mock PSP behind an ACL, payment intents, capture, vendor faults | Anti-corruption layers; closed result unions |
| 10 | Reliability: transactional outbox + Idempotency-Key middleware + order webhooks | At-least-once delivery done honestly (integration flagship) |
| 11 | Promotion engine as plugins; add 2 promo types with zero core edits | The thesis, proved with a diff |
| 12 | **The pivot sprint**: second payment provider mid-quarter + API v2 of orders | The thesis exam; contract evolution, FE feature switch |
| 13 | Hardening: threat model, security audit, flaw harvests, rate limiting, CI security gates | Reading code like an attacker |
| 14 | Performance: catalog read models, N+1 hunt, caching, React Query tuning, code-splitting | Query shape vs object shape — on both sides |
| 15 | Production readiness: health, telemetry, runbooks, postmortem, retrospective → **v1.0.0** | Operating what you built |

## 7. Teaching machinery

- **Per-sprint ritual:** milestone + story issue + 3–6 task issues → branch `sprint-NN-slug` →
  ordered commits (domain → application → adapters → endpoints → web → docs; RED→GREEN arcs as
  real history) → PR from body file → 8–11 tagged inline comments (📘/🔍/⚠️/🔗, personas
  `[Author]` `[Reviewer]` `[Stakeholder]`) → one design-debate thread → exercises →
  squash-merge → close milestone.
- **ADR per sprint** (`docs/adr/`), numbered by sprint from S1; plus one `docs/concepts/`
  one-pager per sprint.
- **Planted flaws:** exactly five, seeded silently, harvested with failing tests in later
  sprints (≥2 in S13). Ledger is author-private (`FLAW-LEDGER.local.md`, gitignored) until the
  S15 reveal commits it as `docs/sprints/flaw-ledger.md`.
- **Commit body format:** summary line; `Why this way:`; `Notice:`; `See also:`; `Refs #N`.
- **Code markers:** `// LEARN:` `// WHY:` `// GOTCHA:` `// TRY:` (and the same in TSX).

## 8. Out of scope

Real payments, real emails, real shipping rates, product image uploads (URLs only), i18n,
mobile apps, multi-store/multi-tenant, SSR/Next.js. Vendor calls exist only as in-repo mocks
behind ports.
