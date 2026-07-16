using Agora.Api.Mapping;
using Agora.Application.Catalog;
using Agora.Application.Errors;
using Agora.Application.Pagination;
using Agora.Contracts.V1;
using Agora.Domain.Catalog;

namespace Agora.Api.Endpoints;

/// <summary>
/// The Store Admin's back-office catalog surface: create / edit / archive / restore, all under
/// <c>/api/v1/admin</c>. Every endpoint here is StoreAdmin-only — the guard lives in the use case
/// (RequireRole), so these handlers stay the same table-of-contents thin as the storefront ones.
/// </summary>
// 🔍 read this file as route → use case → contract, exactly like CategoryEndpoints. The ONLY
// Api-specific work is shaping the 201/200 result and the ?status= parse; authorization and every
// business rule live one layer down. If a rule leaks up here, it is in the wrong place.
public static class AdminCatalogEndpoints
{
    public static RouteGroupBuilder MapAdminCatalog(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/admin").WithTags("Admin catalog");

        MapAdminCategories(group);
        MapAdminProducts(group);

        return api;
    }

    private static void MapAdminCategories(RouteGroupBuilder group)
    {
        group.MapPost("/categories", async (
            CreateCategoryRequest request,
            CreateCategory useCase,
            CancellationToken ct) =>
        {
            Category category = await useCase.ExecuteAsync(request.Name, request.Slug, ct);
            return Results.Created(
                $"/api/v1/categories/{category.Slug.Value}", category.ToAdminDto());
        })
        .Produces<AdminCategoryDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/categories/{id:guid}", async (
            Guid id,
            UpdateCategoryRequest request,
            UpdateCategory useCase,
            CancellationToken ct) =>
        {
            Category category = await useCase.ExecuteAsync(id, request.Name, request.ExpectedVersion, ct);
            return Results.Ok(category.ToAdminDto());
        })
        .Produces<AdminCategoryDto>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static void MapAdminProducts(RouteGroupBuilder group)
    {
        group.MapGet("/products", async (
            string? status,
            string? cursor,
            int? limit,
            HttpResponse response,
            ListAdminProducts useCase,
            CancellationToken ct) =>
        {
            // ⚠️ the admin list defaults to ALL — archived rows included — which is the whole point
            // of this endpoint versus the public one. ?status=active|archived narrows it.
            Page<Product> page = await useCase.ExecuteAsync(ParseStatus(status), cursor, limit, ct);

            if (page.NextCursor is { } next)
            {
                response.Headers["X-Next-Cursor"] = next;
            }

            return Results.Ok(page.Items.Select(p => p.ToAdminDto()));
        })
        .Produces<IEnumerable<AdminProductDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/products/{id:guid}", async (
            Guid id,
            GetAdminProduct useCase,
            CancellationToken ct) =>
        {
            Product product = await useCase.ExecuteAsync(id, ct);
            return Results.Ok(product.ToAdminDto());
        })
        .Produces<AdminProductDto>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/products", async (
            CreateProductRequest request,
            CreateProduct useCase,
            CancellationToken ct) =>
        {
            Product product = await useCase.ExecuteAsync(
                request.CategoryId, request.Name, request.Slug, request.Sku, request.Description,
                request.PriceAmount, request.PriceCurrency, ct);

            return Results.Created(
                $"/api/v1/products/{product.Slug.Value}", product.ToAdminDto());
        })
        .Produces<AdminProductDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/products/{id:guid}", async (
            Guid id,
            UpdateProductRequest request,
            UpdateProduct useCase,
            CancellationToken ct) =>
        {
            Product product = await useCase.ExecuteAsync(
                id, request.CategoryId, request.Name, request.Description,
                request.PriceAmount, request.PriceCurrency, request.ExpectedVersion, ct);

            return Results.Ok(product.ToAdminDto());
        })
        .Produces<AdminProductDto>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        // 409 carries the current server state (see AgoraExceptionHandler) so the admin can rebase.
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/products/{id:guid}/archive", async (
            Guid id,
            ProductVersionRequest request,
            ArchiveProduct useCase,
            CancellationToken ct) =>
        {
            Product product = await useCase.ExecuteAsync(id, request.ExpectedVersion, ct);
            return Results.Ok(product.ToAdminDto());
        })
        .Produces<AdminProductDto>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/products/{id:guid}/restore", async (
            Guid id,
            ProductVersionRequest request,
            RestoreProduct useCase,
            CancellationToken ct) =>
        {
            Product product = await useCase.ExecuteAsync(id, request.ExpectedVersion, ct);
            return Results.Ok(product.ToAdminDto());
        })
        .Produces<AdminProductDto>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    // WHY parse ?status here and not in the use case: it is a transport concern (a query string), the
    // same place we already parse ?limit and ?cursor. An unrecognised value is a malformed request
    // (400), consistent with an out-of-range limit — reject, don't silently fall back to a default.
    private static ProductStatusFilter ParseStatus(string? status) => status?.ToLowerInvariant() switch
    {
        null or "" or "all" => ProductStatusFilter.All,
        "active" => ProductStatusFilter.Active,
        "archived" => ProductStatusFilter.Archived,
        _ => throw new RequestValidationException(
            "status must be one of: active, archived, all."),
    };
}
