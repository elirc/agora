namespace Agora.Api.Carts;

/// <summary>
/// The one place that knows the anonymous cart cookie's name and attributes. Minted on a shopper's
/// first write, cleared when they sign in and it stops meaning anything.
/// </summary>
// 📘 START WITH THE FOLDER. This file is not in Auth/, and that is not filing pedantry — it is the
// sprint's whole thesis, expressed as a directory.
//
// `agora_session` AUTHENTICATES: present it and the server believes you are a specific person, and
// everything that follows — your orders, your addresses, your name on a receipt — flows from that
// belief. `agora_cart` authenticates NOTHING. Present it and the server hands you a shopping list,
// which is exactly what it would hand anybody else holding the same string, because that is what a
// claim check is FOR. One is an identity; the other is a coat-check ticket.
//
// ⚠️ so why not one cookie, since the browser is already sending one? Because the moment a cart and a
// session share a token, the cart's rules become the session's rules — and a cart's rules are
// necessarily lax. A cart token has to survive being minted for a stranger nobody authenticated, live
// for months so a shopper can come back on Tuesday, and be handed out to anyone who asks. Every one
// of those is a reasonable thing to do to a coat-check ticket and a catastrophe to do to an identity.
// Conflate them and you have not given the cart a session; you have given the session a cart's
// threat model. (SPEC §4 says two cookies; ADR-0007 §Decision 1 says why.)
//
// 🔍 the tell that the boundary holds: nothing in Agora.Application knows this file exists. A use case
// receives a token as a string, hands one back when it minted one, and has never heard of a cookie.
public static class CartCookie
{
    public const string Name = "agora_cart";

    /// <summary>How long an anonymous cart survives without being touched.</summary>
    // WHY 60 days and not the session cookie's lifetime: they are answering different questions. A
    // session's expiry is a SECURITY control — how long a stolen token is worth stealing — so it is
    // short and it is renewed by proving who you are. A cart's expiry is a PRODUCT decision: how long
    // the shop is willing to remember what you were thinking about. Nobody's account is at risk if
    // the answer is two months; a shopper coming back after the weekend and finding an empty cart is
    // a real loss for no gain. Different purpose, different number, and neither one should be derived
    // from the other because they are not the same kind of thing.
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(60);

    // ⚠️ THE ATTRIBUTES, AND WHY AN UNPRIVILEGED COOKIE STILL GETS ALL OF THEM:
    //   HttpOnly — "unprivileged" is not "public". A cart is somebody's shopping, and an XSS that can
    //              read this token can read their cart. There is no upside to letting JS see it: the
    //              client never needs it (credentials:'include' attaches it) and every line of code
    //              that could touch it is a line that could leak it.
    //   Secure   — plaintext over the wire is plaintext, whatever the token is worth.
    //   SameSite=Lax — same reasoning as the session: not sent on cross-site subrequests (the CSRF
    //              surface), but sent on top-level navigation, so following a link back into the shop
    //              still finds your cart. Strict would break that; None would reopen CSRF, and "it's
    //              only a cart" is how a cross-site request ends up mutating one.
    // The lesson is that the SAFETY of the cookie has nothing to do with the PRIVILEGE of the token.
    // These flags cost nothing and are right for both; what differs between the two cookies is what
    // the server does when it reads them, and that difference is in code, not in attributes.
    private static CookieOptions Options(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        // A cart cookie is not analytics: it is the feature the shopper is using. Same call as the
        // session's.
        IsEssential = true,
        Expires = expires,
    };

    /// <summary>The token this request presented, or null.</summary>
    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out string? token) && !string.IsNullOrEmpty(token)
            ? token
            : null;

    public static void Issue(HttpResponse response, string token, DateTimeOffset nowUtc) =>
        response.Cookies.Append(Name, token, Options(nowUtc.Add(Lifetime)));

    /// <summary>Forget the anonymous cart token — called when signing in makes it meaningless.</summary>
    // WHY this is called UNCONDITIONALLY on login and register, even for a shopper who never had a
    // cart: after that response there is no state in which keeping an anonymous cart token is right.
    // Whatever it held is now theirs, held by their account; a token still sitting in the jar is a
    // second, stale answer to "whose cart is this?" waiting for the day somebody's lookup consults it.
    // There is no branch here because there is no case — and a Set-Cookie for a cookie that wasn't
    // there costs one header and removes a whole category of "how did THAT happen".
    // GOTCHA: a delete must repeat the defining attributes (Path/Secure/SameSite) or the browser
    // keeps the original. Same trap SessionCookie.Clear documents; same fix.
    public static void Clear(HttpResponse response) =>
        response.Cookies.Delete(Name, Options(null));
}
