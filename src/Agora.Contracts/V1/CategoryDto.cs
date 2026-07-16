namespace Agora.Contracts.V1;

/// <summary>A storefront category, as served by /api/v1/categories.</summary>
// LEARN: Contracts records are the API's public vocabulary — primitives only, no domain
// types (no Slug, no Category). This file changing IS the API changing; treat every edit
// here as a compatibility decision (see ADR 0002).
public sealed record CategoryDto(Guid Id, string Name, string Slug, DateTimeOffset CreatedAtUtc);
