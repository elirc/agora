namespace Agora.Contracts.V1;

/// <summary>One row of the Store Admin's low-stock view.</summary>
// 📘 THREE NUMBERS, NOT ONE, and that is the whole design of this DTO. "2 available" is an emergency
// if two are on the shelf and a non-event if nine are on the shelf with seven in people's carts —
// same figure, opposite actions. A low-stock report that shows only availability sends somebody to
// the stockroom to count skillets that are already there.
//
// The rule generalises: when a number is DERIVED from other numbers and a human is going to act on
// it, send the inputs too. The derivation is cheap; the trip to the stockroom is not.
public sealed record LowStockDto(
    Guid ProductId,
    string Name,
    string Sku,
    int OnHand,
    int Reserved,
    int Available);

/// <summary>The shopper-facing answer to "can I have one?".</summary>
// ⚠️ THIS IS ADVISORY AND THE FIELD NAMES SAY SO. `availableAt` is a timestamp riding along with
// `available` precisely because the number is a read of something moving: between this response and
// the shopper's click, a reservation can expire and another can be taken. The number was true when it
// was computed, the computation is reproducible, and neither of those makes it true NOW.
//
// The alternative — omit the timestamp — invites a client to treat the count as a promise, cache it,
// and eventually show "3 left" for an hour. The promise is made in exactly one place, when a
// reservation is written under the stock row's concurrency token, and nowhere else. See ADR-0008.
public sealed record StockDto(int Available, bool InStock, DateTimeOffset AvailableAt);

/// <summary>A claim on stock, held for a cart.</summary>
public sealed record ReservationDto(
    Guid Id,
    Guid ProductId,
    int Quantity,
    DateTimeOffset ExpiresAtUtc);

/// <summary>Ask to hold stock for the caller's cart.</summary>
public sealed record ReserveStockRequest(Guid ProductId, int Quantity);

/// <summary>The answer to a reservation attempt: the claim, or the number that stopped it.</summary>
// WHY a 200 with `success: false` rather than a 409 for out-of-stock: because "you cannot have four
// of these" is not an error in the caller's request — the request was perfectly well formed and the
// answer is simply no. A client rendering "only 1 left" needs the NUMBER, and putting a number a UI
// depends on inside an error body is how it ends up parsed out of a `detail` string.
//
// 🔍 The line: reserve for a product that does not exist → 404 (you asked about nothing). Reserve
// more than exists → 200, success:false, available:1 (you asked a sensible question and the answer is
// no). Two different kinds of "no", two different shapes.
public sealed record ReservationResultDto(
    bool Success,
    ReservationDto? Reservation,
    int Available);
