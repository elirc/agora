using Agora.Api.Mapping;
using Agora.Application.Pricing;
using Agora.Contracts.V1;

namespace Agora.Api.Endpoints;

/// <summary>
/// The Store Admin's pricing surface: schedule a sale on a product, see every campaign it has ever
/// had, end one early. All under <c>/api/v1/admin</c>, all StoreAdmin-only (the guard lives in the
/// use case's RequireRole, enumerated in AuthorizationMatrixTests).
/// </summary>
// 🔍 THE ROUTE SHAPE IS AN ARGUMENT ABOUT OWNERSHIP. Campaigns are nested under their product for
// creating and listing (`/admin/products/{id}/campaigns`) because a campaign cannot exist without
// one — the product is its parent, and the URL says so. Ending a campaign is NOT nested
// (`/admin/campaigns/{id}/end`): the campaign's id already identifies it uniquely, and repeating the
// product id would let a caller present a mismatched pair we would then have to validate and reject.
// Nest to express containment, flatten once the child has its own identity — and never build a URL
// that can be self-contradictory.
public static class AdminPricingEndpoints
{
    public static RouteGroupBuilder MapAdminPricing(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/admin").WithTags("Admin pricing");

        group.MapGet("/products/{id:guid}/campaigns", async (
            Guid id,
            ListSaleCampaigns useCase,
            CancellationToken ct) =>
        {
            IReadOnlyList<CampaignView> views = await useCase.ExecuteAsync(id, ct);
            return Results.Ok(views.Select(view => view.ToAdminDto()));
        })
        .Produces<IEnumerable<AdminSaleCampaignDto>>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/products/{id:guid}/campaigns", async (
            Guid id,
            CreateSaleCampaignRequest request,
            CreateSaleCampaign useCase,
            CancellationToken ct) =>
        {
            CampaignView view = await useCase.ExecuteAsync(
                id, request.Name, request.DiscountKind, request.DiscountValue,
                request.StartsAtUtc, request.EndsAtUtc, ct);

            // WHY the Location points at the LIST and not at a campaign-by-id endpoint: there isn't
            // one, and inventing a GET nobody needs just to satisfy a header would be a route we then
            // have to authorize, test, document, and keep forever. Point at the collection the new row
            // lives in; add the item route the day something actually fetches it.
            return Results.Created($"/api/v1/admin/products/{id}/campaigns", view.ToAdminDto());
        })
        .Produces<AdminSaleCampaignDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/campaigns/{id:guid}/end", async (
            Guid id,
            EndSaleCampaign useCase,
            CancellationToken ct) =>
        {
            CampaignView view = await useCase.ExecuteAsync(id, ct);
            return Results.Ok(view.ToAdminDto());
        })
        .Produces<AdminSaleCampaignDto>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return api;
    }
}
