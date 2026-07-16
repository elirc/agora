# ADR 0007: Cart Identity, the Merge Rule, Price Snapshots, and Domain Events

- **Status:** Accepted
- **Date:** 2026-07-16 (Sprint 05)
- **Deciders:** [Author], [Reviewer]

## Context

Sprint 05 closes the MVP. A shopper must be able to hold a cart **before they have an account**, and
that cart must **become theirs when they sign in** — the single most-used feature in retail, and the
one most often got subtly wrong.

Four decisions fall out of that sentence, and they interlock:

1. **Whose cart is this?** An anonymous shopper has no identity to key a cart by. Something has to
   stand in for one — and whatever that something is, it must not accidentally *become* an identity.
2. **What happens on login,** when a shopper has two carts and the same product is in both.
3. **What does a cart line remember about price,** given that a cart can sit for a month and prices
   move underneath it.
4. **How does the rest of the system find out** any of this happened, without the cart having to know
   who is listening.

## Decision 1 — two cookies: `agora_session` is an identity, `agora_cart` is a claim check

An anonymous cart is held by an unprivileged token in a **separate cookie** (`agora_cart`, HttpOnly,
Secure, SameSite=Lax, 60 days). A signed-in customer's cart is keyed by their **user id**, and their
cart cookie is not consulted at all. SPEC §4 mandated the separation; this is why it is right.

**The two tokens are cryptographically identical and semantically opposite.** Both are 256 bits of
CSPRNG, stored only as a SHA-256 hash (`ISessionTokens` mints both — the crypto has no reason to
differ). What differs is *what the server does when you present one*: present `agora_session` and you
become a person; present `agora_cart` and you get a shopping list, which is what the server would hand
anyone holding the same string. **The privilege is not in the token. It is in the code path that reads
it.**

**Why not one cookie.** Because a cart's rules are necessarily lax and a session's necessarily are
not. A cart token must be mintable for a stranger nobody authenticated, live for months so a shopper
can come back on Tuesday, and be handed out on request. Every one of those is fine for a coat-check
ticket and catastrophic for an identity. Merge them and you have not given the cart a session — you
have given the session a cart's threat model.

**Why the identity wins when both are present.** `CartLookup` checks `ICurrentUser` **first** and only
falls back to the token for anonymous callers. If it were the other way round — or if the token were
merely a fallback — a stale or stolen `agora_cart` presented by a signed-in customer would hand them
somebody else's shopping, and every test with one browser in it would still pass. One `if`, in one
class, checkable by reading rather than by remembering.

**Ownership is enforced by the query, not by a guard.** There is no `RequireUser` on any cart
endpoint and no `if (line.CartId == cart.Id)` anywhere in the sprint. The cart is looked up *by* the
caller's identity, so every line reachable from it is theirs by construction, and a stranger's line id
is simply not in the list — a **404**, not a 403 (a 403 confirms the line exists, which turns
id-guessing into an oracle). `ICartRepository` deliberately has **no `FindByIdAsync`**: a cart id is
not a thing a caller can name, so there is no door to leave unlocked. That is what the pattern costs —
one method you decline to write.

## Decision 2 — a cart line **remembers** the price it was quoted and is **never charged** it

`CartLine.PriceAtAddition` is a `Money` recorded when the product enters the cart. Every cart read
**re-resolves the live price** through S04's `ProductPricing`; the subtotal is built from live prices;
the snapshot is used for exactly one thing — `PricedCartLine.PriceChanged` — and is reported to the
shopper as history.

**Why not charge the snapshot.** A cart would then honour last month's sale forever. "The price you
were shown is the price you pay" sounds like customer service and is a pricing bug: what a shop
charges is a fact about the shop, decided at the moment of the transaction, not about what a browser
rendered in March. A snapshot sitting on a line is indistinguishable from a price in force unless
somebody remembers the difference — so the code is written so nobody has to (`CartLine.TotalAt` takes
the price as a **parameter**; a line cannot tell you what it costs, because a line does not know).

**Why not forget it either.** Then a sale ends between adding and viewing, the number changes, and the
shopper is never told. Silently re-pricing is honest arithmetic and dishonest service. The snapshot's
entire job is to be **compared and reported**: it is evidence, not input.

**A cart is not an order.** This is the distinction the decision turns on, and S07 is the other half:
an order line's price snapshot *is* authoritative, because an order **is** the transaction and what was
sold at what price stops being a question the moment money moves. Same shape, opposite job.

