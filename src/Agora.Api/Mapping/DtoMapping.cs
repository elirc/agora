using Agora.Contracts.V1;
using Agora.Domain.Catalog;

namespace Agora.Api.Mapping;

// WHY: hand-written, boring, and in the Api layer — mapping is the Api's JOB (translating
// domain ↔ contract), not a domain concern and not worth a library at this size. If this file
// ever hurts, that pain is data (see ADR 0001). Note where the value objects unwrap: Slug and
// Sku become plain strings HERE, at the edge, so the contract stays primitives-only.
internal static class DtoMapping
{
    public static CategoryDto ToDto(this Category category) =>
        new(category.Id, category.Name, category.Slug.Value, category.CreatedAtUtc);

    public static ProductDto ToDto(this Product product) =>
        new(
            product.Id,
            product.CategoryId,
            product.Name,
            product.Slug.Value,
            product.Sku.Value,
            product.Description,
            product.PriceAmount,
            product.PriceCurrency,
            product.CreatedAtUtc);
}
