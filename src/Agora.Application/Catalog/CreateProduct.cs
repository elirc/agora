using Agora.Application.Abstractions;
using Agora.Domain;
using Agora.Domain.Access;
using Agora.Domain.Catalog;
using Agora.Domain.Inventory;
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
    IInventoryRepository inventory,
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

        // 📘 EVERY PRODUCT GETS A SHELF, EVEN AN EMPTY ONE — added in S06, and the reason is a bug the
        // authorization matrix found rather than a tidiness urge.
        //
        // Without this line, a freshly created product has no stock row, and "no stock row" is a state
        // the rest of the system cannot agree about: GET /products/{id}/stock answers "0 available"
        // (StockAvailabilityQuery treats a missing row as none), while POST /cart/reservations answers
        // "404, no such thing" (ReserveStock cannot find a shelf to check). Same product, same instant,
        // two incompatible stories — which is precisely the contradiction S06 spent three commits
        // deleting from the pricing code.
        //
        // Creating the shelf here makes "no stock row" mean exactly one thing again: THE PRODUCT DOES
        // NOT EXIST. So the 404 in ReserveStock becomes true instead of merely usual, and 0-on-hand
        // means what it says — the product is real and we have none of it, which is a different
        // sentence from "there is no such product" and deserves to stay one.
        //
        // 🔗 Note this commits in the SAME unit of work: a product without a shelf cannot exist, not
        // even for the width of a failed transaction. S02 introduced IUnitOfWork for exactly this —
        // several writes, one atomic truth.
        inventory.Add(StockLevel.Create(product.Id, 0, clock.GetUtcNow()));

        await unitOfWork.CommitAsync(cancellationToken);

        return product;
    }
}
