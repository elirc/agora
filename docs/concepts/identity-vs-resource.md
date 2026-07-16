# Identity vs. Resource (why a cart gets its own cookie)

Agora sends a browser two cookies. They look identical — an opaque string of random bytes — and they
are opposite kinds of thing:

| | `agora_session` | `agora_cart` |
|---|---|---|
| **Answers** | *Who are you?* | *Which cart is yours?* |
| **Earned by** | proving a password | asking |
| **Grants** | everything that is yours: orders, addresses, your name on a receipt | one shopping list |
| **If stolen** | you are that person | you can read a stranger's shopping list |
| **Lifetime** | short, a security control | months, a product decision |
| **Kind** | an **identity** | a **claim check** |

The tokens are cryptographically the same — 256 bits of CSPRNG, stored only as a SHA-256 hash, minted
by the same service. That is not sloppiness; the crypto has no reason to differ.

> **The privilege is not in the token. It is in the code path that reads it.**

You could not tell one from the other in a debugger. The difference is entirely in what the server
does when you present it.

## Why not one cookie

Because a cart's rules are lax and must be, and a session's are not and must not be. A cart token has
to be:

- **mintable for a stranger** nobody authenticated (that is the whole feature),
- **long-lived**, so a shopper can come back on Tuesday and find their cart,
- **handed out on request**, to anyone, without a credential.

Every one of those is correct for a coat-check ticket and catastrophic for an identity. Merge the two
and you have not given the cart a session — **you have given the session a cart's threat model.**

## The rule that keeps them apart

When a request presents *both* — which is normal for about one request after signing in, and forever
if anything goes wrong with the clearing — **the identity wins**:

```csharp
// Agora.Application/Carts/CartLookup.cs
if (currentUser.IsAuthenticated)
{
    return await carts.FindActiveByUserAsync(currentUser.UserId, cancellationToken);
}
return await carts.FindActiveByTokenHashAsync(tokens.HashOf(cartToken), cancellationToken);
```

Four lines. Invert them — or make the token a *fallback* — and a stale or stolen `agora_cart` hands a
signed-in customer somebody else's shopping. **Every test with one browser in it still passes.** That
is why the check lives in one class instead of in five use cases: a rule you enforce by reading beats
a rule you enforce by remembering.

## Ownership without an `if`

Notice what the cart endpoints do **not** have: no `RequireUser`, no `RequireRole`, no
`if (line.CartId == cart.Id)`. Every persona — anonymous, Customer, StoreAdmin, Ops — gets a `200`,
because holding a shopping list is not a capability anyone grants.

The boundary is still there. It is in the **lookup**:

- the cart is found *by* your identity (your user id, or your token),
- so every line reachable from it is yours **by construction**,
- so a stranger's line id is not in the list, and the answer is **404**.

`ICartRepository` has no `FindByIdAsync`, deliberately. A cart id is not a thing a caller can name —
no route takes one, no DTO carries one — so there is no door to leave unlocked. That is what this
pattern costs: **one method you decline to write.**

## Why 404 and not 403

A `403` says *this exists and you may not have it*. A `404` says nothing at all.

Give a stranger a `403` for a real line id and a `404` for a typo, and you have built an oracle:
iterate ids, read the statuses, and learn the shape of other people's carts without ever seeing one.
Same answer for "no such line", "not your line", and "you have no cart" — there is exactly one thing to
learn from that endpoint, and it is nothing.

> "Would logging in fix it?" **Yes → 401. No → 403.** And when confirming existence is itself a leak
> → **404.** (See `authn-vs-authz.md`; this is the third case that page promised for S07 and the cart
> got to first.)

## The general shape

A cart is not the last resource that will need this. The question to ask of anything a client can
name:

1. **Is this token an identity or a claim check?** If the server would hand the same thing to anyone
   presenting the same string, it is a claim check. Do not let it grant anything else, ever.
2. **Can the caller name a resource that isn't theirs?** If yes, you need a check — and someone will
   forget to write it. Prefer a lookup they cannot phrase: scope the *query* by the caller.
3. **Does the deny case leak existence?** Then it is a 404, not a 403.
4. **What happens when both identities are present?** There is always an answer, and it should be in
   one place. The one you *proved* beats the one you were *handed*.

## See also

- `docs/adr/0007-cart-identity-merge-and-domain-events.md` — Decision 1, and what happens when the
  claim check meets the identity at login (the merge).
- `docs/concepts/authn-vs-authz.md` — 401 vs 403, and why authorization is a port.
- SPEC §4 — "anonymous cart token is a separate, unprivileged cookie."
