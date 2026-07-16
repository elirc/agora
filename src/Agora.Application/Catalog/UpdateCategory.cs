using Agora.Application.Abstractions;
using Agora.Application.Errors;
using Agora.Domain.Access;
using Agora.Domain.Catalog;

namespace Agora.Application.Catalog;

/// <summary>Store-admin action: rename a category, guarded by its expected version.</summary>
public sealed class UpdateCategory(
    ICategoryRepository categories,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser)
{
    public async Task<Category> ExecuteAsync(
        Guid id,
        string? name,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        currentUser.RequireRole(Role.StoreAdmin);

        Category category = await categories.FindByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("category", id);

        if (category.Version != expectedVersion)
        {
            throw new ConcurrencyConflictException("category", category);
        }

        category.Rename(name);
        await unitOfWork.CommitAsync(cancellationToken);

        return category;
    }
}
