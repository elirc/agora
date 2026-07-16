namespace Agora.Domain.Values;

/// <summary>
/// A half-open interval of time: <c>[Starts, Ends)</c> — an instant exactly at <see cref="Ends"/>
/// is already outside.
/// </summary>
// 📘 WHY HALF-OPEN, AND WHY IT IS WORTH A PARAGRAPH. A closed interval [start, end] makes the last
// instant belong to BOTH a window and the window that follows it — so back-to-back campaigns
// (August ends, September begins) overlap for exactly one tick, and "which price applies?" has two
// answers. Half-open makes windows tile perfectly: `[Aug1, Sep1)` and `[Sep1, Oct1)` touch, never
// overlap, and every instant belongs to exactly one. Every off-by-one at a boundary that this
// codebase will never have to debug is paid for by this single decision, made once, here.
//
// 🔗 S06 reuses this exact shape for stock RESERVATIONS — the same [starts, ends) window, but a
// harder clock question: a sale window that resolves a second late shows a stale price, while a
// reservation window that resolves a second late sells stock that isn't there. Same type; the
// difference is entirely in who supplies the instant and how sure they are of it.
public readonly record struct DateRange
{
    public DateTimeOffset Starts { get; }

    public DateTimeOffset Ends { get; }

    private DateRange(DateTimeOffset starts, DateTimeOffset ends)
    {
        Starts = starts;
        Ends = ends;
    }

    /// <summary>A window from <paramref name="starts"/> (inclusive) to <paramref name="ends"/> (exclusive).</summary>
    public static DateRange From(DateTimeOffset starts, DateTimeOffset ends)
    {
        // WHY `<` and not `<=`: an EMPTY window (Starts == Ends) is legal and useful — it is what a
        // campaign cancelled before it ever began collapses to, and Contains() is correctly false for
        // every instant. A BACKWARDS window is not a window at all, and there is no instant it could
        // describe, so it is refused at the door. Note which invariant lives where: "a window may be
        // empty" is a property of intervals; "a SALE may not be born empty" is a property of sales,
        // and that rule lives on SaleCampaign, not here.
        if (ends < starts)
        {
            throw new DomainRuleException(
                $"A date range cannot end ({ends:O}) before it starts ({starts:O}).");
        }

        return new DateRange(starts, ends);
    }

    public bool IsEmpty => Ends <= Starts;

    public TimeSpan Duration => Ends - Starts;

    /// <summary>Whether <paramref name="instant"/> falls inside the window — inclusive of the start, exclusive of the end.</summary>
    // 🔍 note what this function does NOT do: read a clock. It cannot tell you whether a window is
    // "current", because "current" is not a property of an interval — it is a question about an
    // instant somebody else has to name. That is the whole discipline of this sprint: time is an
    // ARGUMENT. A function that takes the instant is testable at any moment, in any timezone, on any
    // machine, forever. A function that reads the wall clock is testable only today, here.
    public bool Contains(DateTimeOffset instant) => instant >= Starts && instant < Ends;

    /// <summary>Whether two windows share at least one instant.</summary>
    public bool Overlaps(DateRange other) => Starts < other.Ends && other.Starts < Ends;

    /// <summary>The same window, cut short at <paramref name="ends"/> (never extended, never reversed).</summary>
    // WHY clamp rather than throw on an early end: this backs "end the sale NOW". Cutting a window
    // that has not started yet collapses it to empty (it never ran); cutting one that already ended
    // leaves it alone (you cannot un-run history). Both are sensible answers to a sensible request,
    // so the range answers them instead of making every caller special-case the clock.
    public DateRange EndingAt(DateTimeOffset ends)
    {
        DateTimeOffset clamped = ends < Starts ? Starts : ends;

        return clamped >= Ends ? this : new DateRange(Starts, clamped);
    }

    public override string ToString() => $"[{Starts:O}, {Ends:O})";
}
