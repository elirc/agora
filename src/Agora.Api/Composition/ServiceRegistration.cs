using Agora.Api.Auth;
using Agora.Api.Errors;
using Agora.Application.Abstractions;
using Agora.Application.Auth;
using Agora.Application.Carts;
using Agora.Application.Inventory;
using Agora.Application.Catalog;
using Agora.Application.Pricing;
using Agora.Infrastructure;

namespace Agora.Api.Composition;

/// <summary>
/// The composition root — the ONLY place in the Api allowed to see Infrastructure. The
/// architecture tests hold this line (Agora.ArchitectureTests).
/// </summary>
public static class ServiceRegistration
{
    public static WebApplicationBuilder AddAgora(this WebApplicationBuilder builder)
    {
        // Persistence (EF Core + port implementations) — Infrastructure's one entry point.
        builder.Services.AddAgoraPersistence(builder.Configuration);

        // WHY: TimeProvider.System is registered ONCE, here. Everything downstream takes
        // TimeProvider by constructor — which is why the seeder (and every later feature that
        // reads time) can be frozen in tests. The clock is a dependency from day one.
        builder.Services.AddSingleton(TimeProvider.System);

        // Use cases: scoped, cheap, no interfaces — they ARE the application's API.
        // LEARN: no IListProducts. An interface with exactly one implementation and no seam
        // consumer is ceremony; ports (repositories, the clock) earn interfaces, use cases don't.
        builder.Services.AddScoped<ListCategories>();
        builder.Services.AddScoped<GetCategory>();
        builder.Services.AddScoped<ListProducts>();
        builder.Services.AddScoped<GetProduct>();

        // S03 — admin catalog CRUD (StoreAdmin-gated inside each use case).
        builder.Services.AddScoped<CreateCategory>();
        builder.Services.AddScoped<UpdateCategory>();
        builder.Services.AddScoped<ListAdminProducts>();
        builder.Services.AddScoped<GetAdminProduct>();
        builder.Services.AddScoped<CreateProduct>();
        builder.Services.AddScoped<UpdateProduct>();
        builder.Services.AddScoped<ArchiveProduct>();
        builder.Services.AddScoped<RestoreProduct>();

        // S04 — money & time-windowed pricing. ProductPricing is not a use case: it is the shared
        // price-resolution query the storefront reads lean on (it owns the one clock read per
        // request). The three campaign use cases are StoreAdmin-gated inside each RequireRole.
        builder.Services.AddScoped<ProductPricing>();
        builder.Services.AddScoped<CreateSaleCampaign>();
        builder.Services.AddScoped<ListSaleCampaigns>();
        builder.Services.AddScoped<EndSaleCampaign>();

        // S05 — the cart. CartLookup resolves "whose cart is this?" (owned or anonymous token) and
        // CartPricing re-resolves live prices through S04's ProductPricing; both are collaborators
        // the use cases share rather than a fifth copy of the same query.
        builder.Services.AddScoped<CartLookup>();
        builder.Services.AddScoped<CartPricing>();
        builder.Services.AddScoped<GetCart>();
        builder.Services.AddScoped<AddCartLine>();
        builder.Services.AddScoped<UpdateCartLineQuantity>();
        builder.Services.AddScoped<RemoveCartLine>();
        builder.Services.AddScoped<ClearCart>();
        builder.Services.AddScoped<MergeCarts>();

        // S06 — inventory. The sweep is registered too: it is a use case like any other, and S15
        // gives it a schedule. Nothing about correctness depends on it running (see ReleaseExpired-
        // Reservations) — it is a janitor, and a janitor is still worth having.
        builder.Services.AddScoped<StockAvailabilityQuery>();
        builder.Services.AddScoped<ListLowStock>();
        builder.Services.AddScoped<ReserveStock>();
        builder.Services.AddScoped<ReleaseCartReservations>();
        builder.Services.AddScoped<ReleaseExpiredReservations>();

        builder.Services.AddScoped<RegisterCustomer>();
        builder.Services.AddScoped<Login>();
        builder.Services.AddScoped<Logout>();
        builder.Services.AddScoped<GetCurrentUser>();

        // The request's identity: the session-cookie middleware populates RequestCurrentUser;
        // use cases depend on the ICurrentUser PORT and never see HttpContext.
        builder.Services.AddScoped<RequestCurrentUser>();
        builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<RequestCurrentUser>());

        // Errors → RFC 7807, one translation table for the whole API.
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<AgoraExceptionHandler>();

        // OpenAPI: generated from code, committed to the repo, drift-gated in CI.
        builder.Services.AddOpenApi();

        return builder;
    }
}
