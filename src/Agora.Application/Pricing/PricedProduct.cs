using Agora.Domain.Catalog;
using Agora.Domain.Pricing;

namespace Agora.Application.Pricing;

/// <summary>A product together with the price in force for it at the instant it was resolved.</summary>
// WHY a pair and not a price stuffed onto Product: a product does not HAVE an effective price — it
// has a list price, and an effective price is what you get when you ask about it at a moment. Hanging
// the answer on the entity would invite exactly the bug this sprint exists to prevent: a Product
// object that someone later reads a price off, having forgotten which instant made it true.
public sealed record PricedProduct(Product Product, EffectivePrice Price);
