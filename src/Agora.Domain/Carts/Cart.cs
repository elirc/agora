using Agora.Domain.Values;

namespace Agora.Domain.Carts;

/// <summary>
/// What a shopper intends to buy, and who is holding it: a signed-in user, or an anonymous browser
/// presenting an unprivileged token.
/// </summary>
// 📘 A CART IS HELD BY EXACTLY ONE OF TWO THINGS, and the pair of nullable fields below is that
// sentence in code: a <see cref="UserId"/> or a <see cref="TokenHash"/>, never both, never neither.
//
// The reason it is two fields and not one "OwnerId" is the whole point of the sprint. A user id and
// a cart token are not two flavours of the same thing. A user id says WHO YOU ARE; the server got it
// by verifying a password. A cart token says nothing at all about you — it is a claim check, and the
// server will hand the same shopping list to anyone who presents the same string, because that is
// what a claim check is FOR. Collapse them into one column and the day someone writes
// `WHERE OwnerId = @id` against a value the client supplied, an unprivileged token has become an
// identity. They are different kinds, so they are different columns, and (in the Api) different
// cookies. See docs/concepts/identity-vs-resource.md and ADR-0007.
//
// 📘 AND NOTE WHAT IS NOT HERE: a Version. Product got an optimistic-concurrency token in S03
// because two admins editing one product is a lost update worth refusing — a human needs to look.
// A cart is the opposite case. The only person writing your cart is you, in two tabs, and "your
// cart changed elsewhere, please refresh" is a sentence no shop has ever usefully said. The
// operations here are narrow and near-commutative (add this; set that line to 3), the lines are
// independent, and last-write-wins on a line loses nothing anyone would ask for back. Optimistic
// concurrency is for when a silent overwrite destroys information somebody wants; losing "I clicked
// + in the other tab" is not that. S03's tool is a tool, not a habit.
// 🔗 S06's reservations are where this stops being relaxed: a race for scarce stock is between
// DIFFERENT people over a thing that runs out, and that one does not get to shrug.
public sealed class Cart
{
    private readonly List<CartLine> _lines = [];

    public Guid Id { get; }

    /// <summary>The signed-in customer who owns this cart, or null while it is anonymous.</summary>
    public Guid? UserId { get; private set; }

    /// <summary>The at-rest hash of the anonymous cart token holding this cart, or null once it is owned.</summary>
    // WHY a hash and not the token: identical reasoning to sessions (S02). The raw token exists in
    // the Set-Cookie header and the browser's cookie jar, nowhere else — so a leaked `carts` table
    // cannot be replayed to read anybody's cart. The token is unprivileged, but "unprivileged" is
    // not "public": it is still someone's shopping, and a stolen one is a stranger reading it.
    public string? TokenHash { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>The last time anything in this cart moved.</summary>
    // Written by every mutation below and read by nothing yet — the honest label is "not read yet",
    // not "needed". It is the column an abandoned-cart reaper selects on (the promise the Reviewer
    // extracted in this sprint's design debate; S15 runbook material), and the alternative is a
    // reaper that cannot tell a cart abandoned in March from one someone added to this morning.
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>When this cart was consumed by a merge, or null while it is live.</summary>
    public DateTimeOffset? RetiredAtUtc { get; private set; }

    // Computed from RetiredAtUtc, its single source of truth — no column, and the second entry ever
    // on ModelMappingTests' DeliberatelyUnmapped allowlist. Exactly the shape of the first one
    // (Product.IsArchived): a bool derived from the timestamp that decides it. A stored flag beside
    // the timestamp is a second opinion waiting to disagree.
    public bool IsRetired => RetiredAtUtc is not null;

    public IReadOnlyList<CartLine> Lines => _lines;

    private Cart(Guid id, Guid? userId, string? tokenHash, DateTimeOffset nowUtc)
    {
        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>A cart held by an anonymous browser, addressed only by its token.</summary>
    public static Cart ForToken(string? tokenHash, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new DomainRuleException("An anonymous cart needs a token.");
        }

        return new Cart(Guid.NewGuid(), null, tokenHash, nowUtc);
    }

    /// <summary>A cart owned by a signed-in customer.</summary>
    public static Cart ForUser(Guid userId, DateTimeOffset nowUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainRuleException("An owned cart needs a user.");
        }

        return new Cart(Guid.NewGuid(), userId, null, nowUtc);
    }

