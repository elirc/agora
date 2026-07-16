using Agora.Application.Pricing;
using Agora.Contracts.V1;
using Agora.Domain.Catalog;
using Agora.Domain.Pricing;

namespace Agora.Api.Mapping;

// WHY: hand-written, boring, and in the Api layer — mapping is the Api's JOB (translating
// domain ↔ contract), not a domain concern and not worth a library at this size. If this file
// ever hurts, that pain is data (see ADR 0001). Note where the value objects unwrap: Slug and
// Sku become plain strings HERE, at the edge, so the contract stays primitives-only. From S04,
// Money unwraps here too — into the amount + currency pair the wire has always spoken.
internal static class DtoMapping
{
    public static CategoryDto ToDto(this Category category) =>
        new(category.Id, category.Name, category.Slug.Value, category.CreatedAtUtc);

    /// <summary>A product plus the price resolved for it — the storefront's shape.</summary>
    // 📘 the storefront maps from a PricedProduct, never from a bare Product, and that is deliberate:
    // there is no way to build this DTO without having resolved a price first, so no endpoint can
    // accidentally serve a list price as though it were the price in force. The type carries the
    // requirement — the same trick Money pulls one layer down.
    public static ProductDto ToDto(this PricedProduct priced)
    {
        Product product = priced.Product;
        EffectivePrice price = priced.Price;
        SaleCampaign? campaign = price.Campaign;

        return new ProductDto(
            product.Id,
            product.CategoryId,
            product.Name,
            product.Slug.Value,
            product.Sku.Value,
            product.Description,
            price.ListPrice.Amount,
            price.ListPrice.Currency.Code,
            price.Amount.Amount,
            campaign is not null && campaign.IsActive(),
            price.SavedPercent,
            campaign is null
                ? null
                : new SaleDto(campaign.Id, campaign.Name, campaign.Window.Starts, campaign.Window.Ends),
            product.CreatedAtUtc);
    }

    // The admin projections carry Version and archive state; the storefront ones deliberately do
    // not. Same entity, a different edge for a different audience.
    public static AdminCategoryDto ToAdminDto(this Category category) =>
        new(category.Id, category.Name, category.Slug.Value, category.Version, category.CreatedAtUtc);

    public static AdminProductDto ToAdminDto(this Product product) =>
        new(
            product.Id,
            product.CategoryId,
            product.Name,
            product.Slug.Value,
            product.Sku.Value,
            product.Description,
            product.Price.Amount,
            product.Price.Currency.Code,
            product.Version,
            product.IsArchived,
            product.ArchivedAtUtc,
            product.CreatedAtUtc);

    // WHY a CampaignView and not a bare SaleCampaign: "is it running?" needs an instant, the use case
    // has the clock, and a mapper should have neither. Handing it a pre-resolved pair keeps the Api
    // layer doing the only thing it is for — reshaping — instead of quietly acquiring an opinion
    // about what time it is.
    public static AdminSaleCampaignDto ToAdminDto(this CampaignView view)
    {
        SaleCampaign campaign = view.Campaign;

        return new AdminSaleCampaignDto(
            campaign.Id,
            campaign.ProductId,
            campaign.Name,
            campaign.Discount.Kind.ToString(),
            campaign.Discount.Value,
            campaign.Window.Starts,
            campaign.Window.Ends,
            view.IsRunning,
            campaign.CreatedAtUtc);
    }
}
