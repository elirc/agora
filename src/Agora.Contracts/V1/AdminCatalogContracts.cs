namespace Agora.Contracts.V1;

// LEARN: admin contracts live beside the storefront ones but say MORE. The public ProductDto has no
// Version and no archive state — a shopper has no business with either. These admin DTOs expose both
// because the back-office needs them to edit safely and to see what is hidden. Same domain entity,
// two audiences, two shapes; the mapping at the edge (Api/Mapping) decides who sees what.

// ── Requests ─────────────────────────────────────────────────────────────────────────────────

/// <summary>Body of POST /api/v1/admin/categories. A blank/absent slug is generated from the name.</summary>
public sealed record CreateCategoryRequest(string? Name, string? Slug);

/// <summary>Body of PUT /api/v1/admin/categories/{id}. The slug is immutable and is not accepted.</summary>
public sealed record UpdateCategoryRequest(string? Name, int ExpectedVersion);

/// <summary>Body of POST /api/v1/admin/products. A blank/absent slug is generated from the name.</summary>
// WHY PriceAmount is a decimal on the wire and NOT a float: money is base-10 and exact; a double
// cannot represent 0.10 and cents drift. The React form sends the price as a decimal string, which
// System.Text.Json binds to this decimal without ever touching binary floating point. (S04 promotes
// the pair to a Money value object; this shape need not change.)
public sealed record CreateProductRequest(
    Guid CategoryId,
    string? Name,
    string? Slug,
    string? Sku,
    string? Description,
    decimal PriceAmount,
    string? PriceCurrency);

/// <summary>
/// Body of PUT /api/v1/admin/products/{id} — a FULL update of the mutable fields plus the version
/// the admin was editing. Slug and SKU are immutable identifiers and are not accepted.
/// </summary>
// LEARN: we carry the expected version IN THE BODY rather than as an If-Match / ETag header. Both
// are valid optimistic-concurrency transports; body-version is chosen here for minimal ceremony —
// no ETag parsing, no weak/strong validator rules, one shape the React form binds directly, and the
// archive/restore POSTs below can carry it the same way. ADR-0005 records the trade-off against
// ETags (which shine when you also want conditional GETs and HTTP-cache revalidation — Agora does
// not, yet).
public sealed record UpdateProductRequest(
    Guid CategoryId,
    string? Name,
    string? Description,
    decimal PriceAmount,
    string? PriceCurrency,
    int ExpectedVersion);

/// <summary>Body of POST /api/v1/admin/products/{id}/archive and .../restore — version-guarded like any edit.</summary>
public sealed record ProductVersionRequest(int ExpectedVersion);

// ── Responses ────────────────────────────────────────────────────────────────────────────────

/// <summary>A category as the admin sees it: with its concurrency version.</summary>
public sealed record AdminCategoryDto(
    Guid Id,
    string Name,
    string Slug,
    int Version,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// A product as the admin sees it: with its concurrency version and archive state. This is also the
/// shape echoed inside a 409 conflict body (the "current server state") so a stale editor can rebase.
/// </summary>
public sealed record AdminProductDto(
    Guid Id,
    Guid CategoryId,
    string Name,
    string Slug,
    string Sku,
    string Description,
    decimal PriceAmount,
    string PriceCurrency,
    int Version,
    bool IsArchived,
    DateTimeOffset? ArchivedAtUtc,
    DateTimeOffset CreatedAtUtc);