**Resolve → mutate → compose.** `CartPricing` splits into an async half (fetch products, price them at
one instant) and a pure static half (fold the cart and those prices into the response). A use case
resolves *first*, mutates, then composes — so one request contains exactly **one clock read and one
campaigns query**, and the snapshot, the timestamps, the event's instant and every price in the
response are the same moment. The tempting order (mutate, then price for the response, plus a little
resolve for the snapshot) gives one request three "now"s and a comment explaining why the gap is too
small to matter. If the answer is "the gap is too small to matter", the gap should not be there.

**`PricedProduct` gained an `AsOf` field.** S04's own comment said the pair exists so nobody forgets
which instant made a price true — and it did not carry the instant. Nothing was broken, because every
consumer resolved and rendered immediately. S05 is the first caller that has to *remember* one, and
found the gap. An answer that travels should carry the question it answered.

## Decision 3 — merge on login takes the **larger** quantity, per product

Signing in (**and registering** — see below) folds the anonymous cart into the customer's own:
every product from either cart survives, and a product in both keeps `max(mine, theirs)`. The policy is
one named function, `Cart.Reconcile`, so that changing it is obviously changing a rule.

The easy half is not the argument: **nothing a shopper put in a cart may vanish because they logged
in.** The argument is the collision — 2 on the phone, 3 in the account. Three answers were available:

| Rule | Answer | Verdict |
|---|---|---|
| Sum | 5 | Rejected |
| **Take-max** | **3** | **Chosen** |
| Anonymous-wins | 2 | Rejected |

**1. Sum's failure mode is a charge nobody asked for.** The commonest reason a product is in both
carts is that the shopper added 2 on their phone *because they forgot* the 3 in their account. Sum
bills them for 5. Take-max's failure mode is the shopper who genuinely wanted 1+2=3, sees 2 in the
drawer, and clicks `+` once. One failure is visible and costs a click; the other is money.

**2. Sum has nowhere to put an overflow.** 60 and 50 is 110; the per-line maximum is 99. So sum needs
a *second* rule — and here is the thing: **add-to-cart can refuse** (the shopper is right there, a 422
is a real answer), but **a merge happens inside a login**. There is no shopper waiting on a cart
error; there is a shopper waiting to be signed in. Sum's overflow rule can therefore only ever be
"clamp silently": a quantity nobody chose, from a rule nobody wrote down, found by finance. `max` of
two legal quantities is legal. Always. **The rule that needs no exception is usually the rule.**

**3. Take-max is idempotent; sum is not.** `max(max(a,b), b) == max(a,b)`; `a+b+b ≠ a+b`. Merging
twice happens — a retried login, a double-submitted form, a response lost on the way back — and with
`max` the second merge produces an identical cart. This is not a nicety: it is what makes the
two-transaction window in Decision 5 recoverable by "just retry", and it is why a rule's **algebra** is
worth arguing about before its ergonomics.

**Honest counter-argument:** several large stores sum, and the shopper who deliberately builds a cart
across two devices is real. If analytics ever shows that shopper is common, this is one line in
`Cart.Reconcile` and a superseding ADR. We are choosing the failure we would rather explain.

**Register merges too, and it matters more than login.** "Create an account to check out" is the
oldest funnel in retail, so signing *up* is if anything more likely to be carrying a full cart —
and it is the highest-intent moment of the visit.

**The anonymous cart is retired, not deleted** (`RetiredAtUtc`; the repository filters it out of both
lookups). Three reasons, ascending: it is what S03 decided for products, and two answers to "how does
a row go away" is none; a retired row with its lines is the difference between an investigation and an
apology when a shopper says signing in ate their cart; and it is where the merge's idempotence comes
from *operationally* — a second merge cannot find the source. The algebra (`max`) and the filter are
two independent reasons a retry is safe.

**Why merge and not "claim"** even for a customer with no cart: re-pointing the anonymous row is one
UPDATE and a second code path, and the interesting case (the collision) would then be exercised only
by customers who already had a cart. One path means the boring case walks the same lines as the
interesting one, so the boring case tests it.

## Decision 4 — `IEventSink`: a sink, not a bus; facts, not objects

Cart use cases publish `AgoraEvent`s (`cart.product_added`, `cart.merged`) to an `IEventSink` port.
The only adapter today writes to the log.

**A sink, not a bus.** A bus routes, knows subscribers, and can be asked whether anything handled a
message — which invites `await bus.Send(...)` and a handler doing the actual work, and then half the
application's control flow lives in a message router. A sink takes a fact and returns. **If you need
something to happen, call it.** The test: if deleting every subscriber breaks the feature, it was never
an event.

