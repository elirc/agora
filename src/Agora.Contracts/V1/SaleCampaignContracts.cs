namespace Agora.Contracts.V1;

// LEARN: the back-office's view of a sale, which says MORE than the shopper's SaleDto — the discount
// mechanics, the full window, and campaigns that are over or have not started. A merchandiser needs
// to answer "what did we run last month?" and "what is queued for the weekend?"; a shopper needs to
// know that this thing is £12 today. Same rows, two audiences, two shapes.

// ── Requests ─────────────────────────────────────────────────────────────────────────────────

/// <summary>Body of POST /api/v1/admin/products/{id}/campaigns.</summary>
// WHY DiscountKind is a string on the wire and not an int: an API that says
// {"discountKind": "PercentOff"} is readable in a log, in a curl, and in this file six months from
// now; {"discountKind": 1} is a lookup table someone has to find. The parse happens in the use case
// so an unknown kind is a 422 that NAMES the legal values, not a binder's "could not convert".
//
// WHY DiscountValue is a decimal (and the React form sends it as a string): the same rule as
// PriceAmount — money and money-adjacent numbers never touch a JS float. System.Text.Json binds the
// string straight to a C# decimal without ever passing through a double.
//
// WHY the window is two DateTimeOffsets and not two dates: "starts on 2026-08-01" is not an instant —
// it is an instant ONLY once you say whose midnight you meant. Making the client send an offset moves
// that decision to the one place that knows the answer (the admin's browser, which knows the admin's
// timezone) instead of letting the server guess with its own locale. See ADR-0006.
public sealed record CreateSaleCampaignRequest(
    string? Name,
    string? DiscountKind,
    decimal DiscountValue,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc);

// ── Responses ────────────────────────────────────────────────────────────────────────────────

/// <summary>A sale campaign as the admin sees it, including whether it is running right now.</summary>
// WHY IsRunning is computed on the SERVER and shipped: the alternative is for the browser to compare
// the window against its own clock, which puts a second answer to "is this live?" in a second place
// with a second clock — and the two disagree the moment a laptop's time is off. One question, one
// answer, resolved against the server's injected TimeProvider (see ListSaleCampaigns).
public sealed record AdminSaleCampaignDto(
    Guid Id,
    Guid ProductId,
    string Name,
    string DiscountKind,
    decimal DiscountValue,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    bool IsRunning,
    DateTimeOffset CreatedAtUtc);
