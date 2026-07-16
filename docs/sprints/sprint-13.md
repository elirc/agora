# Sprint 13 — Hardening: Threat Model & Security Audit

**Goal:** read Agora like an attacker, write it down like an engineer. Two audit findings are
harvested RED→GREEN, plus a systematic sweep and CI security gates.

## Scope

**In:** **threat model** (`docs/security/threat-model.md`: assets, trust boundaries, STRIDE over the
API surface, webhook egress, session cookies); **the audit** (`docs/security/sprint-13-audit.md`)
with a findings table — the two harvests below plus honest smaller findings (login rate limiting
absent, session-fixation review, webhook SSRF egress rules, cookie flags); fixes for all; **CI
additions**: gitleaks secret scan (binary) + `dotnet list package --vulnerable` + `npm audit`, each
failing on a known vuln; **login rate limiting + lockout backoff**; the ownership matrix test
expanded to **every by-id read** (the fix that keeps the IDOR fixed); the S2 hand-rolled-auth bet
audited, kept, and recorded; React login-throttle UX + generic auth errors (no user enumeration).

**Out:** penetration-test theater; WAF cosplay; dependency pinning beyond lockfiles.

## The harvest arcs — in-PR commits (framed as audit findings)

**Finding: cross-customer resource read (IDOR).** RED: an ownership matrix test with customer B's
session + customer A's resource id expects 404, gets 200. Fix: by-id reads require owner context at
the *type* level (`OwnedId<T>` or equivalent — the compiler refuses the unscoped query); sweep of
every by-id path; matrix expanded to all of them.

**Finding: money-precision drift.** RED: a CsCheck property finds inputs where a per-line computation
diverges from the summed one by a penny (adversarial decimals from the shrinker). Fix: the drifting
path replaced with the S4 allocator; a benchmark in the commit body shows the "optimization" bought
nothing.

## Commit narrative (sketch)

1–2. IDOR arc (RED matrix test → type-level owner scoping + sweep) · 3–4. money-precision arc
(RED property → allocator + benchmark) · 5. `fix: login rate limiting + lockout; generic auth
errors` · 6. `feat(ci): gitleaks + vulnerable-package audit + npm audit` · 7. `docs: threat model +
audit report + ADR-0016 security posture`

## Teaching placements

- 📘 on the threat model: trust boundaries are where reviews concentrate; STRIDE as a checklist, not
  a religion.
- 📘 on the owner-scoped id fix: make the wrong thing unrepresentable beats remembering to filter.
- 🔍 on the audit doc: "a finding without a pinned regression test will regress — reproduction,
  impact, fix, and test, all four."
- ⚠️ on the deleted fast path: performance claims without benchmarks are how correctness dies.
- 🔗 on the `web/` generic auth error: "invalid email or password" as a single message — the login
  form must not become a user-enumeration oracle; this fix spans server and client together.

## Debate

`[Reviewer]`: "Owner-scoped ids infect every repository signature — a global EF query filter on
`OwnerId` does the same invisibly." `[Author]`: invisible is the problem; query filters silently
no-op when context is missing (the outbox dispatcher runs owner-less), and a silent no-op on an
authorization filter is the vulnerability, not the fix; the type forces the decision at every call
site. Conclusion: both — query filter as belt, typed ids as suspenders, dispatcher documented as the
deliberate exception. ADR-0016 records it.

## Exercises

- Run the ownership matrix and count the by-id endpoints. Which two were hardest to categorize? Why?
- Replay an expired-timestamp webhook signature (S10). Which defense catches it, and in what order?
- Delete one finding's regression test and write the incident report from eighteen months later.
