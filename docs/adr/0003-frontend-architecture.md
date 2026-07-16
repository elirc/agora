# ADR 0003: Frontend Architecture — the API's Promises, Kept in the Browser

- **Status:** Accepted
- **Date:** 2026-07-16 (Sprint 01)
- **Deciders:** [Author], [Reviewer]

## Context

Agora is the course's first full-stack sprint. The React storefront is not a spectator to the API —
it is the second enforced consumer of the contract, and the place where the API's promises are kept
(or broken) in front of a shopper. This ADR is its own document, separate from ADR 0002, because
the frontend has a discipline of its own that is worth stating as loudly as the backend's — not a
footnote to the API conventions.

## Decision

A Vite + React 19 + TypeScript (strict) SPA in `web/`, with three enforced rules:

1. **Generated types, not hand-written ones.** `web/src/api/schema.d.ts` is generated from
   `docs/openapi/agora-v1.json` by `openapi-typescript`. A CI step (`npm run api-types:check`)
   regenerates in memory and fails on drift, exactly as the backend's OpenAPI drift test does. The
   server and the client cannot disagree about the shape of a product without a red build on both
   sides.
2. **Components don't fetch.** Network access lives behind one seam, `web/src/api/client.ts` — a
   thin typed wrapper over `fetch`. Components read server state through TanStack Query hooks
   (`web/src/features/*/queries.ts`); they never call `fetch` or an HTTP library directly. This is
   enforced by an ESLint boundary rule (`no-restricted-globals` for `fetch`, exempting `src/api/`) —
   the frontend's fitness function, the mirror of the backend's architecture tests.
3. **Server state and UI state are different things.** Server state (products, categories) lives in
   TanStack Query — cached, keyed, and paginated by the library. UI state that must survive a
   reload or be shareable (the selected category filter) lives in the URL. Components own almost no
   `useState`.

Structure: `web/src/api/` (the client seam) → `web/src/features/<area>/` (queries + components) →
`web/src/pages/` (composition) → the router. Tailwind v4 for styling, no component library.

## Consequences

- A field rename on the server is a compile error in the browser, caught before merge — the payoff
  of generated types. The cost is a generation step in the workflow, scripted so it can't be
  skipped.
- Testing mocks at the client seam (`src/api/client`), never at component internals — so tests
  exercise the real hooks, the real grid, the real pagination, faking only the network. This mirrors
  the backend's in-memory repository fakes.
- Cursor pagination in the UI is `useInfiniteQuery` following `X-Next-Cursor` — the same header the
  API returns and the `.http` file demonstrates. "Load more" is a `fetchNextPage`.

## Rejected alternatives

- **Hand-maintained TypeScript interfaces mirroring the DTOs:** they rot silently the first time
  someone edits one side and not the other. Generation makes drift a build failure, not a bug report.
- **`fetch` calls inside components with `useEffect`:** re-invents caching, dedup, loading/error
  state, and pagination badly, and scatters the network across the tree. TanStack Query owns server
  state; the ESLint rule keeps `fetch` out of components.
- **Next.js / SSR:** out of scope (SPEC §8). The storefront is a client app against a versioned API;
  SSR is a different product with a different operational cost.
