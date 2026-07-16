# ADR 0002: API Conventions — the Contract Before the Features

- **Status:** Accepted
- **Date:** 2026-07-16 (Sprint 01)
- **Deciders:** [Author], [Reviewer], [Stakeholder]

## Context

Agora's API is the treaty between the C# backend and the React storefront — and, eventually,
between Agora and outside integrators. Conventions decided per-endpoint become inconsistencies;
conventions decided once, in the walking skeleton, become rails every later sprint runs on.

## Decisions

1. **Versioned base path** `/api/v1/...`. After a version ships, evolution is by addition or by a
   new version — never a breaking change (S12 demonstrates v2 of one resource).
2. **Errors are RFC 7807 `application/problem+json`** with stable `type` URIs
   (`https://agora.example/problems/<slug>`). Three families in Sprint 01:
   - `not-found` → 404 (also used where the caller may not learn whether a resource exists)
   - `invalid-request` → 400 (malformed cursor, out-of-range limit)
   - `domain-rule-violated` → 422 (well-formed request; the business vocabulary said no — e.g. a
     path segment that is not a valid slug)
   Clients switch on `type`, so `detail` text can improve without breaking anyone. More families
   (401/403 with auth in S02, 429/503 later) join the same one translation table.
3. **Cursor pagination** on every list: `?cursor=` (opaque base64url token) + `?limit=`
   (1..100, default 25, out-of-range **rejected**, not clamped). The next-page token is returned in
   the **`X-Next-Cursor` response header**; the body stays a plain array of the resource. Ordering
   is `(CreatedAtUtc DESC, Id DESC)` with the Id tiebreak mandatory.
4. **Filtering is a per-resource whitelist.** `GET /products` accepts exactly one filter param —
   `?category=<slug>`. Unlisted query params are ignored, never honored, so a typo can't silently
   widen a query.
5. **OpenAPI is committed** (`docs/openapi/agora-v1.json`) and drift-gated: a backend contract test
   regenerates the served document and diffs it. Intentional changes are blessed via
   `AGORA_UPDATE_OPENAPI=1 dotnet test`. **The same document has a second consumer:** the React
   app's API types are generated from it (`web/src/api/schema.d.ts` via `openapi-typescript`) and a
   CI step re-diffs them. One contract, two enforced consumers.
6. **Timestamps** are UTC `DateTimeOffset` serialized ISO-8601.
7. **`Idempotency-Key`** will be honored on all money-moving POSTs (arrives with S10 middleware; the
   convention is contractual from day one so partners can build against it).

## Why header-cursor instead of an envelope?

An envelope (`{ items: [], next: "..." }`) types every list twice in OpenAPI and forces clients to
unwrap; a header keeps resource payloads clean and generic clients can share one walker. Cost:
header metadata is invisible to naive JSON-only tooling — accepted, and the `.http` file plus the
frontend client demonstrate the loop. (The Reviewer preferred the envelope; the debate on the
Sprint-01 PR records the trade and the tiebreaker: our two primary consumers — a generated
TypeScript client and scripted integrators — both read headers happily.)

## Consequences

- Every new list endpoint inherits the cursor pattern (and its index) for free.
- The drift gate makes "update the spec" unforgettable — and makes PR diffs show contract changes
  in reviewable JSON, then in a reviewable TypeScript diff.
- Rejecting bad limits (rather than clamping) surfaces client bugs at development time.
