# Sprint 02 — Identity: AuthN, AuthZ, and Ownership

**Goal:** every request runs as someone (or as an anonymous shopper), authentication is hand-rolled
and legible, and ownership is a policy — not an `if` scattered across handlers.

## Scope

**In:** User + Role (Customer / StoreAdmin / Ops); register / login / logout with **hand-rolled
cookie sessions** + **PBKDF2** password hashing; `ICurrentUser` (who + role) flowing into use
cases; authorization policies per endpoint group; **authorization matrix test** (role × endpoint,
deny cases first); React auth context + guarded routes + login/register pages + a current-user
query; `docs/http/auth.http`.

**Out:** partner keys / webhooks (S10); password reset; MFA; the anonymous cart token (S5); an
ASP.NET Identity migration (bet: audited and revisited in S13).

## Teaching focus

AuthN vs authZ. Ownership modeled as a policy object rather than a per-handler
`if (x.CustomerId == me.Id)`. Why hand-rolled-then-hardened beats library-first-understood-never.

## Commit narrative (sketch)

1. `feat(domain): User + Role, invariants in constructors (no plaintext ever in the domain)`
2. `feat(application): register/login/logout use cases, ICurrentUser, IPasswordHasher port`
3. `feat(infrastructure): session store, PBKDF2 hasher, cookie issuance`
4. `feat(api): auth endpoints, session cookie, endpoint filters resolving CurrentUser`
5. `test(api): authorization matrix — role × endpoint expectations`
6. `feat(web): auth context, guarded routes, login/register pages`
7. `docs: ADR-0003 cookie-session auth + ownership policy, concepts/authn-vs-authz.md`

## Teaching placements

- 📘 on `IPasswordHasher`/PBKDF2: work factor, per-user salt, and why hashing is a port not a static.
- 🔍 on the authz matrix test: "reviewers read the deny cases first."
- ⚠️ on session token generation: cryptographically-random tokens, timing-safe cookie compare,
  `HttpOnly` / `SameSite`.
- 🔗 on the `web/` guarded routes: the guard is UX, not security — the server re-checks every
  request; a hidden button is not an authorization boundary.

## Debate

`[Reviewer]`: "Just use ASP.NET Identity." `[Author]`: Identity solves enterprise auth we don't
have (external logins, 2FA, security stamps) and hides the two ideas this sprint exists to teach —
credential verification and session lifetime. We accept the cost of hand-rolling and schedule an
audit; ADR-0003 records an explicit "when we'd switch" trigger, and S13 collects on it.

## Exercises

- Add a `SupportReadOnly` capability that can GET orders and mutate nothing, touching only the
  policy layer.
- Write the failing test you'd expect for a Customer reading another Customer's resource. Keep it.
- Explain why `ICurrentUser` must not be readable inside a Domain entity.
