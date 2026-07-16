using Agora.Application.Abstractions;
using Agora.Domain;
using Agora.Domain.Access;
using Agora.Domain.Catalog;
using Agora.Domain.Values;

namespace Agora.Application.Catalog;

/// <summary>
/// Store-admin action: add a product to a category. Slug generates from the name (or an explicit
/// one is honoured); both slug and SKU must be unique across the catalog — including archived rows,
/// which keep their identifiers reserved.
/// </summary>
public sealed class CreateProduct(
    IProductRepository products,
    ICategoryRepository categories,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public async Task<Product> ExecuteAsync(
        Guid categoryId,
        string? name,
        string? slug,
        string? sku,
        string? description,
        decimal priceAmount,
        string? priceCurrency,
        CancellationToken cancellationToken)
    {
        currentUser.RequireRole(Role.StoreAdmin);

        // WHY validate the FK ourselves rather than let the database raise it: a missing category is
        // a well-formed request the business rejects (422), not a 500. The FK constraint remains the
        // integrity backstop; this is the friendly, specific error.
        if (await categories.FindByIdAsync(categoryId, cancellationToken) is null)
        {
            throw new DomainRuleException("That category does not exist.");
        }

        Slug productSlug = string.IsNullOrWhiteSpace(slug) ? Slug.Generate(name) : Slug.From(slug);
        Sku productSku = Sku.From(sku);

        // LEARN: uniqueness is checked against ALL products, archived included — an archived product
        // still owns its slug and SKU, so reusing them would collide the day it is restored. The
        // unique indexes (Infrastructure) are the real guarantee; these checks are the kind error.
        if (await products.FindBySlugAsync(productSlug, ProductStatusFilter.All, cancellationToken) is not null)
        {
            throw new DomainRuleException($"A product with the slug '{productSlug}' already exists.");
        }

        if (await products.FindBySkuAsync(productSku, cancellationToken) is not null)
        {
            throw new DomainRuleException($"A product with the SKU '{productSku}' already exists.");
        }

        // 📘 THE BOUNDARY IS WHERE PRIMITIVES BECOME VALUES. `priceAmount` and `priceCurrency` came
        // off an HTTP request as a JSON number and a string; from this line on they are a Money, and
        // nothing downstream can add them to a different currency, round them a second way, or lose
        // the pairing between the two. Note the rounding: Money.Of applies Agora's ONE policy here,
        // at the door — an admin who types 19.999 gets 20.00 stored and 20.00 echoed back, decided
        // visibly at the boundary rather than surfacing later inside some multiplication. (ADR-0006.)
        var product = Product.Create(
            categoryId, name, productSlug, productSku, description,
            Money.Of(priceAmount, priceCurrency), clock.GetUtcNow());

        products.Add(product);
        await unitOfWork.CommitAsync(cancellationToken);

        return product;
    }
}
