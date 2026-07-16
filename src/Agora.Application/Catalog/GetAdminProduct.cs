using Agora.Application.Abstractions;
using Agora.Application.Errors;
using Agora.Domain.Access;
using Agora.Domain.Catalog;

namespace Agora.Application.Catalog;

/// <summary>
/// Store-admin read of a single product by id — including archived ones, which the storefront
/// GET-by-slug hides. Backs the admin edit form (which needs the current fields AND version).
/// </summary>
public sealed class GetAdminProduct(IProductRepository products, ICurrentUser currentUser)
{
    public async Task<Product> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        currentUser.RequireRole(Role.StoreAdmin);

        return await products.FindByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("product", id);
    }
}
