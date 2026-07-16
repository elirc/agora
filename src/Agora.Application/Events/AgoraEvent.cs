using System.Globalization;

namespace Agora.Application.Events;

/// <summary>
/// Something that happened, in a shape that survives leaving the process: a name, an instant, and
/// flat data.
/// </summary>
// 📘 THE CRUDENESS OF `Data` IS THE DESIGN, so before you improve it, read why it is like this.
//
// The obvious richer envelope is `AgoraEvent(string Name, object Payload)`, or a hierarchy of typed
// events, or — most tempting of all — an event that just carries the Cart. Every one of those is
// wrong for the same reason: AN EVENT IS A FACT, AND A FACT IS DATA. It has already happened. It
// cannot change afterwards, and it must mean the same thing to a reader who opens it in another
// process, in another service, tomorrow.
//
// Carry an entity and none of that holds. The Cart handed to a subscriber is a live, tracked,
// mutable object whose state at DELIVERY is not its state at PUBLICATION; the subscriber can call
// methods on it; and the moment delivery stops being synchronous and in-process — which is precisely
// what S10 does to it — there is no object to hand over at all, only a row in a table with some text
// in it. Flat strings from day one means the day the adapter starts serialising, nothing above it
// notices. The wire format was always the format.
//
// So: names are PAST TENSE and dotted (`cart.line_added`), because an event announces, it does not
// instruct — `cart.recalculate_totals` is a command wearing an event's hat, and a system full of
// those is an RPC layer with worse error handling. Ids are ids, not references. Money is a string of
// its exact decimal, never a float, because the reader will be JavaScript sooner than you think.
// 🔗 S10 turns these into outbox rows and then webhook bodies, which is when "the payload is the
// contract" stops being a slogan and starts being someone else's integration.
public sealed record AgoraEvent
{
    private AgoraEvent(string name, DateTimeOffset occurredAtUtc, IReadOnlyDictionary<string, string> data)
    {
        Name = name;
        OccurredAtUtc = occurredAtUtc;
        Data = data;
    }

    /// <summary>What happened, past tense: <c>cart.line_added</c>.</summary>
    public string Name { get; }

    /// <summary>The instant the fact became true, from the caller's clock — never from this type's.</summary>
    // WHY a parameter and not `DateTimeOffset.UtcNow` right here, where it would be so convenient:
    // an event's instant is the instant of the THING THAT HAPPENED, and the only code that knows when
    // that was is the code it happened in. Reading a clock here would time-stamp events with "when
    // the envelope was built", which is close enough to right to never look wrong, and which puts an
    // ambient clock in the middle of a layer that spent all of S04 getting them out. Every instant in
    // Agora is passed, never fetched, except in the one class that fetches it.
    public DateTimeOffset OccurredAtUtc { get; }

    /// <summary>The fact's fields — flat, already stringly, ready to be a row or a JSON body.</summary>
    public IReadOnlyDictionary<string, string> Data { get; }

    public static AgoraEvent Of(
        string name,
        DateTimeOffset occurredAtUtc,
        params (string Key, string Value)[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("An event needs a name.", nameof(name));
        }

        return new AgoraEvent(
            name,
            occurredAtUtc,
            data.ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal));
    }

    /// <summary>Render the fact for a log line or a diagnostic: <c>cartId=…, productId=…</c>.</summary>
    public string Describe() =>
        string.Join(", ", Data.Select(field => $"{field.Key}={field.Value}"));

    /// <summary>An invariant-culture string for a number that will be read by something that is not C#.</summary>
    // GOTCHA: `42.ToString()` on a machine with a Turkish or German locale is a coin flip you did not
    // know you were tossing — and a decimal is worse (`34,50`). The reader of these fields is a log
    // scraper today and somebody's webhook handler in S10; neither of them speaks your server's
    // locale. Invariant, always, at the boundary.
    public static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Text(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Text(Guid value) => value.ToString();
}
