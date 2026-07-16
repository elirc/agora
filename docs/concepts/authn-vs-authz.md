# Authentication vs. Authorization (and why the status codes differ)

Two words that get slurred into "auth" are two different questions, asked in order:

| | Question | Failure | Status |
|---|---|---|---|
| **Authentication (authN)** | *Who are you?* | I can't tell / your session is stale | **401** Unauthorized |
| **Authorization (authZ)** | *May you do this?* | I know who you are; you still may not | **403** Forbidden |

The order is not negotiable: you cannot be *forbidden* from something you have not yet been
*identified* for. So `RequireRole(...)` in Agora calls `RequireUser()` first — an anonymous caller
hitting an admin-only action is **401, not 403**. (See `RequestCurrentUser.RequireRole`.)

## In Agora, concretely

- **AuthN** is the session cookie. `SessionCookieAuthenticator` reads `agora_session`, looks up the
  session by its hash, and — if it's live — attaches the user to `ICurrentUser`. No cookie, or a
  stale one, means *anonymous*. That is authentication: establishing *who*.
- **AuthZ** is `RequireUser()` / `RequireRole(...)` inside use cases, plus (from S07) resource
  **ownership** — a Customer may read *their* order, not another customer's. That is authorization:
  deciding *may*.

## 401 vs. 403 — the one-line test

> "Would logging in fix it?" **Yes → 401. No → 403.**

A signed-out shopper on `/account`: logging in fixes it → 401. A Customer trying a StoreAdmin action:
logging in as themselves changes nothing → 403. (And a resource they don't own often gets **404**,
not 403 — because confirming it *exists* is itself a leak. That rule arrives with orders in S07.)

## Where each lives — and why it's not in the component

Authorization is a **backend** decision, re-checked on **every** request. The React route guard
(`RequireAuth`) and a hidden button are **UX**, not security: they spare an anonymous shopper a
broken page, but they protect nothing. Anything the browser enforces, an attacker skips by calling
the API directly. The server is the authority; the client is a courtesy.

Both questions are answered by *ports*, not ambient globals: `ICurrentUser` is injected, so a test
hands a use case a stub caller and never touches `HttpContext`. Untestable authorization is how the
ownership bugs in the hardening sprint get in — so we make it a first-class, tested seam from day
one, and enumerate it in the **authorization matrix** (`AuthorizationMatrixTests`).

See also: `docs/adr/0004-cookie-session-auth.md`.