**A fact is data, not an object graph.** `AgoraEvent` is a name, an instant, and a flat
`IReadOnlyDictionary<string, string>`. Carrying a `Cart` would hand a subscriber a live, mutable,
tracked entity whose state at delivery is not its state at publication — and when delivery stops being
in-process (S10), there is no object to hand over, only a row with text in it. Flat strings from day
one means the wire format was always the format. Names are past tense and dotted; money is an exact
decimal string, never a float, because the eventual reader is JavaScript.

**`cart.product_added`, not `cart.line_added`.** Adding a skillet you already have adds no line — it
makes there be more skillets. An event name is a claim about what happened; it has to be true every
time, not usually. (The endpoint makes the same call: `POST /cart/lines` answers **200, not 201**,
because half the time it creates nothing.)

**Only two events, from five use cases.** An event log that mirrors your mutations is a change feed
nobody reads, and every published fact is a promise to whoever subscribed. `cart.line_quantity_set`
would be true and has never been wanted. These two earn their place because something outside the
module wants them: a cart coming alive starts an abandoned-cart mailer's clock, and a merge is the one
moment support gets a ticket about.

**Why the port ships now rather than in S10.** Introducing a port at the same time as its real
implementation teaches nothing about whether the port was right. Here the call sites are written
first and the interesting adapter arrives later — the only order in which "we swapped the adapter and
no use case changed" is a claim rather than a coincidence. If S10 has to reach back into
`Agora.Application` to make an outbox fit, the shape was wrong, and that is a finding worth having.
The registration in `AddAgoraPersistence` is **one line**, and it is the one S10 replaces.

Delivery — who receives an event, how many times, and what a redelivery means — is **S10's subject**
and a bigger one than it looks. The concepts page for events is deliberately deferred to that sprint,
which will have the whole story; this sprint's one-pager is `docs/concepts/identity-vs-resource.md`.

## Decision 5 — the endpoint composes login + merge (two use cases, two transactions)

`AuthEndpoints` calls `Login` (or `RegisterCustomer`), then `MergeCarts`, then clears the cart cookie
unconditionally.

**Why not merge inside `Login`.** It would be atomic — one commit, no window — and it would mean
`Login`, which answers exactly one question ("are these credentials good, here is a session"), starts
knowing about carts. The next thing that should happen at sign-in (a welcome-back email, a loyalty
check, a fraud signal) is then also a parameter of `Login`, and within a quarter `Login` is a sign-in
*pipeline*. Shopping already depends on identity; identity must not learn to depend on shopping.

**The price, stated plainly.** Two transactions. If the merge throws, the shopper gets a 500, their
anonymous cart is un-merged, and the cart cookie is not cleared. They retry the login and it works —
because `MergeCarts` is idempotent, which is the payoff from Decision 3. The window is real and this
paragraph is where it is written down.

## Consequences

- **`ModelMappingTests`' allowlist gains its second entry ever:** `Cart.IsRetired`, the identical
  shape to `Product.IsArchived` (a bool computed from the timestamp that is its single source of
  truth). The test of a legitimate entry is not "is it computed?" but "does something else already
  decide this, such that a column would duplicate a decision?" — and the reason the list lives in a
  file a reviewer reads rather than behind a `[NotMapped]` attribute is that **an allowlist is a
  review surface, not a suppression mechanism**.
- **No `Version` on `Cart`.** S03's optimistic concurrency is for lost updates a human should look at;
  the only person writing your cart is you, in two tabs, and "your cart changed elsewhere, please
  refresh" is a sentence no shop has usefully said. Last-write-wins on a line loses nothing anyone
  would ask for back. S06's reservations are where this stops being relaxed — a race for scarce stock
  is between *different* people over a thing that runs out.
- **Two concurrent adds of the same new product can 500** (the unique `(CartId, ProductId)` index wins
  the race and EF raises a `DbUpdateException`, not the concurrency exception `EfUnitOfWork`
  translates). Exactly the shape S02 accepted for `users.email`: the domain gives the friendly answer,
  the index is the real guarantee. Rare, and the shopper retries.
- **⚠️ S04's allocation prediction was wrong, and it is worth writing down.** ADR-0006 shipped
  `Allocate`/`Distribute` with no caller and said "S05's cart is the first caller". S05 is here and it
  isn't: the cart **multiplies** (price × quantity) and **adds** (line totals into a subtotal) — it has
  nothing to *split*, because splitting is what you do to a cart-level discount across lines, and that
  is S11's promotion engine. The first real caller is S07's order lines, or S11.
  The concession the Reviewer actually made was different and is still exactly as true: allocation is
  the part of `Money` nobody can review by eye, so it is the part that must be proven by property
  test. **The prediction was wrong and the reason was right — noticing those were separate claims is
  the skill.** Predictions about which sprint needs what are cheap; reasons are not.
