# Sprint 15 — Production Readiness → v1.0.0

**Goal:** operate what we built — health, telemetry, runbooks, one rehearsed disaster — plus the
course's honest accounting: the retrospective and the **flaw-ledger reveal**.

## Scope

**In:** health endpoints (`/health/live`, `/health/ready` — readiness checks DB + **outbox
dispatcher lag**); structured logging with **correlation ids** per request → into the outbox → into
webhook delivery (one id traces a business event end to end); operational metrics surface (request
timings, dispatcher depth, order/payment throughput — log-based, no APM); **runbooks**
(`docs/runbooks/`): deploy, backup/restore (with an actually-executed restore transcript),
webhook-replay, order/refund-correction, incident-response; **the incident drill** — a simulated
outbox-dispatcher stall during a checkout window, worked end to end, written as
`docs/postmortems/2026-XX-outbox-stall.md` (timeline, contributing causes, action items — two land as
commits here); **the reveal:** `docs/sprints/flaw-ledger.md` — all five flaws, plant/harvest commit
links, and the review comments that looked past them; `COURSE-RETROSPECTIVE.md` (sprint takeaways,
the arcs, the thesis verdict from S11/S12 numbers, further-exercise syllabus); a React error boundary
+ an Ops status/health widget + a bundle-budget check; final README Mermaid hexagon; **tag `v1.0.0`
+ Release**; close all milestones.

**Out:** real deployment targets; container orchestration; APM vendors.

## Teaching focus

Operations as a design review you can't argue with. Postmortems: blameless but not causeless. Reading
your own course critically — the retrospective models how seniors extract lessons.

## Commit narrative (sketch)

1. `feat(api): health/liveness/readiness (readiness = DB + dispatcher lag)`
2. `feat: correlation ids end to end (request → outbox → webhook delivery)`
3. `feat: operational metrics logging + dispatcher-lag gauge; feat(web): Ops status widget + error boundary`
4. `docs: runbooks (deploy, backup-restore w/ transcript, webhook-replay, order-correction, incident)`
5. `fix: outbox dispatcher stall guards` (postmortem action items AI-1, AI-2)
6. `docs: postmortem — the outbox-stall drill`
7. `docs: flaw-ledger revealed + COURSE-RETROSPECTIVE + final architecture diagram`

## Teaching placements

- 📘 on correlation ids: observability is a data-modeling problem wearing an ops costume.
- 📘 on the postmortem: the difference between "the root cause" and the five contributing causes.
- 🔍 on runbooks: "a runbook you haven't executed is fiction — note the restore transcript."
- ⚠️ on readiness vs liveness: getting them backwards causes the outage you were preventing.
- 🔗 on the `web/` Ops status widget + the reveal: the widget makes dispatcher lag visible to Ops,
  and `flaw-ledger.md` links every plant/harvest pair and every debate to its outcome (the Reviewer
  wins, tallied).

## Debate

`[Reviewer]`: "Log-based metrics won't survive real traffic — we should have wired OpenTelemetry."
`[Author]`: agreed on the destination, disputed on the when; OTel without a backend is ceremony, and
log-derived metrics teach the *questions* (what would page us?) before buying the answers. `IMetrics`
is one file; swapping it is an S12-grade local diff — now a checkable claim, not a promise. Reviewer
signs off: "the course's last debate ends the way the first one did — with the diff as the argument."

## Post-merge

Tag `v1.0.0`, publish a GitHub Release (full-course recap), close milestone 15, verify all 15 closed,
final README sweep.

## Exercises

- Execute the backup/restore runbook against docker-compose Postgres. Time it. Fix where the runbook
  lied.
- Trace one webhook delivery's correlation id back to the HTTP request that caused it, using only logs.
- Write Sprint 16's playbook: what would YOU build next, and which existing seam absorbs it?
