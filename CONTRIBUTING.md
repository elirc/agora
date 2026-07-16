# Working Agreements

These are the team rules Agora is built under. They are enforced socially in review and
mechanically in CI.

## The five Agora rules

1. **The Domain stays pure.** `Agora.Domain` references no framework, no EF Core, no ASP.NET, no
   JSON library. Business logic lives in Domain/Application with tests to match. An endpoint, EF
   configuration, or React component containing business rules is a failed review.
2. **The dependency rule is law.** Domain ← Application ← (Infrastructure | Api), Contracts stay
   logic-free. `tests/Agora.ArchitectureTests` enforces this in CI. Architecture tests are never
   weakened to make a sprint easier — if they fight you, the design is wrong; fix the design and
   record it in an ADR.
3. **The API contract is a promise.** After a version ships, evolution happens by addition or by
   a new version — never by breaking change. The OpenAPI document is committed; a backend
   contract test fails the build if the served spec drifts, and the frontend's generated API
   types are drift-checked against the same document. One contract, two enforced consumers.
4. **Money-moving operations are idempotent.** Any POST that pays, captures, or refunds accepts
   an `Idempotency-Key` and is safe to retry. No exceptions.
5. **Components don't fetch.** React components never call `fetch`/`axios` directly — data flows
   through the typed API client (`web/src/api/`) and TanStack Query hooks. An ESLint boundary
   rule enforces this; UI state and server state are different things and are managed differently.

## Process

- **One sprint = one branch = one PR**, squash-merged. Branch: `sprint-NN-short-slug`.
- Every sprint has a milestone (`Sprint NN`), a stakeholder story issue, and 3–6 task issues.
- Commits are ordered to teach (domain → use cases → adapters → endpoints → web → docs) and use
  bodies with `Why this way:` / `Notice:` / `See also:` sections, ending with `Refs #N`.
- Reviewer sign-off is a PR comment under the `[Reviewer]` persona (GitHub blocks self-approval;
  all personas post from one account with prefixes `[Author]` / `[Reviewer]` / `[Stakeholder]`).
- Definition of Done lives in the PR template. "OpenAPI updated", "architecture tests green",
  and "frontend types regenerated" are line items, not suggestions.

## Quality gates (all must pass before merge)

```bash
dotnet build -warnaserror
dotnet test
dotnet format --verify-no-changes
cd web && npm run typecheck && npm run lint && npm test && npm run api-types:check
```

- CI runs these plus (from Sprint 13) secret scanning and dependency audit.
- Backend tests never require a database, Docker, or the network: pure domain tests, in-memory
  fakes for ports, EF Core on in-memory SQLite for persistence tests, `WebApplicationFactory`
  for API tests. Frontend tests mock at the API-client seam, never at the component level.
- Every new NuGet or npm dependency needs a one-line justification in the ADR that introduces it.

## Style

- `.editorconfig` is authoritative; `dotnet format` and ESLint/Prettier settle arguments.
- Nullable reference types on everywhere; `TreatWarningsAsErrors` everywhere; TS `strict` on.
- Value objects over primitives at domain boundaries (`Money`, not `decimal`; `Sku`, not
  `string`).
- Teaching comments use the markers `// LEARN:` `// WHY:` `// GOTCHA:` `// TRY:` (in C# and
  TSX alike) — see LEARNER-GUIDE.md. Ordinary comments follow ordinary rules: say what the code
  cannot.
