using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Composition;

/// <summary>
/// `dotnet run -- migrate seed` — database commands run and the process exits, so ops scripts
/// and humans share one entry point with the app itself.
/// </summary>
public static class StartupTasks
{
    /// <returns>true if commands were handled and the host should NOT start serving.</returns>
    public static async Task<bool> HandleDatabaseCommandsAsync(WebApplication app, string[] args)
    {
        bool migrate = args.Contains("migrate", StringComparer.OrdinalIgnoreCase);
        bool seed = args.Contains("seed", StringComparer.OrdinalIgnoreCase);

        if (!migrate && !seed)
        {
            return false;
        }

        using IServiceScope scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgoraDbContext>();

        if (migrate)
        {
            // WHY: relational migrations exist only for the Postgres provider; the SQLite dev
            // fallback derives its schema from the model. Same command, right behavior for each.
            if (db.Database.IsNpgsql())
            {
                await db.Database.MigrateAsync();
            }
            else
            {
                await db.Database.EnsureCreatedAsync();
            }

            app.Logger.LogInformation("Database schema is up to date.");
        }

        if (seed)
        {
            var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
            await AgoraSeeder.SeedAsync(db, clock, CancellationToken.None);
            app.Logger.LogInformation("Seed data applied.");
        }

        return true;
    }
}
