# ADR 0004: Hand-Rolled Cookie-Session Authentication & Ownership as Policy

- **Status:** Accepted
- **Date:** 2026-07-16 (Sprint 02)
- **Deciders:** [Author], [Reviewer]

## Context

Sprint 02 gives Agora identity: shoppers register and sign in, and requests run as *someone* (or as
an anonymous browser). Two ideas define the sprint — **verifying a credential** and **the lifetime
of a session** — and everything after it (owned carts, orders, refunds) is built on the seam it
establishes. We need password storage, a session mechanism, roles (Customer / StoreAdmin / Ops), and
a single, testable way to ask "who is this, and may they?".

The obvious move is to reach for **ASP.NET Core Identity** (or issue JWTs). We deliberately did not.

## Decision

**Hand-roll a cookie-session authentication stack, kept small enough that a mid-level engineer can
explain every line — and schedule an audit of it.**

1. **Passwords: PBKDF2-SHA256, 600k iterations, per-user 128-bit salt** (`Pbkdf2PasswordHasher`,
   Infrastructure). The stored verifier is self-describing — `pbkdf2-sha256$<iters>$<salt>$<key>` —
   so raising the work factor later is a redeploy, not a mass reset (verification reads the stored
   count). Hashing is a **port** (`IPasswordHasher`): the algorithm is swappable and tests inject a
   fast fake. The domain stores only the verifier and never learns what "PBKDF2" is.

2. **Sessions: opaque, server-side, hashed at rest.** `SessionTokenService` mints a 256-bit CSPRNG
   token; only its SHA-256 hash is stored (`sessions.token_hash`, unique). An exfiltrated table
   cannot be replayed. The token is **opaque, not a JWT**: sessions are server-side state anyway
   (we need revocation on logout), so a self-describing token would buy nothing and cost us the
   ability to kill it. SHA-256 (not PBKDF2) for the token because a 256-bit random value has nothing
   to brute-force, and an auth check runs on *every* request.

3. **Fixed lifetime, not sliding.** `Session.Lifetime = 7 days`, computed once at `Start`. A sliding
   window would extend expiry on every request — a database **write on every request**, including
   public catalog GETs. Fixed + re-login is simpler, still revocable, and keeps reads read-only.
   *When we'd revisit:* a "remember me" option, or a security requirement for idle timeout, is when
   sliding (or refresh tokens) earns its cost.

4. **Cookie: `agora_session`, `HttpOnly; Secure; SameSite=Lax; Path=/`.** HttpOnly so an XSS cannot
   read the session; Secure so it never crosses plaintext (localhost is a secure context, so dev
   works); SameSite=Lax to shut the CSRF door on cross-site subrequests while still riding top-level
   navigations back into the shop. The token travels **only** in Set-Cookie — never in a response
   body — so the contract's `MeDto` has no token field. That single absence is the tell that Agora
   chose cookies, not bearer tokens.

5. **A stale cookie degrades to anonymous; protected endpoints fail closed.** A bearer token is
   *presented* deliberately, so a bad one is a 401. A cookie is *ambient* — the browser attaches it
   to every request, including public catalog — so a missing/expired/revoked cookie leaves the
   request **anonymous** rather than erroring. The endpoints that need a user (`/me`, `/logout`)
   still 401; the SPA reads that and routes to `/login`. Consequently a 401 carries **no
   WWW-Authenticate** header: that advertises an HTTP *auth scheme* to retry with, and a cookie
   session is not one.

6. **Authorization is a policy, not scattered `if`s.** `ICurrentUser` (an Application port) exposes
   `RequireUser()` (→ 401) and `RequireRole(...)` (→ 403). Roles are **capabilities, not a
   hierarchy**: Ops is not an implicit StoreAdmin: an endpoint that both may use lists both. The
   `RequestCurrentUser` adapter is populated once per request by `SessionCookieAuthenticator` and is
   read-only downstream — never `HttpContext.Current`-style ambient state, which is how
   authorization becomes untestable. Resource **ownership** (a customer sees *their* orders) is the
   same policy idea and lands with orders in S07.

7. **The authorization matrix is the fitness function.** `AuthorizationMatrixTests` enumerates
   persona × endpoint → status, deny cases first. Every future endpoint appends rows; the S13
   hardening sprint audits the table for gaps.

## The bet (put on record)

The Reviewer's position — "just use ASP.NET Identity" — is reasonable and may win later. We are
betting that hand-rolling *now* teaches credential verification and session lifetime in a way a
library hides, and that the surface is small enough to audit. **S13 collects on this bet:** it runs
a security audit of exactly this code. *If that audit finds a hole ASP.NET Identity would have
prevented, the Reviewer was right* — and this ADR is the record of the wager. Trigger to switch:
the day we need external logins (Google/Apple), TOTP/2FA, or security-stamp invalidation, the
hand-rolled path stops paying for itself and we migrate.

## Consequences

- Every authenticated request costs one indexed `sessions` lookup by token hash (the price of
  server-side sessions and revocation). Anonymous requests skip it — no cookie, no query.
- `IUnitOfWork` is introduced here (Agora's first writes): register writes a `User` **and** a
  `Session` in one commit. This seam becomes load-bearing for the S10 transactional outbox.
- Uniform login failures (wrong password == unknown email, byte-identical) defend against user
  enumeration — a property asserted at both the use-case and HTTP levels.
- We now own password and session security. That ownership is the point, and the reason S13's audit
  is scheduled rather than hoped for.

## Rejected alternatives

- **ASP.NET Core Identity:** solves enterprise auth we don't have (external logins, 2FA, lockout,
  security stamps, a `UserManager`/`SignInManager` surface) and *hides the two ideas this sprint
  exists to teach*. We accept the cost of hand-rolling and schedule the audit rather than adopt a
  framework whose internals a learner would never read.
- **JWT / stateless bearer tokens:** no server-side revocation without a denylist (which re-adds the
  state JWTs were meant to avoid), and tokens in JS-readable storage are an XSS exfiltration target.
  Cookie sessions give revocation for free and keep the token out of JavaScript's reach.
- **Sliding-expiration sessions:** a write per request; deferred until a feature (remember-me / idle
  timeout) justifies it.
- **Roles as a hierarchy (Ops implies everything):** a silent god-role with no audit trail. Explicit
  capability lists keep "who may do this" reviewable in the authorization matrix.
