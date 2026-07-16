using Agora.Application.Abstractions;
using Agora.Application.Errors;
using Agora.Domain.Catalog;

namespace Agora.Application.Catalog;

public sealed class GetCategory(ICategoryRepository categories)
{
    public async Task<Category> ExecuteAsync(string rawSlug, CancellationToken cancellationToken)
    {
        // WHY: Slug.From throws DomainRuleException (422) for garbage input — "Not A Slug"
        // is a malformed business identifier, not a missing resource. A well-formed slug
        // that matches nothing is the 404 below.
        Slug slug = Slug.From(rawSlug);

        Category? category = await categories.FindBySlugAsync(slug, cancellationToken);

        return category ?? throw new NotFoundException("category", slug);
    }
}
