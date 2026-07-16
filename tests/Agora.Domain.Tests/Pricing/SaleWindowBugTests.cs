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

    // 📘 WHY THERE IS NO TEST HERE THAT PINS THE OLD `IsActive()` AT A CHOSEN MOMENT — and why that
    // absence was the most useful thing in this file while the bug was live.
    //
    // I tried to write one. It could not be written. `IsActive()` took no parameter, so there was no
    // way to ask it about 09:30 on Tuesday; you could only ask it about *now*, and "now" is whenever
    // the suite happens to run. `Assert.Equal(sale.IsActiveAt(DateTimeOffset.UtcNow), sale.IsActive())`
    // PASSED — not because the two agreed, but because today is not during an August sale and both
    // said "no". A test that passes because both answers are the same shade of nothing.
    //
    // That was not a testing inconvenience. It was the diagnosis. A function that reads the clock
    // itself can only ever be tested at the one instant you are standing in, so its tests are either
    // vacuous or they fail next Tuesday — and a developer who tries to write one feels the friction,
    // shrugs, and writes nothing. Which is exactly what happened: `IsActiveAt` has boundary tests to
    // the tick, and `IsActive` had none at all. **The untestable function is the one that shipped the
    // bug, and it was untestable for precisely the reason it was wrong.**

    [Fact]
    public void A_sale_scheduled_in_a_non_utc_offset_is_evaluated_as_an_instant()
    {
        SaleCampaign sale = FlashSale();

        // ⚠️ THIS WAS THE BUG, ISOLATED — and it is now a regression test, which is the only reason
        // to keep a reproduction after the fix.
        //
        // `Window.Starts` is 2026-08-04T09:00+09:00 — an INSTANT. The deleted code took its
        // `.DateTime`: 2026-08-04T09:00, a bare number with the offset thrown in the bin. Those are
        // not the same fact. One means a moment in the history of the universe; the other means nine
        // o'clock somewhere unstated.
        //
        // Comparing that number to a reading of the server's wall clock asked "is the server's local
        // number bigger than Tokyo's local number" — a question about nothing, with no answer that is
        // right anywhere. It was least wrong on a machine whose locale matched the one the sale was
        // typed into, which is precisely the machine the person who wrote it was sitting at.
        //
        // The sale runs 00:00Z–08:00Z. At 00:30Z it is running, and now everything agrees — because
        // there is only one way left to ask.
        DateTimeOffset duringTheSale = TokyoNine.AddMinutes(30);

        Assert.True(sale.IsActiveAt(duringTheSale));

        // The half-open window, pinned from both sides at the boundary — in the offset the store
        // typed and in the UTC the server thinks in. Same instants, two spellings, one answer.
        Assert.True(sale.IsActiveAt(TokyoNine));
        Assert.True(sale.IsActiveAt(new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero)));
        Assert.False(sale.IsActiveAt(TokyoNine.AddTicks(-1)));
        Assert.False(sale.IsActiveAt(TokyoFive));
        Assert.False(sale.IsActiveAt(new DateTimeOffset(2026, 8, 4, 8, 0, 0, TimeSpan.Zero)));
    }
}
