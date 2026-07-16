namespace Agora.Application.Tests.Fakes;

/// <summary>A TimeProvider frozen at a chosen instant.</summary>
// LEARN: time is an input. A test that reads the real clock is a test whose assertions age —
// freeze it and "expires exactly one week from now" becomes an exact equality, not a fuzzy range.
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>A frozen TimeProvider a test can move on purpose.</summary>
// LEARN: FixedClock pins one instant, which is the right tool for "at THIS moment, the answer is
// THAT". This one is for the other kind of assertion — "and then time passed, and the answer
// CHANGED" — which S05's cart needs because a price that moves between adding a thing and looking at
// it is the whole point of keeping a snapshot. Advance() is the test saying out loud that a month
// went by; a test that waited for one, or that used the real clock and hoped, would be neither.
// WHY not a package (Microsoft.Extensions.Time.Testing's FakeTimeProvider does this and more): four
// lines, no dependency, and nothing here needs timers fired or tasks scheduled — only the one method
// Agora actually calls. Every dependency needs a justification (CONTRIBUTING); this one couldn't
// find one.
public sealed class MovableClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
