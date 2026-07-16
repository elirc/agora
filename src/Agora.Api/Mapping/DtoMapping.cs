using Agora.Application.Carts;
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
            // 🔗 "Is it on sale?" is `campaign is not null`, full stop — and the fact that this reads
            // as *too simple* is the lesson. PriceResolver only hands back a campaign it already
            // filtered with IsActiveAt(asOf), so a campaign being here IS the campaign running, as of
            // the same instant that produced the price on the line above. Asking a second time — with
            // a second clock — is what made the badge and the price disagree in the first place. The
            // fix for a duplicated question is not a better duplicate; it is trusting the answer.
            campaign is not null,
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

    /// <summary>The cart, priced — the shape every cart endpoint answers with.</summary>
    // 📘 SAME TRICK AS PricedProduct, ONE SPRINT ON: this maps from a PricedCart, never from a bare
    // Cart, so there is no way to build a cart response without having resolved prices for it first.
    // A `ToDto(this Cart)` overload would compile, look reasonable, and have no honest source for
    // UnitPriceAmount — at which point the only field in reach is PriceAtAddition, and the cart starts
    // quoting last month's sale. The type makes the wrong mapping unwritable rather than merely
    // discouraged.
    public static CartDto ToDto(this PricedCart cart) =>
        new(
            [.. cart.Lines.Select(ToDto)],
            cart.Subtotal.Amount,
            cart.Subtotal.Currency.Code);

    // Every amount here arrives ALREADY resolved and already multiplied — LineTotalAmount is
    // PricedCartLine.LineTotal, computed in decimal by Money, not `unitPrice * quantity` written out
    // here where it would be a decimal today and somebody's double tomorrow. The mapper reshapes. It
    // never calculates. (S04's rule, and the cart is the first place it would have been tempting.)
    private static CartLineDto ToDto(PricedCartLine priced) =>
        new(
            priced.Line.Id,
            priced.Line.ProductId,
            priced.Product.Name,
            priced.Product.Slug.Value,
            priced.Product.Sku.Value,
            priced.Line.Quantity,
            priced.UnitPrice.Currency.Code,
            priced.UnitPrice.Amount,
            priced.LineTotal.Amount,
            priced.Line.PriceAtAddition.Amount,
            priced.PriceChanged,
            priced.Line.AddedAtUtc);

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
