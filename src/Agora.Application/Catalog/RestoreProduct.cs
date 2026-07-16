using Agora.Application.Abstractions;
using Agora.Application.Errors;
using Agora.Domain.Access;
using Agora.Domain.Catalog;

namespace Agora.Application.Catalog;

/// <summary>Store-admin action: return an archived product to the storefront, version-guarded.</summary>
public sealed class RestoreProduct(
    IProductRepository products,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser)
{
    public async Task<Product> ExecuteAsync(
        Guid id,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        currentUser.RequireRole(Role.StoreAdmin);

        Product product = await products.FindByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("product", id);

        if (product.Version != expectedVersion)
        {
            throw new ConcurrencyConflictException("product", product);
        }

        product.Restore();
        await unitOfWork.CommitAsync(cancellationToken);

        return product;
    }
}
