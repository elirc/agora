using Agora.Application.Abstractions;
using Agora.Application.Errors;
using Agora.Domain.Access;
using Agora.Domain.Catalog;

namespace Agora.Application.Catalog;

/// <summary>Store-admin action: soft-archive a product (remove it from the storefront), version-guarded.</summary>
public sealed class ArchiveProduct(
    IProductRepository products,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public async Task<Product> ExecuteAsync(
        Guid id,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        currentUser.RequireRole(Role.StoreAdmin);

        Product product = await products.FindByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("product", id);

        // WHY archive is also version-guarded: "archive" and "edit" race against each other just
        // like two edits do. An admin who clicks Archive on a view that is three saves stale should
        // be stopped, not allowed to hide a product someone else just re-priced.
        if (product.Version != expectedVersion)
        {
            throw new ConcurrencyConflictException("product", product);
        }

        product.Archive(clock.GetUtcNow());
        await unitOfWork.CommitAsync(cancellationToken);

        return product;
    }
}
