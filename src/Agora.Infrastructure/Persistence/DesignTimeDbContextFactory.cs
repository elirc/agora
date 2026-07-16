using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Agora.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef` at design time to build a context for migration scaffolding.
/// Never runs in the application.
/// </summary>
// WHY: migrations are generated against the Postgres provider because Postgres is what
// production means here; tests run SQLite via EnsureCreated (model-derived schema) and never
// touch these files.
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AgoraDbContext>
{
    public AgoraDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=agora;Username=agora;Password=agora")
            .Options;

        return new AgoraDbContext(options);
    }
}
