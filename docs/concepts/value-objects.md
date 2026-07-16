# Value objects: types that make bugs unrepresentable

> A one-pager from Sprint 04. Companion to `docs/adr/0006-money-and-time-windowed-pricing.md`.

## The idea in one sentence

A **value object** is a small immutable type defined entirely by its value — no identity, no
lifecycle — whose constructor is the only door in, so that every instance which exists is valid, and
the operations it *refuses to offer* are as important as the ones it does.

## Primitive obsession, and why it is expensive

Here is a price:

```csharp
decimal priceAmount;
string priceCurrency;
```

Nothing is wrong with those two lines. Everything is wrong with what the *rest of the codebase* is
now allowed to do with them:

```csharp
var total = usdAmount + eurAmount;         // compiles. ships. wrong.
var discounted = price * 0.9m;             // rounded... where? by whom? to what?
var each = total / 3;                      // 3.33 + 3.33 + 3.33 = 9.99. a cent evaporated.
if (priceCurrency == "usd") { … }          // "USD"? "Usd"? who normalises, and did they?
```

Each of these is a decision, and a `decimal` has no opinion about any of them — so the decision gets
made forty times, by forty call sites, most of which never noticed they were making one. That is the
real cost of primitive obsession. It is not that primitives are bad; it is that **primitives make
every caller responsible for rules nobody wrote down.**

`Money` answers all four, once:

```csharp
usd.Add(eur);            // throws: "Cannot combine USD and EUR without an exchange rate."
price.Multiply(0.9m);    // rounds by THE policy — banker's, to the currency's minor units
total.Allocate(3);       // 3.34 + 3.33 + 3.33 — sums to exactly 10.00, proven by property test
CurrencyCode.From("usd") // normalises at the door; "DOLLAR" never becomes a currency
```

## The four moves

**1. One door.** The constructor is private; `Money.Of` is the only way in, and it validates and
normalises. There is no half-built state, so nothing downstream re-checks. `Slug.From`, `Sku.From`,
`EmailAddress.From` and `CurrencyCode.From` are the same move — Agora has been making it since S01.

**2. Make illegal states unrepresentable — and illegal *operations* uncallable.** Read `Money`'s
surface for what is missing. There is no `Multiply(Money)`, because $5 × $3 is not $15, it is nothing
at all. There is no implicit conversion from `decimal`, because "5" is not an amount of money until
someone says of what. A type teaches by refusing.

**3. Put the rule where the rule is true.** `Money` permits a negative amount — a refund *is*
negative money. "A shelf price is not negative" is a fact about **products**, so it lives in
`Product.ValidPrice`. "A window may be empty" is true of intervals (`DateRange` allows `[x, x)`); "a
SALE may not be born empty" is true of sales, so it lives in `SaleCampaign.Schedule`. Getting this
split right is most of the craft. A rule pushed too low blocks a legitimate caller; pushed too high,
it gets copied.

**4. Unwrap only where the type stops being true.** `EffectivePrice.SavedPercent` divides money by
money and returns a plain `decimal` — deliberately. A ratio is not an amount; the currency **cancels
out**. "25% USD" is a category error. Unwrap at that exact seam and nowhere else.

## Where value objects meet the outside world

Value objects live in the **domain**. Everywhere else, they unwrap:

| Boundary | What happens | Where |
|---|---|---|
| HTTP in | `decimal` + `string` → `Money.Of(amount, currency)` | `CreateProduct`, `UpdateProduct` |
| HTTP out | `Money` → `price.Amount`, `price.Currency.Code` | `Api/Mapping/DtoMapping` |
| Database | `Money` → two columns, via an EF **complex property** | `ProductConfiguration` |

Note the shape of that table: **the wire did not change and the schema did not change.** `ProductDto`
still sends a JSON number and a string; the columns are still `PriceAmount decimal(18,2)` and
`PriceCurrency char(3)`. A value object is a fact about your *code*. The contract and the storage
evolve on their own clocks — which is exactly why S01 could leave a `// TRY:` note promising this
promotion and be confident it would be cheap.

## When NOT to reach for one

Value objects are cheap, not free. Skip it when:

- **The primitive has no rules.** `Product.Description` is a `string` because "some text, trimmed,
  under 4000 chars" is a length check, not a vocabulary. A `Description` type would be ceremony.
- **There is exactly one call site and no arithmetic.** The type earns its keep by the *number of
  places* that would otherwise each decide.
- **You are predicting.** `Discount` is a two-case enum-and-decimal, not a strategy interface,
  because S11 builds the real promotion engine and it will not look like a guess made in S04.

The test is not "is this a domain concept?" — everything is. It is: **would a primitive here force
some other file to know a rule?** If yes, that rule wants a type. If no, you are writing a wrapper.

## The cost, honestly

S04 paid exactly one real price for `Money`: **EF Core 10 cannot build an index over a complex
type's properties**, so `sale_campaigns` indexes `ProductId` alone rather than the
`(ProductId, StartsAtUtc)` the query would prefer (ADR-0006 §Consequences). That is the whole bill.
It is worth knowing that it is not zero — the argument for value objects is not that they are free,
it is that a penny that vanishes on one invoice in ten thousand costs more than an index.

## Prove it to yourself

1. Write `Money.Of(5m, CurrencyCode.Usd) + Money.Of(5m, CurrencyCode.From("EUR"))` in a test. Read
   the exception. Now explain why that beats a runtime check in one endpoint — where would the *other*
   twelve call sites have checked?
2. Delete the `MidpointRounding.ToEven` from `Money.Of` and run the suite. Which tests go red, and
   are they the ones you expected? (Rounding policy is a decision. Red is what a decision looks like.)
3. Add a public `IsFree` property to `Money` and run `ModelMappingTests`. Nothing happens — work out
   why not, and what that says about what that fitness function does and does not guard.
4. Split `$0.10` across three lines. Which line gets the extra cent, and would you be able to defend
   that to the customer looking at the invoice?

## See also

- `docs/adr/0006-money-and-time-windowed-pricing.md` — the decisions, and the roads not taken
- `docs/concepts/fitness-functions.md` — types are fitness functions too; these just run at compile time
- `src/Agora.Domain/Values/Money.cs` and `tests/Agora.Domain.Tests/Values/MoneyPropertyTests.cs`
