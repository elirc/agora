using Agora.Application.Abstractions;
using Agora.Infrastructure.Events;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Persistence.Repositories;
using Agora.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Agora.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers persistence: EF Core (Postgres for real life, SQLite for a zero-dependency
    /// local start) plus every port implementation this layer owns.
    /// </summary>
    public static IServiceCollection AddAgoraPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // WHY: provider selection is configuration, not code — the same binary runs against
        // compose Postgres (Database:Provider=postgres) or a local SQLite file (the default
        // when nothing is configured). Tests bypass this entirely and inject an in-memory
        // SQLite connection.
        string provider = configuration["Database:Provider"] ?? "sqlite";
        string connectionString = configuration.GetConnectionString("agora")
            ?? "Data Source=agora.dev.db";

        services.AddDbContext<AgoraDbContext>(options =>
        {
            _ = provider.ToLowerInvariant() switch
            {
                "postgres" => options.UseNpgsql(connectionString),
                "sqlite" => options.UseSqlite(connectionString),
                _ => throw new InvalidOperationException(
                    $"Unknown Database:Provider '{provider}'. Use 'postgres' or 'sqlite'."),
            };
        });

        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ISaleCampaignRepository, SaleCampaignRepository>();
        services.AddScoped<ICartRepository, CartRepository>();
        services.AddScoped<IInventoryRepository, InventoryRepository>();

        // S05 — the event seam. Today events are written to the log and nothing more; the PORT is
        // what matters, because S10 replaces this adapter with a transactional outbox without a
        // single use case changing. Delivery guarantees are that sprint's subject, not this one's.
        services.AddScoped<IEventSink, LoggingEventSink>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ISessionTokens, SessionTokenService>();

        return services;
    }
}
