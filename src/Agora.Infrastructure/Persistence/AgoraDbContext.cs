using Agora.Domain.Access;
using Agora.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Agora.Infrastructure.Persistence;

public sealed class AgoraDbContext(DbContextOptions<AgoraDbContext> options)
    : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();

    // S02 — identity. Users own carts and (from S07) orders; sessions back the login cookie.
    public DbSet<User> Users => Set<User>();
    public DbSet<Session> Sessions => Set<Session>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AgoraDbContext).Assembly);

        if (Database.IsSqlite())
        {
            ApplySqliteTimestampWorkaround(modelBuilder);
        }
    }

    // GOTCHA: SQLite has no real timestamp type; the provider stores DateTimeOffset as TEXT
    // and then refuses to ORDER BY or compare it — our keyset pagination would 500. On SQLite
    // only, store every DateTimeOffset as UTC ticks (INTEGER): ordering and comparison become
    // integer ops, and semantics stay identical because Agora only ever stores UTC instants.
    // LEARN: this is what "the same model, different providers" actually costs — and why the
    // Api.Tests run on SQLite is not a perfect proxy for Postgres. Anything provider-sensitive
    // gets an explicit note (see ADR 0001 consequences). The product PRICE is a `decimal`,
    // which SQLite likewise cannot order/compare — harmless in S01 because no query sorts or
    // filters on price; when S04's Money arrives, price ordering gets the same treatment.
    private static void ApplySqliteTimestampWorkaround(ModelBuilder modelBuilder)
    {
        var utcTicks = new ValueConverter<DateTimeOffset, long>(
            value => value.UtcTicks,
            ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties()
                         .Where(p => p.ClrType == typeof(DateTimeOffset)
                                     || p.ClrType == typeof(DateTimeOffset?)))
            {
                property.SetValueConverter(utcTicks);
            }
        }
    }
}
