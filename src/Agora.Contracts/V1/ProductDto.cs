namespace Agora.Contracts.V1;

/// <summary>A purchasable product, as served by /api/v1/products.</summary>
// WHY: PriceAmount + PriceCurrency are two primitives here on purpose — the wire has always
// spoken JSON numbers and strings, so promoting Money to a value object INSIDE the domain
// (S04) need not change this shape at all. The contract and the domain evolve on their own
// clocks; that independence is what versioning buys.
public sealed record ProductDto(
    Guid Id,
    Guid CategoryId,
    string Name,
    string Slug,
    string Sku,
    string Description,
    decimal PriceAmount,
    string PriceCurrency,
    DateTimeOffset CreatedAtUtc);