    /// <summary>The line for <paramref name="lineId"/>, or null when this cart has no such line.</summary>
    // 🔍 THE OWNERSHIP CHECK YOU ARE LOOKING FOR IS NOT HERE, AND THAT IS THE DESIGN. There is no
    // `if (line.CartId == Id)` and no `RequireOwner(userId)` anywhere in this sprint, because a line
    // in someone else's cart is not in the list this method searches: the cart was already looked up
    // BY the caller's identity (their user id, or their token), so every line reachable from here is
    // theirs by construction. Ownership enforced by the QUERY is ownership nobody can forget to
    // enforce; an `if` at the top of a use case is a rule that holds until the day someone writes a
    // sixth use case and doesn't. (S02's "ownership as a policy, not an if-statement", collected.)
    //
    // WHY it returns null rather than throwing: "there is no such line in your cart" is a fact, and
    // what that fact MEANS to a caller is an HTTP question (404) the domain has no business having an
    // opinion about. The aggregate answers; the use case decides.
    public CartLine? LineOrDefault(Guid lineId) => _lines.SingleOrDefault(line => line.Id == lineId);

    /// <summary>
    /// Put <paramref name="quantity"/> of a product in the cart — increasing the existing line if the
    /// product is already there. Returns the line that now holds it.
    /// </summary>
    // 📘 ONE LINE PER PRODUCT is the invariant that makes a cart a cart rather than a receipt tape.
    // Adding a skillet twice means you want two skillets; it does not mean two rows that both say
    // "skillet" and separately have to be reconciled at checkout, in the UI, and in every report.
    // The database backs this with a unique index on (CartId, ProductId) — the same belt-and-braces
    // S02 used for users.email: the domain gives the friendly answer, the index closes the race two
    // simultaneous adds could win.
    // NOTICE the price is a PARAMETER. The cart does not know what anything costs and must not learn:
    // it is handed the price in force, resolved once, by the layer that read the clock.
    public CartLine AddOrIncrease(
        Guid productId,
        int quantity,
        Money unitPrice,
        DateTimeOffset nowUtc)
    {
        if (productId == Guid.Empty)
        {
            throw new DomainRuleException("A cart line needs a product.");
        }

        CartLine? existing = _lines.SingleOrDefault(line => line.ProductId == productId);

        if (existing is null)
        {
            var line = CartLine.Create(Id, productId, quantity, unitPrice, nowUtc);
            _lines.Add(line);
            Touch(nowUtc);
            return line;
        }

        // ⚠️ 98 + 5 THROWS; IT DOES NOT QUIETLY BECOME 99. Clamping would be a lie told to the one
        // person who could correct it: the shopper is right here, watching, and a 422 that says
        // "cannot exceed 99 per line" lets them choose. Silently capping means they asked for 103,
        // were charged for 99, and were never told which of those numbers the store believed.
        existing.ChangeQuantity(existing.Quantity + quantity);
        Touch(nowUtc);
        return existing;
    }

    /// <summary>Set a line to an exact quantity (the stepper's write).</summary>
    public void SetQuantity(Guid lineId, int quantity, DateTimeOffset nowUtc)
    {
        CartLine line = LineOrDefault(lineId)
            ?? throw new DomainRuleException("That line is not in this cart.");

        line.ChangeQuantity(quantity);
        Touch(nowUtc);
    }

    /// <summary>Take a product out of the cart entirely.</summary>
    public void RemoveLine(Guid lineId, DateTimeOffset nowUtc)
    {
        CartLine line = LineOrDefault(lineId)
            ?? throw new DomainRuleException("That line is not in this cart.");

        _lines.Remove(line);
        Touch(nowUtc);
    }

    /// <summary>Empty the cart.</summary>
    // WHY this is not "remove every line in a loop" at the call site: emptying a cart is ONE thing
    // the shopper did, so it is one method, one UpdatedAtUtc, and (when S10 wires the outbox up) one
    // fact — rather than eleven removals that a subscriber would have to reassemble into "they
    // emptied it".
    public void Clear(DateTimeOffset nowUtc)
    {
        _lines.Clear();
        Touch(nowUtc);
    }
    /// <summary>Consume this cart: it has been merged elsewhere and must never be found again.</summary>
    // WHY retire and not DELETE — three reasons, in ascending order of how much they matter:
    //   1. It is what S03 decided for products, and a codebase with two answers to "how do we make a
    //      row go away" has neither.
    //   2. When a shopper says "signing in ate my cart", a retired row with its lines and its
    //      timestamp is the difference between an investigation and an apology.
    //   3. It is where the merge's idempotence comes from. A retired cart is not findable by its
    //      token (the repository filters on RetiredAtUtc), so a merge that runs a second time finds
    //      nothing and does nothing. Delete would give the same answer — but only because the row is
    //      gone, which means the retry works and no one can ever check that it did.
    // The rows do accumulate. That is the abandoned-cart reaper's job, and the reaper is the promise
    // the Reviewer extracted in this sprint's debate rather than something this method should be
    // doing on the way past.
    public void Retire(DateTimeOffset nowUtc)
    {
        if (IsRetired)
        {
            throw new DomainRuleException("Cart is already retired.");
        }

        RetiredAtUtc = nowUtc;
        Touch(nowUtc);
    }

    private void Touch(DateTimeOffset nowUtc) => UpdatedAtUtc = nowUtc;

    // WHY: EF Core materialization only.
    private Cart()
    {
    }
}
