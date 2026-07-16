using Agora.Application.Abstractions;
using Agora.Domain.Carts;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Persistence.Repositories;

internal sealed class CartRepository(AgoraDbContext db) : ICartRepository
{
    public Task<Cart?> FindActiveByUserAsync(Guid userId, CancellationToken cancellationToken) =>
        Live().SingleOrDefaultAsync(cart => cart.UserId == userId, cancellationToken);

    public Task<Cart?> FindActiveByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        Live().SingleOrDefaultAsync(cart => cart.TokenHash == tokenHash, cancellationToken);

    public void Add(Cart cart) => db.Carts.Add(cart);

    // 🔍 THIS PRIVATE METHOD IS WHERE TWO OF THE SPRINT'S RULES ACTUALLY LIVE, so it is worth more
    // attention than a three-line helper usually gets.
    //
    // `Where(RetiredAtUtc == null)` is what makes a merge idempotent. Not a flag anybody checks, not
    // a guard in a use case — a filter in the only two queries that can find a cart. A cart consumed
    // by a merge is not "findable but marked"; it is unreachable, so a second merge with the same
    // token finds nothing and does nothing, and no use case had to remember. Ownership and lifetime
    // both enforced by the query is the same trick twice.
    //
    // `Include(Lines)` because a cart without its lines is not a cart, it is a row — every single
    // caller wants them, so there is no lazy-loading question to get wrong and no N+1 to discover in
    // S14. GOTCHA: no AsNoTracking anywhere in this file, unlike the catalog's read paths. Every cart
    // read either leads to a write in the same request or is one request away from doing so, and a
    // mutation on a detached cart saves precisely nothing, silently. The catalog can afford
    // AsNoTracking because its reads outnumber its writes by four orders of magnitude; a cart's do
    // not.
    private IQueryable<Cart> Live() =>
        db.Carts
            .Include(cart => cart.Lines)
            .Where(cart => cart.RetiredAtUtc == null);
}
