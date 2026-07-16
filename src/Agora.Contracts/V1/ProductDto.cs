namespace Agora.Contracts.V1;

/// <summary>A purchasable product, as served by /api/v1/products.</summary>
// WHY: PriceAmount + PriceCurrency are two primitives here on purpose — the wire has always
// spoken JSON numbers and strings, so promoting Money to a value object INSIDE the domain
// (S04) need not change this shape at all. The contract and the domain evolve on their own
// clocks; that independence is what versioning buys.
//
// 📘 S04 KEPT THAT PROMISE, and the diff is the evidence: the domain's price became a Money value
// object this sprint and these two fields did not move. What S04 ADDS is additive — four sale fields
// and a nested Sale — because a shipped contract grows by addition or by a new version, never by a
// breaking change (ADR-0002, rule 3). Every S01 client reading priceAmount/priceCurrency still works,
// unchanged, and simply never learns there was a sale.
//
// ⚠️ THE CLIENT DOES NOT DO MONEY MATH — read the field list as the argument for that. The server
// sends the list price, the RESOLVED effective price, and the save percentage: all three already
// computed. It would be one line shorter to send the list price and a percentage and let React
// multiply. That line would be a bug: JavaScript has no decimal type, so `19.99 * 0.9` is
// 17.991000000000003, and every client (this app, a future mobile app, a partner's integration)
// would round it its own way and get its own answer. Money is resolved once, in the domain, in
// decimal, by the code that owns the rounding policy. The client renders the number it is handed.
public sealed record ProductDto(
    Guid Id,
    Guid CategoryId,
    string Name,
    string Slug,
    string Sku,
    string Description,
    // The LIST price — what the product costs when nothing is on sale.
    decimal PriceAmount,
    string PriceCurrency,
    // What it costs right now: the sale price if a campaign is running, else the list price.
    decimal EffectivePriceAmount,
    bool IsOnSale,
    // The "Save X%" badge number, whole percent. Zero when nothing is on sale.
    int SavePercent,
    // The campaign behind the sale price, or null when the product is at list price.
    SaleDto? Sale,
    DateTimeOffset CreatedAtUtc);

/// <summary>The sale a shopper is currently getting: what it is called and when it ends.</summary>
// WHY the storefront learns the window but not the discount's mechanics: "Ends Sunday" is useful to a
// shopper; "PercentOff 15" is the store's business. The admin contract (AdminSaleCampaignDto) carries
// the mechanics because the back-office edits them. Same row, two audiences, two shapes — the same
// split S03 made between ProductDto and AdminProductDto.
public sealed record SaleDto(
    Guid CampaignId,
    string Name,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc);
