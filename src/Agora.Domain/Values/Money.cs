using System.Globalization;

namespace Agora.Domain.Values;

/// <summary>
/// An amount of money in a currency. Exact, base-10, and closed under the operations a store
/// actually performs: add, subtract, scale, and split without losing a cent.
/// </summary>
// 📘 THE THREE DECISIONS THIS TYPE MAKES, AND WHY (ADR-0006):
//
// 1. `decimal`, never `double`. A double is base-2: it cannot represent 0.10, only something
//    0.1000000000000000055511151231257827... close to it. Ten of those added together are not 1.00,
//    and no amount of care at the call site fixes a number that was wrong before you touched it.
//    `decimal` is base-10 with 28–29 significant digits — 0.10 is exactly 0.10 — which is why every
//    field, every intermediate, and every operator below is decimal. Money is not "a number that
//    happens to be a price"; it is a base-10 quantity, and the type must be base-10 too.
//
// 2. Currency is part of IDENTITY, not a label. `Money(5, USD)` and `Money(5, EUR)` are different
//    values, and `usd.Add(eur)` does not have an answer — not a wrong answer, NO answer, until a
//    human supplies a rate and a moment in time. So Add/Subtract/CompareTo throw on a mismatch
//    rather than quietly pretending the numbers are commensurable. That refusal is the whole point:
//    a `decimal` plus a `string` on a DTO has no way to say no.
//    TRY: write `Money.Of(5m, CurrencyCode.Usd) + Money.Of(5m, CurrencyCode.From("EUR"))` in a test.
//    You get a DomainRuleException at that line, naming both currencies — not a plausible-looking 10
//    that ships to a customer.
//
// 3. Rounding is a POLICY, chosen once and written down HERE. Every Money is rounded to its
//    currency's minor units at construction (`Of`), using banker's rounding. There is exactly one
//    `Math.Round` in this file and no other place in Agora may round money. A codebase that rounds
//    "wherever it seemed necessary" has a rounding policy too — it just doesn't know what it is.
//
// See docs/concepts/value-objects.md.
public readonly record struct Money : IComparable<Money>
{
    /// <summary>Agora's rounding policy: banker's rounding (half-to-even).</summary>
    // WHY ToEven and not AwayFromZero (the "round half up" everyone learns in school): half-up is
    // BIASED. It rounds .005 up every single time, so across a few hundred thousand order lines the
    // house gains a systematic fraction of a cent per tie. Half-to-even sends ties up and down in
    // roughly equal measure, so the error cancels instead of accumulating — which is why it is the
    // banking default, the IEEE default, and .NET's default. The cost is that 2.345 → 2.34 surprises
    // people once; the benefit is a ledger that doesn't drift in one direction forever.
    // NOTICE: this is a decision, not a fact. It is written here, tested, and recorded in ADR-0006 —
    // which is the actual lesson. An unwritten rounding policy is a rounding bug with better PR.
    public const MidpointRounding Rounding = MidpointRounding.ToEven;

    /// <summary>The amount, always already rounded to <see cref="CurrencyCode.MinorUnits"/>.</summary>
    public decimal Amount { get; }

    public CurrencyCode Currency { get; }

    // WHY private: `Of` is the only door, so the invariant "an Amount is always at its currency's
    // scale" holds for every instance that exists. Nothing downstream re-rounds, re-checks, or
    // wonders.
    private Money(decimal amount, CurrencyCode currency)
    {
        Amount = amount;
        Currency = currency;
    }

    /// <summary>An amount in a currency, rounded to that currency's minor units by the policy above.</summary>
    public static Money Of(decimal amount, CurrencyCode currency) =>
        new(Math.Round(amount, currency.MinorUnits, Rounding), currency);

    /// <summary>Convenience for the boundary, where currency arrives as a string off the wire.</summary>
    public static Money Of(decimal amount, string? currency) => Of(amount, CurrencyCode.From(currency));

    public static Money Zero(CurrencyCode currency) => new(0m, currency);

    public bool IsZero => Amount == 0m;

    public bool IsNegative => Amount < 0m;

    /// <summary>Sum of two amounts in the same currency.</summary>
    // WHY no rounding here: both operands are ALREADY at the currency's scale (the `Of` invariant),
    // and the sum of two 2-decimal numbers is a 2-decimal number. Exact in, exact out. Multiplication
    // is the operation that can leave the scale — so that one, and only that one, rounds.
    public Money Add(Money other) => new(Amount + SameCurrencyAs(other).Amount, Currency);

    public Money Subtract(Money other) => new(Amount - SameCurrencyAs(other).Amount, Currency);

    /// <summary>Scale an amount by a plain number (a factor, a quantity — never another Money).</summary>
    // 📘 the signature is the lesson. Multiplying money BY money is meaningless — $5 × $3 is not $15,
    // it is nothing at all — so this takes a `decimal` factor, and there is no `Multiply(Money)` to
    // call by mistake. Dividing money by money IS meaningful (it yields a ratio, and the currency
    // cancels out), which is why EffectivePrice's "save X%" unwraps to decimal deliberately rather
    // than pretending a percentage is an amount. The operations a type refuses to offer teach as much
    // as the ones it does.
    public Money Multiply(decimal factor) => Of(Amount * factor, Currency);

    public Money Negate() => new(-Amount, Currency);

    public int CompareTo(Money other) => Amount.CompareTo(SameCurrencyAs(other).Amount);

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public static Money operator *(Money money, decimal factor) => money.Multiply(factor);

    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    public override string ToString() =>
        $"{Amount.ToString("F" + Currency.MinorUnits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)} {Currency}";

    // The currency gate. Every binary operation goes through here, so there is exactly ONE place that
    // can decide two amounts are commensurable — and it decides "only if the currencies are equal".
    private Money SameCurrencyAs(Money other) =>
        Currency == other.Currency
            ? other
            : throw new DomainRuleException(
                $"Cannot combine {Currency} and {other.Currency} without an exchange rate.");
}
