namespace Agora.Application.Tests.Fakes;

/// <summary>A TimeProvider frozen at a chosen instant.</summary>
// LEARN: time is an input. A test that reads the real clock is a test whose assertions age —
// freeze it and "expires exactly one week from now" becomes an exact equality, not a fuzzy range.
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
