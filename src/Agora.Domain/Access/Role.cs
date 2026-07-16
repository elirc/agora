namespace Agora.Domain.Access;

/// <summary>
/// What a principal may do in the single Agora store. There is no multi-tenancy here (one
/// store), so a role is a flat capability set, not an org-scoped grant.
/// </summary>
// LEARN: these are CAPABILITIES, not a hierarchy. Ops is not a super-admin that implies
// StoreAdmin — fulfillment and back-office are different jobs. An endpoint that both may
// use lists BOTH roles (RequireRole(StoreAdmin, Ops)); it never leans on "Ops implies
// everything". Folding a god-role into the enum is how authorization quietly rots into
// "well, ops can do anything" with no audit and no test. See docs/adr/0004.
public enum Role
{
    /// <summary>A registered shopper: owns a cart, checks out, sees their own orders.</summary>
    Customer = 1,

    /// <summary>Back-office: catalog, pricing, inventory, promotions (from S03).</summary>
    StoreAdmin = 2,

    /// <summary>Fulfillment: order pipeline, refunds, webhook subscriptions (from S07).</summary>
    Ops = 3,
}
