# Sprint 12 — THE PIVOT SPRINT (The Thesis Exam)

**Goal:** a real mid-quarter requirement lands, and the PR *measures* how local the diff was. This
is the sprint the whole architecture has been underwriting.

## The pivot (stakeholder issue, dropped cold)

> `[Stakeholder]` "We're adding **NorthStar Pay** as a second PSP. Some checkouts must route to
> NorthStar starting **mid-quarter**; orders already in flight keep the provider they started on.
> NorthStar's capture semantics and line granularity differ from PayFlow's, and the external
> consumers of our order API can't break. Also: partners asked for a cleaner order shape — ship
> **v2 of the order resource**, keep v1 byte-stable. The frontend flips providers behind a feature
> flag. Four weeks."

## Scope

**In:** **NorthStar Pay** mock (in-repo, different dialect + capture semantics) behind the existing
`IPaymentGateway` port (S9 pattern, second implementation — PayFlow is now one of two); **provider
selection** per checkout, with **in-flight orders pinned** to the provider chosen at intent
creation; **API v2 of the order resource** (`/api/v2/orders`, new line grouping) with **v1 served
unchanged as an adapter over the v2 shape** (not a fork) + deprecation headers on v1; frontend
provider switch behind a **feature flag** + v2 order views + regenerated v2 types; **the
measurement:** PR body carries a files-touched-per-layer table + commentary wherever locality broke;
`docs/http/orders-v2.http`.

**Out:** automatic reconciliation between providers; a third provider; removing v1 (sunset is a later
comms exercise).

## Teaching focus

Contract evolution without breakage (v1 adapter over v2 shape). Ports paying rent — the second PSP
proves S9's ACL was worth its file. The thesis, adjudicated with numbers.

## Commit narrative (sketch)

1. `feat(vendor): NorthStar Pay mock + ACL adapter behind IPaymentGateway` (dialect tests; second implementation — should feel routine now)
2. `feat(domain+application): provider selection + in-flight pinning (fixed at intent creation)`
3. `feat(api): /api/v2/orders + v1 compatibility adapter + deprecation headers`
4. `test(api): v1 golden responses replayed against the v2-backed implementation — byte-stable`
5. `feat(web): provider switch behind a feature flag + v2 order views + regenerated types`
6. `docs: ADR-0014 payment-provider abstraction (second vendor), ADR-0015 API versioning policy, concepts/contract-evolution.md; PR body = the locality report`

## Teaching placements

- 📘 on the locality table: the thesis, adjudicated with numbers — files per layer, core vs edge.
- 📘 on provider pinning: an in-flight order finishes on the provider it started with — effective
  selection at intent creation, not at capture.
- 🔍 on the v1 adapter: "prove v1 consumers observe the same JSON, byte-for-byte" (the replay test).
- ⚠️ on the feature-flagged provider switch: a flag is a runtime switch — who may flip it, and what
  happens to requests caught mid-flip?
- 🔗 on the `web/` flag + regenerated v2 types: one OpenAPI change, two enforced consumers again —
  the frontend adopts v2 by regenerating types, not hand-editing them; S1's contract discipline
  pays its largest dividend here.
- 🔗 S1 debate ("if S12's diff isn't local, I was right") — `[Author]` collects the bet, with data.

## Debate

`[Reviewer]`: "Two PSPs behind one port is the S9 bet cashed — but v2-with-a-v1-adapter doubles the
order-serialization surface; just break v1, there's one frontend." `[Author]`: 'one frontend' today,
but the webhook consumers (S10) are external, unversioned promises; the adapter is one file and the
replay test proves it honest, whereas a break is a phone call to every integrator. Reviewer concedes
on v1, wins a smaller point — the deprecation header must carry a sunset date + link, not a bare
`Deprecation: true`; a follow-up commit adds it.

## Exercises

- The locality table shows which layer changed most. Was it the right layer? Argue.
- Route a checkout to NorthStar, then flip the flag mid-checkout. What does the pinned order do?
- Write v1's sunset policy: headers, date, comms. What does S10's webhook system contribute to the
  migration?
