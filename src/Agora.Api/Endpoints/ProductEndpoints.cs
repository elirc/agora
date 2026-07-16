using Agora.Api.Mapping;
using Agora.Application.Catalog;
using Agora.Application.Pagination;
using Agora.Contracts.V1;
using Agora.Domain.Catalog;

namespace Agora.Api.Endpoints;

public static class ProductEndpoints
{
    public static RouteGroupBuilder MapProducts(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/products").WithTags("Products");

        group.MapGet("/", async (
            string? category,
            string? cursor,
            int? limit,
            HttpResponse response,
            ListProducts useCase,
            CancellationToken ct) =>
        {
            // WHY: `category` is the ONE whitelisted filter param. Anything else in the query
            // string is ignored, not honored — an explicit allowlist per resource (ADR 0002),
            // so a typo'd filter can never silently widen a query.
            Page<Product> page = await useCase.ExecuteAsync(category, cursor, limit, ct);

            if (page.NextCursor is { } next)
            {
                response.Headers["X-Next-Cursor"] = next;
            }

            return Results.Ok(page.Items.Select(p => p.ToDto()));
        })
        .Produces<IEnumerable<ProductDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{slug}", async (string slug, GetProduct useCase, CancellationToken ct) =>
        {
            var product = await useCase.ExecuteAsync(slug, ct);
            return Results.Ok(product.ToDto());
        })
        .Produces<ProductDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return api;
    }
}
