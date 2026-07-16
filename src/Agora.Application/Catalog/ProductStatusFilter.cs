namespace Agora.Application.Catalog;

/// <summary>
/// Which archive states a product query should return. There is no default — every caller of a
/// catalog read port names one, so "did we remember to hide archived rows from shoppers?" is a
/// visible argument at the call site, not an assumption buried in the repository.
/// </summary>
// ⚠️ this enum is the shape of the soft-archive decision. The storefront always asks for
// <see cref="Active"/>; the admin list defaults to <see cref="All"/> and narrows on ?status=.
// Adding a third state (say, a future "draft") means adding a case here and in the repository —
// the compiler then walks you to every switch that must account for it.
public enum ProductStatusFilter
{
    /// <summary>Only products a shopper may see — not archived.</summary>
    Active,

    /// <summary>Only archived products — the admin's "archived" tab.</summary>
    Archived,

    /// <summary>Every product regardless of archive state — the admin's default view.</summary>
    All,
}
