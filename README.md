# Agora — an Ecommerce Web App, Built as a Curriculum

Agora is a **single-store ecommerce web application** in the mold of a small Shopify-powered
shop: a storefront where shoppers browse a catalog, hold a cart, and check out; and an admin
back-office where the store team manages products, pricing, inventory, promotions, and orders.

It is also — and primarily — a **teaching repository**. The product is real, but the way it was
built is the point: **15 sprints, each shipped as one GitHub Pull Request** engineered as a
chapter of a course that takes a junior engineer toward mid-level in **C#/.NET, React/TypeScript,
and web application development end to end**.

> **Course 9** in the git-connected upskilling series. Predecessors: GitJira, Tracer, Relay,
> Folio, Harbor, Pulse (JS/TS) and Atlas (C#/.NET, API-only). Agora is the first full-stack
> .NET + React course.

## The thesis

> **CRUD is never just CRUD.**

Every feature in Agora starts as an innocent create/read/update/delete — and every sprint shows
where the real product work lives: at the edges. Validation on both sides of the wire. Ownership
checks that don't rot. Two admins editing the same product. Money that doesn't lose pennies.
Sale campaigns that start at the right moment in every timezone. Payments that survive a retry.
The course proves it mechanically, not rhetorically:

- **Architecture-fitness tests** (`tests/Agora.ArchitectureTests`) enforce the dependency rule
  in CI from Sprint 1. Violate the hexagon and the build fails.
- **One contract, two enforced consumers**: the committed OpenAPI document is drift-checked by a
  backend contract test *and* regenerated into the React app's API types — both gates fail CI
  if either side wanders.
- **The promotion engine is plugins**: Sprint 11 adds two promotion types with **zero core-code
  changes** and shows the diff to prove it.
- **Sprint 12 is a pivot exam**: a second payment provider lands mid-quarter alongside an API v2
  of one resource, and the PR *measures* diff locality per layer.
- The **reliability surface** (transactional outbox, Idempotency-Key middleware, order webhooks)
  treats "money moved but the response was lost" as a first-class requirement.

## What Agora does

| Domain | Highlights |
|---|---|
| Catalog | Categories, products, slugs, soft archive, optimistic concurrency for admin edits |
| Pricing | `Money` value object, sale campaigns with time windows, price display done honestly |
| Cart | Anonymous carts, merge on login, quantity rules, price snapshots vs live prices |
| Inventory | Stock levels, checkout-time reservations with expiry, low-stock admin view |
| Checkout & orders | Address, shipping, `placed → paid → fulfilled → delivered` state machine |
| Payments | Mock PSP ("PayFlow") behind an anti-corruption layer; closed result unions; vendor faults |
| Promotions | Pluggable engine — percentage, fixed-amount, threshold free-shipping, and counting |
| Reviews | Product reviews through a generic, extracted moderation workflow |
| Reliability | Transactional outbox, idempotency middleware, signed order webhooks |
| Reporting | Sales dashboards and catalog search as read models, not entity-graph traversal |

Personas: **Shopper** (anonymous), **Customer**, **Store Admin**, **Ops**. There is one store;
the authorization seam that matters is *resource ownership* — a customer sees their orders and
nobody else's.

We clone the *functionality and domain* of small-shop platforms — never branding, copy, or assets.

## Stack

**Backend** — C# / .NET 10, ASP.NET Core minimal APIs, EF Core (Postgres dev / SQLite tests),
xUnit + CsCheck property tests, NetArchTest fitness functions, RFC 7807 errors, committed OpenAPI.
CI needs no database, no Docker, no network.

**Frontend** — Vite + React 19 + TypeScript in `web/`: React Router, TanStack Query, Tailwind,
Vitest + Testing Library, and API types generated from the committed OpenAPI document.

```
src/
  Agora.Domain           pure business logic — zero framework dependencies
  Agora.Application      use cases + ports (TimeProvider, IPaymentGateway, repositories, ...)
  Agora.Contracts        versioned request/response DTOs + webhook payloads
  Agora.Infrastructure   EF Core, adapters, outbox dispatcher, migrations
  Agora.Api              ASP.NET Core host: endpoints, auth, ProblemDetails, OpenAPI
tests/
  Agora.Domain.Tests | Agora.Application.Tests | Agora.Api.Tests | Agora.ArchitectureTests
web/                     the React storefront + admin SPA
```

## How to study this repo

Read **[LEARNER-GUIDE.md](LEARNER-GUIDE.md)** first. Short version: study each sprint's **merged
PR** in order — stakeholder issue → commits one by one → inline review comments → design-debate
thread → exercises. The playbooks in `docs/sprints/` say what each sprint teaches; the ADRs in
`docs/adr/` record why the code is shaped the way it is.

Milestones: `v0.5.0` (MVP — browsable storefront with a working cart, after Sprint 5) and
`v1.0.0` (shipped, after Sprint 15).

## Running Agora

```bash
docker compose up -d                                  # local Postgres
dotnet run --project src/Agora.Api -- migrate seed    # schema + demo catalog
dotnet run --project src/Agora.Api                    # API (Swagger UI at /swagger in Development)
cd web && npm install && npm run dev                  # storefront at http://localhost:5173
```

Every feature area has a committed `.http` file under `docs/http/` so you can replay each flow.

Verify the whole solution the way CI does:

```bash
dotnet build -warnaserror && dotnet test && dotnet format --verify-no-changes
cd web && npm run typecheck && npm run lint && npm test && npm run api-types:check
```
