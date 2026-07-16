using Agora.Application.Abstractions;
using Agora.Domain.Carts;

namespace Agora.Application.Carts;

/// <summary>A cart that a write is about to happen to, plus a token to mint if one was needed.</summary>
// WHY the token comes BACK rather than the cookie being set in here: Application does not know what
// a cookie is, and it should not learn. The use case says "a new cart needed a token, here it is";
// the Api decides that a token reaches a browser as an HttpOnly Set-Cookie. Same shape as S02's
// LoggedIn, and the same reason: swapping the transport should not touch a use case.
public sealed record OpenedCart(Cart Cart, string? IssuedToken);

/// <summary>
/// Answers "whose cart is this request about?" — the one place that turns an identity (a signed-in
/// user, or an anonymous token) into a <see cref="Cart"/>.
/// </summary>
// 🔍 THIS CLASS IS THE SPRINT'S IDENTITY BOUNDARY, and it is four lines of `if`. Read them slowly:
// **a signed-in caller's cart is found by their user id and their cart cookie is not consulted at
// all.** That is not an optimisation. If a cart lookup ever considered the token FIRST, or fell back
// to it, then presenting a stolen or stale `agora_cart` while signed in would hand you a cart that
// is not yours — and the bug would be invisible in every test that only ever has one browser in it.
// The identity you have beats the claim check you present. Always, and in one place, so it is
// checkable by reading rather than by remembering.
//
// 📘 why a shared collaborator and not a base class, and not five copies: every cart use case starts
// with this question and must answer it identically. Five copies is five chances to write the `if`
// backwards; a base class would make five use cases inherit a lookup they should be ASKING for. It
// is not a use case itself — it takes ports, has no authorization of its own, and returns domain
// objects — which is the same shape ProductPricing has, for the same reason.
public sealed class CartLookup(
    ICartRepository carts,
    ICurrentUser currentUser,
    ISessionTokens tokens,
    TimeProvider clock)
{
    /// <summary>The caller's cart, or null when they have not got one. Never creates.</summary>
    // WHY reads never create: a GET that writes a row is a GET with a side effect, and the row it
    // writes is real. Every crawler, every link preview, every uptime check that ever touches the
    // storefront would leave a cart behind — and then the abandoned-cart metrics are mostly bots and
    // nobody can tell. A cart is created by the first thing that needs to REMEMBER something.
    public async Task<Cart?> FindAsync(string? cartToken, CancellationToken cancellationToken)
    {
        if (currentUser.IsAuthenticated)
        {
            return await carts.FindActiveByUserAsync(currentUser.UserId, cancellationToken);
        }

        return string.IsNullOrEmpty(cartToken)
            ? null
            : await carts.FindActiveByTokenHashAsync(tokens.HashOf(cartToken), cancellationToken);
    }

    /// <summary>
    /// The caller's cart, creating one (and minting a token, if they are anonymous) when they have
    /// none. The write path.
    /// </summary>
    public async Task<OpenedCart> OpenAsync(string? cartToken, CancellationToken cancellationToken)
    {
        if (await FindAsync(cartToken, cancellationToken) is { } existing)
        {
            return new OpenedCart(existing, null);
        }

        DateTimeOffset now = clock.GetUtcNow();

        if (currentUser.IsAuthenticated)
        {
            var owned = Cart.ForUser(currentUser.UserId, now);
            carts.Add(owned);
            return new OpenedCart(owned, null);
        }

        // 📘 THE CART TOKEN IS MINTED BY THE SAME SERVICE AS THE SESSION TOKEN, and that is the
        // lesson rather than an accident. The CRYPTO is identical and should be: 256 bits of CSPRNG,
        // stored only as a SHA-256 hash, unguessable. What differs is not the token — you could not
        // tell one from the other in a debugger — it is WHAT THE SERVER DOES when you present it.
        // Present agora_session and you become somebody. Present agora_cart and you get a shopping
        // list, which is what the server would hand anyone holding the same string.
        //
        // ⚠️ so the privilege is not in the string; it is in the code path that reads it. Which is
        // exactly why they must never be the same cookie, and why an anonymous token must never be
        // consulted for a signed-in caller (see FindAsync). The day those two paths meet, a claim
        // check has quietly become an identity, and the only evidence is a shopper looking at
        // somebody else's cart. (SPEC §4; ADR-0007 §Decision 1.)
        (string token, string hash) = tokens.Mint();
        var anonymous = Cart.ForToken(hash, now);
        carts.Add(anonymous);

        return new OpenedCart(anonymous, token);
    }
}
