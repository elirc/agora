using Agora.Domain.Catalog;
using Agora.Domain.Pricing;

namespace Agora.Application.Pricing;

/// <summary>A product, the price in force for it, and the instant that made that price true.</summary>
// WHY a pair and not a price stuffed onto Product: a product does not HAVE an effective price — it
// has a list price, and an effective price is what you get when you ask about it at a moment. Hanging
// the answer on the entity would invite exactly the bug S04 exists to prevent: a Product object that
// someone later reads a price off, having forgotten which instant made it true.
//
// 📘 AND S05 FINISHED THAT SENTENCE. Read the paragraph above — it is S04's, unchanged — then notice
// what the type it describes did not actually do: IT NEVER CARRIED THE INSTANT. The pair exists so
// nobody can forget which moment made the price true, and the moment was not in it. Nothing was
// broken, because every consumer so far resolved a price and rendered it immediately — the instant
// was implicit in "just now", and implicit was good enough.
//
// S05 is the first caller that has to REMEMBER one. A cart line records the price it was quoted, and
// a record of a price is worth nothing without the moment it was true. Worse: without `AsOf` here,
// the use case has to read a clock a second time to learn when "now" was — so one request grows two
// `now`s, microseconds apart, and a comment explaining why that is fine. It is not fine; it is
// precisely what ProductPricing's own comment shouts about one layer up, at a smaller scale.
//
// So: one field, and every instant downstream of a single clock read is the same instant — the price
// in the response, the price stamped on the line, the timestamp on the cart, the event's OccurredAt.
// 🔗 the lesson is about types, not carts: AN ANSWER THAT TRAVELS SHOULD CARRY THE QUESTION IT
// ANSWERED. EffectivePrice already carried its REASON (the campaign that explains the discount); this
// is the same instinct applied to the other half of the question. S04 got it three-quarters right,
// which is a good sprint — and finding the missing quarter took a caller, not a code review.
public sealed record PricedProduct(Product Product, EffectivePrice Price, DateTimeOffset AsOf);
