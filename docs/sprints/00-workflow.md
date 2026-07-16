# The Sprint Ritual (Phases A–G)

Every sprint follows this script. Sprint files specify only deviations and sprint-specific content.

## Phase A — Frame

1. Milestone `Sprint NN` exists (created at bootstrap).
2. Open the **stakeholder story issue** (template: story) in a persona's voice; attach milestone +
   labels (`sprint-NN`, phase label, `enhancement`).
3. Open **3–6 task issues** decomposing the story; attach milestone + labels.

## Phase B — Branch & build

4. `git checkout -b sprint-NN-short-slug` from up-to-date `main`. Branch BEFORE writing code.
5. Build inside-out: Domain (pure logic + exhaustive tests) → Application (use cases + ports) →
   Infrastructure (adapters) → Api (endpoints + contract tests) → **web** (typed client, query
   hooks, components, pages) → docs (`ADR`, concept one-pager, `.http` file, OpenAPI regen +
   frontend type regen).
6. Local gate before any PR:
   `dotnet build -warnaserror && dotnet test && dotnet format --verify-no-changes` and
   `cd web && npm run typecheck && npm run lint && npm test && npm run api-types:check`.

## Phase C — Commit narrative

7. 4–10 ordered commits telling the story. Lesson arcs are real history: naive commit → RED
   failing-test commit → fix commit.
8. Commit body format: summary line, then `Why this way:`, `Notice:`, `See also:`, ending `Refs #N`.

## Phase D — Pull request

9. Push; open PR with `--body-file` (summary, behavior walkthrough with `.http` examples and
   storefront screenshots-in-words, architecture-impact section, teaching goals, DoD checklist,
   exercises, `Closes #N` lines).

## Phase E — Review theater (the heart of the course)

10. Post **8–11 inline comments** on diff lines via
    `gh api repos/elirc/agora/pulls/<N>/comments -f commit_id=<sha> -f path=<file> -F line=<n> -f side=RIGHT -F body=@<file>`
    (bodies drafted to files first; ≤7 per invocation). Tags: 📘 concept · 🔍 review-lens ·
    ⚠️ pitfall · 🔗 connects-to. Personas: `[Author]`, `[Reviewer]`, `[Stakeholder]` — all posted
    from the course account with prefixes. Include ≥1 Author↔Reviewer thread (2–3 replies).
    At least one comment per sprint lands on a `web/` file — the frontend is not a spectator.
11. Post **one design-debate comment**: `[Reviewer]` challenges a real decision, `[Author]`
    defends with trade-offs, documented conclusion. (≥3 times in the course the Reviewer wins and
    a follow-up commit changes course.)
12. `[Reviewer]` sign-off comment (GitHub blocks self-approval — the comment IS the approval).

## Phase F — Merge

13. Squash-merge (`gh pr merge <N> --squash --delete-branch`); confirm CI green first.
14. Close the milestone. Sync local main.

## Phase G — Post-merge

15. After S5: tag `v0.5.0`, publish GitHub Release with phase recap. After S15: `v1.0.0` + Release.
16. Update `docs/sprints/` index if arcs shifted.

## Standing quality bar

- Domain stays framework-free; architecture tests never weakened.
- No breaking change to a shipped v1 endpoint after S5.
- Money-moving POSTs are idempotent (mandatory middleware from S10).
- Components don't fetch; server state lives in TanStack Query.
- Every new NuGet or npm package gets a one-line justification in that sprint's ADR.
