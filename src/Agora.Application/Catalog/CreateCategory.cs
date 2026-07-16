using Agora.Application.Abstractions;
using Agora.Domain;
using Agora.Domain.Access;
using Agora.Domain.Catalog;

namespace Agora.Application.Catalog;

/// <summary>
/// Store-admin action: add a category. The slug is generated from the name unless the admin
/// supplies an explicit one; either way it is fixed for the life of the category.
/// </summary>
public sealed class CreateCategory(
    ICategoryRepository categories,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public async Task<Category> ExecuteAsync(
        string? name,
        string? slug,
        CancellationToken cancellationToken)
    {
        // WHY the guard is the FIRST line: authorization is a precondition, not an afterthought.
        // Anonymous → 401, any non-admin → 403, before a single field is read or a row is touched.
        currentUser.RequireRole(Role.StoreAdmin);

        // LEARN: generate the slug from the name, OR honour an explicit one — both end at Slug's
        // single validating door. A blank explicit slug means "generate for me".
        Slug categorySlug = string.IsNullOrWhiteSpace(slug) ? Slug.Generate(name) : Slug.From(slug);

        if (await categories.FindBySlugAsync(categorySlug, cancellationToken) is not null)
        {
            // Friendly 422 for the common case; the unique index (Infrastructure) is the real
            // guarantee that closes the race two simultaneous creates could win.
            throw new DomainRuleException($"A category with the slug '{categorySlug}' already exists.");
        }

        var category = Category.Create(name, categorySlug, clock.GetUtcNow());

        categories.Add(category);
        await unitOfWork.CommitAsync(cancellationToken);

        return category;
    }
}
