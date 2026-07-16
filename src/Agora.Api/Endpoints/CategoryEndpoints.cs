using Agora.Api.Mapping;
using Agora.Application.Catalog;
using Agora.Application.Pagination;
using Agora.Contracts.V1;
using Agora.Domain.Catalog;

namespace Agora.Api.Endpoints;

public static class CategoryEndpoints
{
    // 🔍 an endpoint file should read like a table of contents: route → use case → contract.
    // If you find yourself scrolling here, logic has leaked upward into the wrong layer.
    public static RouteGroupBuilder MapCategories(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/categories").WithTags("Categories");

        group.MapGet("/", async (
            string? cursor,
            int? limit,
            HttpResponse response,
            ListCategories useCase,
            CancellationToken ct) =>
        {
            Page<Category> page = await useCase.ExecuteAsync(cursor, limit, ct);

            // LEARN: the next-page token rides a response HEADER (X-Next-Cursor), so the body
            // stays a plain array of the resource. Envelope-vs-header is a real API-design fork;
            // ADR 0002 records why Agora chose headers.
            if (page.NextCursor is { } next)
            {
                response.Headers["X-Next-Cursor"] = next;
            }

            return Results.Ok(page.Items.Select(c => c.ToDto()));
        })
        .Produces<IEnumerable<CategoryDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{slug}", async (string slug, GetCategory useCase, CancellationToken ct) =>
        {
            var category = await useCase.ExecuteAsync(slug, ct);
            return Results.Ok(category.ToDto());
        })
        .Produces<CategoryDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return api;
    }
}
