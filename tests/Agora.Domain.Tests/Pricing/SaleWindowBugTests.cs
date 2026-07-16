using Agora.Domain.Pricing;
using Agora.Domain.Values;

namespace Agora.Domain.Tests.Pricing;

/// <summary>
/// The bug report, reproduced. See docs/adr/0008-the-clock-is-a-port.md.
/// </summary>
// 🔍 READ THIS FILE BEFORE THE FIX THAT FOLLOWS IT. A bug fix is best read commit-first: the failing
// test is the SPEC OF THE BUG, written before anyone is allowed to touch the code that caused it. If
// you skip to the fix you will learn what changed; if you read this you will learn what was wrong,
// which is the part that transfers.
//
// The report, verbatim from the store owner:
//
//   "We ran a flash sale on the skillet from 09:00 to 17:00 our time on Tuesday. At 09:30 a customer
//    sent me a screenshot: the price was $27.60 — the SALE price, correct — but there was no
//    strike-through and no 'Save 20%' badge. It just looked like the price had quietly dropped. Then
//    someone in support told me the badge showed up later in the afternoon. Same product, same sale,
//    same page. I can't run a sale where the discount is a secret."
//
// One page. Two answers about the same campaign at the same instant. That is not a rounding
// difference or a caching artifact — that is two different clocks being consulted about one fact.
public class SaleWindowBugTests
{
    private static readonly Guid Skillet = Guid.NewGuid();

    // The store schedules in ITS OWN time — which is the entire point, and completely ordinary. A
    // shop in Tokyo says "the sale starts at 09:00 on Tuesday" and means 09:00 in Tokyo. The window
    // records that faithfully as an instant with an offset: 2026-08-04T09:00+09:00, which is
    // 2026-08-04T00:00Z. Nothing here is exotic. Every one of these values is correct.
    private static readonly DateTimeOffset TokyoNine =
        new(2026, 8, 4, 9, 0, 0, TimeSpan.FromHours(9));

    private static readonly DateTimeOffset TokyoFive =
        new(2026, 8, 4, 17, 0, 0, TimeSpan.FromHours(9));

    private static SaleCampaign FlashSale() =>
        SaleCampaign.Schedule(
            Skillet,
            "Tuesday Flash Sale",
            Discount.PercentOff(20m),
            DateRange.From(TokyoNine, TokyoFive),
            TokyoNine.AddDays(-1));

    [Fact]
    public void The_sale_is_running_at_half_past_nine_in_tokyo()
    {
        SaleCampaign sale = FlashSale();

        // 09:30 Tokyo — half an hour into an eight-hour sale. The customer in the screenshot.
        var halfPastNine = new DateTimeOffset(2026, 8, 4, 9, 30, 0, TimeSpan.FromHours(9));

        // The pure check gets this right, as it always has. This test is not the bug; it is the
        // control. It is here so that when the next assertion fails you know the WINDOW is fine and
        // the DATA is fine, and the disagreement is coming from somewhere else entirely.
        Assert.True(sale.IsActiveAt(halfPastNine));
    }

    // 📘 WHY THERE IS NO TEST HERE THAT PINS `IsActive()` AT A CHOSEN MOMENT — and why that absence
    // is the most useful thing in this file.
    //
    // I tried to write one. It cannot be written. `IsActive()` takes no parameter, so there is no way
    // to ask it about 09:30 on Tuesday; you can only ask it about *now*, and "now" is whenever the
    // suite happens to run. Write `Assert.Equal(sale.IsActiveAt(DateTimeOffset.UtcNow), sale.IsActive())`
    // and it passes — not because the two agree, but because today is not during an August sale and
    // both say "no". A test that passes because both answers are the same shade of nothing.
    //
    // That is not a testing inconvenience. It is the diagnosis. A function that reads the clock itself
    // can only ever be tested at the one instant you are standing in, so its tests are either vacuous
    // or they fail next Tuesday — and a developer who tries to write one feels the friction, shrugs,
    // and writes nothing. Which is exactly what happened: `IsActiveAt` has boundary tests to the tick,
    // and `IsActive` had none at all. **The untestable function is the one that shipped the bug, and
    // it was untestable for precisely the reason it was wrong.**
    //
    // So the test below asks the only question that CAN be asked of it, and gets an answer that is
    // wrong on every machine on earth.

    [Fact]
    public void A_sale_scheduled_in_a_non_utc_offset_is_not_evaluated_against_a_naive_wall_clock()
    {
        SaleCampaign sale = FlashSale();

        // ⚠️ THIS IS THE BUG, ISOLATED. `Window.Starts` is 2026-08-04T09:00+09:00 — an INSTANT. Its
        // `.DateTime` is 2026-08-04T09:00 — a bare number with the offset thrown in the bin. Those
        // are not the same fact and they are not eight hours apart by accident: one of them means a
        // moment in the history of the universe and the other means nine o'clock somewhere unstated.
        //
        // Comparing that number to a reading of the server's wall clock asks "is the server's local
        // number bigger than Tokyo's local number", which is a question about NOTHING. It has no
        // answer that is right anywhere, and it happens to be least wrong on a machine whose locale
        // matches the one the sale was typed into — which is exactly the machine the person who wrote
        // it was sitting at.
        //
        // The sale runs 00:00Z–08:00Z. Ask about 00:30Z: it is running, and the whole storefront
        // agrees. The naive comparison, however, is measuring the server's wall clock against the
        // number 09:00 — so it will not say "running" until nine o'clock happens on the SERVER, in
        // whatever zone that is. On a UTC box the badge appears nine hours late. In the report, the
        // badge appeared "later in the afternoon". It did.
        DateTimeOffset duringTheSale = TokyoNine.AddMinutes(30);

        Assert.True(sale.IsActiveAt(duringTheSale));
        Assert.Equal(sale.IsActiveAt(duringTheSale), sale.IsActive());
    }
}
