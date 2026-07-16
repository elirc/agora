using Agora.Application.Abstractions;
using Agora.Application.Errors;
using Agora.Domain;
using Agora.Domain.Access;
using Agora.Domain.Catalog;

namespace Agora.Application.Catalog;

/// <summary>
/// Store-admin action: edit a product's category, name, description, and price — guarded by the
/// version the admin was editing. The slug and SKU are immutable and are not accepted here.
/// </summary>
public sealed class UpdateProduct(
    IProductRepository products,
    ICategoryRepository categories,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser)
{
    public async Task<Product> ExecuteAsync(
        Guid id,
        Guid categoryId,
        string? name,
        string? description,
        decimal priceAmount,
        string? priceCurrency,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        currentUser.RequireRole(Role.StoreAdmin);

        Product product = await products.FindByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("product", id);

        // 🔍 THE LOST-UPDATE GATE. Trace two admins editing product v3:
        //   1. Admin A loads v3.  2. Admin B loads v3.  3. Admin B saves → row becomes v4.
        //   4. Admin A saves, still presenting expectedVersion=3.
        // Here at step 4, product.Version is 4 (B already committed) but expectedVersion is 3 — so
        // A is rejected with a 409 that CARRIES the current product, and B's edit survives. Without
        // this check, A's write would overwrite B's silently: the lost update. Note we return the
        // loaded entity in the exception so the API can show A exactly what B changed.
        if (product.Version != expectedVersion)
        {
            throw new ConcurrencyConflictException("product", product);
        }

        // The moving of a product between categories is an edit like any other — but the target
        // category must exist (a 422, not a 500 from a dangling FK).
        if (categoryId != product.CategoryId
            && await categories.FindByIdAsync(categoryId, cancellationToken) is null)
        {
            throw new DomainRuleException("That category does not exist.");
        }

        product.UpdateDetails(categoryId, name, description, priceAmount, priceCurrency);

        // The domain has bumped Version to expectedVersion+1. On commit, EF's concurrency token
        // (Version was the ORIGINAL loaded value) closes the tiny remaining window: if some other
        // writer slipped in between our load and this save, SaveChanges affects zero rows and
        // EfUnitOfWork translates the DbUpdateConcurrencyException into the same 409.
        await unitOfWork.CommitAsync(cancellationToken);

        return product;
    }
}