- **Money's currency gate is load-bearing for the first time.** The subtotal is `Aggregate(Add)` over
  line totals, so every step goes through `Money`'s gate: a cart holding a USD line and a EUR line
  throws, naming both, instead of shipping a plausible 40. `lines.Sum(l => l.Amount)` over decimals
  would have added twenty dollars to twenty euros.
- **Two hardcoded currency assumptions now exist, both named:** `CurrencyCode.MinorUnits` (S04) and
  `CartPricing.StoreCurrency` (the empty cart's zero — a `Money` cannot exist without a currency and
  an empty cart has no line to borrow one from). Behind names, not inline, so a second currency is a
  change rather than a grep.
- **Archived products: three questions, three answers.** May I *add* it — no (404; the storefront has
  no such product, the same answer `/products/{slug}` gives). May I *hold* it — yes; a product can be
  archived while it sits in a cart, and dropping the line would lose something the shopper put there
  for a reason nobody told them. May I *buy* it — **S07's problem**, at checkout, where the money is.
  Conflating them gives you a cart that either lies or loses things.
- **`GET /cart` never 404s and never mints a cookie.** Every shopper has a cart; an empty one is a
  cart, so there is no `POST /cart` and no null in the response shape. And a GET that Set-Cookies is a
  GET with a side effect — every crawler that touched the storefront would leave a row behind, and
  then "abandoned carts" is a metric about robots. The cookie is minted by the first **write**.
- **`DELETE /cart` on an empty cart is 200.** DELETE is idempotent; that is what the verb means, and a
  double-click relies on it.
- **`PATCH /cart/lines/{id}` with `quantity: 0` is a 422, not a removal.** `parseInt(input.value) || 0`
  is one of the most common lines in front-end code, so `0` is what an *empty box* sends. Two
  operations that mean different things get two verbs.
- **Cart writes answer with the whole cart (200 + `CartDto`), not 204.** Removing a line changes the
  subtotal; a 204 would leave the drawer with a false number and two bad options — a second round
  trip, or adding money up in JavaScript.
- **Carts accumulate**, both abandoned and retired. That is the **abandoned-cart reaper**'s job — the
  promise the Reviewer extracted in this sprint's design debate, and S15 runbook material.
  `UpdatedAtUtc` is the column it will select on.
- **`ProductCard` is no longer one big `<Link>`.** A `<button>` inside an `<a>` is invalid HTML with
  undefined-in-practice behaviour. What forced the change was not a design review but the card
  acquiring a second verb.

## Rejected alternatives

- **A `localStorage` cart, no server table until checkout.** The Reviewer's opening position; see the
  design-debate thread. Rejected: a client-only cart cannot enforce quantity rules, cannot resolve a
  price (so it must either store one — a client deciding what things cost — or show none), cannot
  survive a device switch, and cannot merge honestly on login because there is nothing to merge *from*
  that the server ever agreed to. The junk it avoids is real and is a reaper's job, not an
  architecture.
- **One cookie for session and cart.** See Decision 1. The cart's threat model would become the
  session's.
- **A cart id in the URL (`GET /cart/{id}`).** Rejected: it makes a cart addressable and therefore
  guessable, and it requires an ownership check on every route — a rule that holds until someone
  writes a sixth use case and forgets. What you never expose, you never have to defend.
- **Sum on collision.** See Decision 3. Not idempotent, can overflow with nowhere to put the
  overflow, and its failure mode is billing.
- **Claiming the anonymous cart when the customer has none.** One UPDATE cheaper, one code path more.
- **Charging `PriceAtAddition`.** A cart honouring an expired sale forever. That behaviour is an
  **order**'s (S07), and the difference between a cart and an order is exactly the point.
- **An `itemCount` field on `CartDto`.** The client sums integers — exact in JavaScript to 2^53 — and
  a DTO carrying every derivable number has more ways to disagree with itself. The rule is about
  *money*, not arithmetic: send what the client cannot compute, let it compute what it can.
- **Publishing an event per mutation.** A change feed nobody reads. Emit facts someone asked for.
- **Optimistic UI for the subtotal.** The quantity is honest to patch — the client authored it. The
  subtotal would be `unitPrice * quantity` in a language with no decimal type: inventing a number and
  showing it to a shopper as though the shop had agreed to it.

## New dependencies

**None.** No NuGet, no npm. `MovableClock` (four lines, `tests/Agora.Application.Tests/Fakes`) does
what `Microsoft.Extensions.Time.Testing.FakeTimeProvider` would, minus the timers and scheduling
nothing here needs — every dependency needs a justification, and this one could not find one.
