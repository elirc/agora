using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Agora.Api.Tests.Infrastructure;

/// <summary>
/// Boots the real API over an in-memory SQLite database. No Docker, no network — the whole
/// HTTP stack, real EF, real DI, in milliseconds.
/// </summary>
// LEARN: the connection is held open for the factory's lifetime because an in-memory SQLite
// database lives exactly as long as its (only) open connection. Close it and the schema
// evaporates mid-test.
public sealed class AgoraApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Swap the configured provider for the test connection.
            services.RemoveAll<DbContextOptions<AgoraDbContext>>();

            _connection.Open();
            services.AddDbContext<AgoraDbContext>(options => options.UseSqlite(_connection));

            // Schema + demo catalog, once per factory.
            using ServiceProvider provider = services.BuildServiceProvider();
            using IServiceScope scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AgoraDbContext>();

            db.Database.EnsureCreated();

            // WHY the seeder's own guard matters: WithWebHostBuilder (used by tests that swap an
            // adapter) can re-run this over the SAME in-memory connection, so seeding must be
            // idempotent — AgoraSeeder no-ops when categories already exist.
            AgoraSeeder.SeedAsync(db, TimeProvider.System, CancellationToken.None)
                .GetAwaiter().GetResult();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection.Dispose();
    }
}

file static class ServiceCollectionExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        foreach (ServiceDescriptor descriptor in
                 services.Where(d => d.ServiceType == typeof(T)).ToList())
        {
            services.Remove(descriptor);
        }
    }
}
