# The Lost-Update Problem (and the 409 that prevents it)

**One-pager · Sprint 03 · pairs with ADR 0005**

## The bug that leaves no trace

Two Store Admins open the same product. Both see version 3. Alia fixes a typo in the description; Ben,
who never saw Alia's tab, lowers the price. Both click Save.

With **last-write-wins** — a plain `UPDATE products SET ... WHERE id = @id` — here is the exact
interleaving that eats a write:

```
time  Alia                          Ben                           products row
────  ───────────────────────────   ───────────────────────────   ─────────────
 t0   GET product  → v3             GET product  → v3             name="Skillet"  v3
 t1   edit description
 t2                                 edit price
 t3   PUT (description)  ─────────────────────────────────────▶   name="Skillet*" v3→(save)
 t4                                 PUT (price)   ─────────────▶   price=… (overwrites t3)
 t5   "Saved!"                      "Saved!"                       Alia's description is GONE
```

Both admins were told "Saved". Nobody was told that Alia's description was overwritten by Ben's save,
which was built on a copy of the row that predated it. There is **no error, no log line, no exception**
— the write simply vanished. That is what makes the lost update the worst kind of data loss: it is
both *invisible* (nobody notices at the time) and *irreversible* (the old value is gone). "One store,
one admin" is not a defense — it's an assumption that expires the day a second person gets edit rights.

## The fix: make the write conditional on what you read

Give every product a `Version`. A client that read v3 must present `expectedVersion: 3` with its save.
The server accepts the write **only if the row is still at v3**, and increments to v4. Replay the race:

```
time  Alia                          Ben                           products row
────  ───────────────────────────   ───────────────────────────   ─────────────
 t0   GET product  → v3             GET product  → v3             v3
 t3   PUT expects v3  ────────────────────────────────────────▶  v3 → v4 (accepted)
 t4                                 PUT expects v3  ───────────▶  v4 ≠ 3 → 409 CONFLICT
```

Ben's save is **rejected with `409 Conflict`**, not silently applied. Alia's description survives.
Ben is *told* his edit was stale — and, crucially, is handed the current row (v4, with Alia's change)
so his UI can show him what changed and let him decide: reload Alia's version, or re-apply his price on
top of it (rebase to v4 and re-save). Nobody's work disappears without someone choosing to replace it.

## Where each piece lives in Agora

| Concern | Where |
|---|---|
| The token | `Product.Version` / `Category.Version` — `int`, `+1` on every mutation (Domain) |
| The gate (common case) | `UpdateProduct` compares `Version` to `expectedVersion` → `ConcurrencyConflictException` (Application) |
| The gate (true race) | EF concurrency token → `DbUpdateConcurrencyException` → translated in `EfUnitOfWork` (Infrastructure) |
| The 409 + current state | `AgoraExceptionHandler` maps to `problem+json`, `type = …/conflict`, `current` member (Api) |
| The recovery UX | `ConflictBanner` diffs "your edit" vs "current", offers reload / overwrite (web) |

## Why an explicit `int` and not `rowversion` / `xmin`

The database could maintain the token itself (SQL Server `rowversion`, Postgres `xmin`). But the test
suite runs on **SQLite**, which has neither — so the concurrency story would be untested on the very
provider the tests use. An explicit `int` behaves the same on every provider and in the in-memory
fakes. See ADR 0005.

## The senior habit

Any time a request is "read a thing, change it, write it back," ask: *what happens if someone else
writes between my read and my write?* If the answer is "the last writer wins and the other change
disappears," you have a lost update waiting for its second user. The cost of preventing it — one column
and one comparison — is almost always smaller than the cost of the support ticket that says "I
definitely saved that and now it's gone."

## Try it

1. In `UpdateProduct`, comment out the `Version != expectedVersion` check and the `IsConcurrencyToken()`
   line, then run `AdminCatalogEndpointsTests` — the lost-update test goes red the *right* way (the
   stale write now succeeds). Read what it asserts.
2. `PUT` the same product from two `.http` requests with the same `expectedVersion`. The second is a
   `409`; inspect its `current` member — that snapshot is what the React banner renders.
3. Archive a product that a shopper holds in a cart (carts arrive in S5). What should add-to-cart and
   cart-merge do about an archived line? Sketch it — this is a real S5 design question.
