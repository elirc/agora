using Agora.Domain.Carts;

namespace Agora.Application.Abstractions;

/// <summary>
/// The carts store. Every read here is scoped to ONE holder — a user id or a token hash — and there
/// is deliberately no <c>FindByIdAsync</c>.
/// </summary>
// 🔍 READ THE MISSING METHOD. Every other repository in Agora has a by-primary-key lookup, and this
// one does not, because a cart id is not a thing a caller should ever be able to name. There is no
// route that takes one, no DTO that carries one, and therefore no way to ask for a cart that is not
// yours — the only two doors both require you to already BE the holder. That is what "ownership
// enforced by the query" costs: one method you decline to write. Add FindByIdAsync here, ship it
// with no caller, and the sixth use case someone writes in a hurry will use it.
// ⚠️ every method returns TRACKED entities (no AsNoTracking anywhere in the cart adapter). Unlike the
// catalog, whose reads massively outnumber its writes, every cart read in this sprint is either a
// write in disguise or one request away from being one — and a mutation on a detached cart saves
// nothing, silently.
public interface ICartRepository
{
    /// <summary>The signed-in customer's live cart, or null if they have never had one.</summary>
    Task<Cart?> FindActiveByUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// The live cart an anonymous token holds, or null. Retired carts are never returned — which is
    /// what makes a repeated merge a no-op instead of a second merge.
    /// </summary>
    Task<Cart?> FindActiveByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    void Add(Cart cart);
}
