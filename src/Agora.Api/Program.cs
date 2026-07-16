using Agora.Api.Composition;
using Agora.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddAgora();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Resolves the agora_session cookie (if any) into the request's current user. Downstream,
// authorization is a POLICY decision (ICurrentUser.RequireUser/RequireRole), never ambient state.
app.UseMiddleware<Agora.Api.Auth.SessionCookieAuthenticator>();

// OpenAPI is served in every environment: the drift test (Api.Tests) and CI diff the committed
// document against exactly what this endpoint returns.
app.MapOpenApi();

// WHY: every catalog endpoint below is ANONYMOUS — no authentication, no authorization — and
// that is deliberate for exactly one sprint. Sprint 01 is the walking skeleton: it proves the
// architecture, the contract, and the pipeline on a read-only slice a shopper could see anyway.
// Identity (cookie sessions, roles, ownership) lands in Sprint 02, and the moment there is a
// thing worth protecting (a cart, an order), it will be protected. Read-only public catalog is
// not that thing yet — so the skeleton stays open and says so, loudly, right here.
RouteGroupBuilder api = app.MapGroup("/api/v1");
api.MapCategories();
api.MapProducts();
api.MapAuth();

// `dotnet run -- migrate seed` performs database chores and exits.
if (await StartupTasks.HandleDatabaseCommandsAsync(app, args))
{
    return;
}

app.Run();

// WHY: WebApplicationFactory (Api.Tests) and the architecture tests need a compile-safe handle
// to this assembly's entry point.
public partial class Program;
