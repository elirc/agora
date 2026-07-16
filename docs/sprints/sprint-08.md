# Sprint 08 — Reviews & the Extracted Moderation Workflow (The Reuse Sprint)

**Goal:** a second thing needs "pending → approved/rejected" — so NOW we extract moderation as a
generic component, from code we first wrote naively. Reuse by extraction, not prediction.

## Scope

**In:** Review (rating + body, customer-authored, one per product per customer); admin moderation
(`pending → approved / rejected` with reason); **the extraction:** the moderation flow written
concretely first (a small copy-pasted state shape), pinned by **characterization tests**, then
extracted to a generic `ModerationWorkflow<T>` and rewired; a moderation inbox (pending items across
types); public reviews list (approved only) + star summary; admin moderation queue UI + review
submission UI; `docs/http/reviews.http`.

**Out:** review images; helpfulness voting; multi-step moderation; profanity/ML filters — a human
queue is the product.

## Teaching focus

The rule of three, honestly applied at two: extract when the second consumer *proves the shape*, and
document what we deliberately did NOT parameterize. Refactoring as commit narrative — behavior-
preserving moves kept separate from behavior changes; the characterization tests never move.

## Commit narrative (sketch)

1. `feat(domain): Review + concrete moderation flow — deliberately copy-pasted from an existing shape`
2. `test: characterization tests pinning the concrete moderation behavior before any refactor`
3. `refactor(domain): extract ModerationWorkflow<T> — reviews rewired, zero behavior change` (tests untouched, still green — that's the proof)
4. `feat(application+api): moderation inbox across types, public approved-only reviews`
5. `feat(web): moderation queue (admin) + review submission + star summary`
6. `docs: ADR-0009 extraction over prediction, concepts/rule-of-three.md`

## Teaching placements

- 📘 on commit 1: "we copy-pasted on purpose — premature abstraction costs more than duplication,
  and today's duplication is tomorrow's specification."
- 📘 on the characterization tests: refactoring's safety net, written BEFORE anything moves.
- 🔍 on commit 3's diff: "a true refactor commit — the test files appear in the diff zero times."
- ⚠️ on `ModerationWorkflow<T>`: what we deliberately did NOT abstract (multi-step chains,
  escalation, auto-rules) and the YAGNI ledger of what we'll pay for only when a consumer demands it.
- 🔗 on the `web/` moderation queue: the same component now backs more than one admin queue — the
  frontend proves the reuse the day a single queue view renders two content types.

## Debate

`[Reviewer]`: "`ModerationWorkflow<T>` will grow type parameters until it's a workflow engine —
model moderation as data (a states table), not generics." `[Author]`: a states table *is* an engine,
the most expensive way to avoid writing one more class; generics stay until a third consumer wants
runtime-configured steps — the moment we need a designer-editable process, we buy, not build.
Conclusion: generics plus a tripwire comment marking the buy/build line. Reviewer signs off "under
protest, tripwire noted."

## Exercises

- Add moderation for a third thing (seller Q&A) as the third consumer. Time yourself; count files.
- "Improve" a status name mid-refactor and watch a characterization test fail. What did it protect?
- Sketch the states-table schema the Reviewer wanted. Where would it actually win?
