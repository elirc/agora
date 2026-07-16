# ADR 0001: Hexagonal Architecture, Enforced by Tests

- **Status:** Accepted
- **Date:** 2026-07-16 (Sprint 01)
- **Deciders:** [Author], [Reviewer]

## Context

Agora's thesis is that software is optimized for change: prices will gain campaigns, a second
payment provider is already scheduled to land mid-course (S12), and integrators will arrive with
requirements we haven't heard yet. The classic failure mode is not choosing a bad architecture —
it's choosing a good one and letting it erode, one convenient `using` at a time.

## Decision

Ports-and-adapters (hexagonal) across five backend projects:

| Project | Role | May reference |
|---|---|---|
| `Agora.Domain` | entities, value objects (`Slug`, `Sku`), state machines, policies | nothing |
| `Agora.Application` | use cases + ports (interfaces) | Domain |
| `Agora.Contracts` | versioned DTOs — the API's public vocabulary | nothing |
| `Agora.Infrastructure` | EF Core, adapters, dispatchers | Application, Domain |
| `Agora.Api` | endpoints, ProblemDetails, OpenAPI | Application, Contracts (+ Infrastructure **only** in `Agora.Api.Composition`) |

**The rule is executable.** `tests/Agora.ArchitectureTests` (NetArchTest) fails the build on any
forbidden dependency, including framework leaks into Domain and endpoints touching EF. Architecture
tests are never weakened to make a sprint compile; if they fight, the design is wrong.

Supporting decisions:

- **Use cases are classes without interfaces.** Ports (things with >1 implementation or a test
  seam: repositories, the clock) get interfaces; use cases don't — an interface with one
  implementation and no seam consumer is ceremony.
- **The clock is injected** (`TimeProvider`) from day one — even the seeder takes it.
- **Repositories are per-aggregate ports.** The explicit `IUnitOfWork.CommitAsync` transaction
  seam is deferred: Sprint 01's catalog slice is READ-ONLY, so there is no write to commit yet. It
  arrives with the first write (identity in S02, catalog admin in S03) — we add the seam when a
  write needs it, not before.
- **Modular monolith**, not microservices: boundaries enforced by tests are cheaper than
  boundaries enforced by networks, and teach the same discipline.

## Consequences

- Nine projects (five src + four test) for a two-table walking skeleton — visible ceremony now,
  invoiced against S11/S12 where change-locality is *measured*. The Reviewer's challenge and this
  bet are recorded in the Sprint-01 debate thread; S12's PR must show the receipt.
- Mapping code exists at every boundary (domain ↔ contract). We accept hand-written mapping; if it
  hurts, that pain is a signal worth feeling before automating.
- EF entities ARE domain entities (no separate persistence model) — the pragmatic hexagon. The
  cost: Domain classes need EF-friendly private constructors. The alternative (a full persistence
  model + mapper) buys purity we don't yet need; revisit if EF's shape ever forces a domain
  compromise beyond a private constructor.

## Rejected alternatives

- **N-tier ("Services" + "Repositories" folders in one project):** the dependency rule becomes a
  convention, and conventions erode. We want a build break, not a code-review argument.
- **Microservices:** maximal boundary enforcement, maximal operational cost; wrong trade for a
  teaching monolith and for most products this size.
- **Clean-architecture templates with MediatR + AutoMapper on day one:** every dependency must
  earn its place with a felt pain first (CONTRIBUTING requires a written why per package).
