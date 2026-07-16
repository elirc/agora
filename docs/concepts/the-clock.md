# The clock is an input

> A one-pager worth reading even if you never touch Agora.

## The bug that motivates this

A shop ran a flash sale from 09:00 to 17:00 its own time. Half an hour in, a customer saw **the sale
price with no sale badge**. Same page, same product, same instant, two answers.

Two functions were consulted. One took the instant as a parameter. The other fetched it itself. They
disagreed, and there is no ordering of the code that saves you when that happens: one of them is
simply wrong.

## The two mistakes, which are usually confused

```csharp
// The famous one: this reads the SERVER's locale.
DateTime.Now

// The one everybody forgets: this throws the offset in the bin.
window.Starts.DateTime   // 2026-08-04T09:00+09:00  →  the number 09:00
```

`2026-08-04T09:00+09:00` is **a moment in the history of the universe**. Its `.DateTime` is **the
number nine**, which means nine o'clock *somewhere unstated*. Comparing that to a reading of the
server's wall clock asks whether one local number is bigger than another local number: a question
about nothing, with no answer that is right anywhere.

It happens to be *least wrong* on a machine whose locale matches the one the sale was typed into —
which is exactly the machine the person who wrote it was sitting at. That is why it passed review.

## The rule

**Take the instant as a parameter. Read it once, at the edge, from an injected clock.**

```csharp
// Pure. Ask it about Tuesday and it answers about Tuesday — in a test, in 2031, in Manila.
public bool IsActiveAt(DateTimeOffset instant) => Window.Contains(instant);

// The edge. ONE read per request, handed down, so every product on the page speaks about the
// same moment. (Read the clock per product and a page rendering across midnight prices its top
// half yesterday.)
DateTimeOffset now = clock.GetUtcNow();
```

In .NET the port already exists: **`TimeProvider`**. You do not need an `IClock`. (Agora's playbook
said to write one; the code already had the better answer, and a hand-rolled wrapper would have added
an interface, an adapter, and a registration to arrive at what the BCL gives you.)

## Why `UtcNow` is banned too

This is the part people push back on, because UTC feels like the fix.

UTC is not the problem. **Fetching the instant yourself is the problem.** A function that calls
`DateTime.UtcNow` internally:

- cannot be asked about any moment except the one you are standing in;
- therefore cannot be tested at a boundary — its tests are either vacuous or they fail next Tuesday;
- will disagree with the injected clock beside it the first time the two are read a millisecond apart.

Timezones are the *symptom* that makes this famous. Untestability is the disease.

## The tell

**The untestable function is the one that shipped the bug, and it was untestable for precisely the
reason it was wrong.**

In Agora, `IsActiveAt` — the pure one — had boundary tests to the tick. `IsActive()` — the ambient
one — had none at all, and nobody noticed. Try to write its test and you feel the friction
immediately: you cannot tell it what "now" is, so you assert `IsActive() == IsActiveAt(UtcNow)`, it
passes because today isn't during an August sale, and you have tested nothing.

Most developers, at that moment, shrug and write no test. That shrug is the signal. **When a function
is awkward to test, the awkwardness is usually information about the function, not about testing.**

## Ban the capability, not the symptom

Fixing the one function leaves the door open. Agora bans the *category* with a fitness function that
scans the domain for ambient reads and names the file and line:

```
Agora.Domain read the wall clock in 1 place(s).
Pricing/SaleCampaign.cs:105: DateTime.Now — DateTime now = DateTime.Now;
```

This asserts on source text, which normally deserves suspicion. It earns it here: *"no code in the
domain may read the wall clock"* is a statement about what the domain may **ask for**, not about what
it computes — so no behavioural test can express it. It is the same instinct as an architecture test
that checks Domain *cannot reach* a framework rather than that it behaves as though it hasn't.

**A rule that can be violated by accident is not a rule. It is a hope with good posture.**

## Where the local clock IS the right answer

One place, and it is not the server:

```ts
// "Ends Sunday" means Sunday where the SHOPPER is. The instant came from the server; the
// rendering is the browser's business, because the browser is the only party that knows.
new Intl.DateTimeFormat(undefined, { weekday: 'short' }).format(new Date(sale.endsAtUtc))
```

Instants travel. **Wall-clock readings are a rendering, and they belong wherever the reader is.**

## Checklist

- [ ] Does this function fetch the current time? Could it take it as a parameter instead?
- [ ] How many places does an instant enter this pipeline? Can you name all of them?
- [ ] Is there a `.DateTime` on a `DateTimeOffset` anywhere? What did the offset mean, and where did it go?
- [ ] Can you test the boundary at a moment of your choosing — or only at "now"?
- [ ] If a colleague added an ambient read tomorrow, what would tell them?

## See also

- ADR-0008 — the decision and the fitness function
- `tests/Agora.Domain.Tests/Pricing/SaleWindowBugTests.cs` — the bug report as a test
- `tests/Agora.ArchitectureTests/AmbientTimeBanTests.cs` — the ban
- `docs/concepts/value-objects.md` — `DateRange`, and why a window is a type
