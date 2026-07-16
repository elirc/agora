using Agora.Domain.Access;
using Agora.Domain.Carts;
using Agora.Domain.Catalog;
using Agora.Domain.Inventory;
using Agora.Domain.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
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

    // S04 — time-windowed sale prices. Read on every storefront page, written only by the admin.
    public DbSet<SaleCampaign> SaleCampaigns => Set<SaleCampaign>();

    // S05 — carts. Note there is no DbSet<CartLine>: a line is reachable only through its cart, which
    // is what an aggregate root MEANS. Adding one would hand every future caller a way to load, query
    // and mutate a line without the cart that owns it — and the boundary the domain draws would be one
    // `db.CartLines.Where(...)` away from meaningless.
    public DbSet<Cart> Carts => Set<Cart>();

    // S06 — inventory. Two DbSets and NOT an aggregate, which is the opposite call to Cart/CartLine
    // above and worth the contrast. A cart line is meaningless without its cart. A reservation is a
    // claim in its own right: it outlives the cart that made it, it is queried by the sweep across
    // every product at once, and the availability question needs the claims WITHOUT loading a shelf
    // through a navigation. Different lifetimes, different query shapes, different roots.
    public DbSet<StockLevel> StockLevels => Set<StockLevel>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

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
    // which SQLite likewise cannot order/compare — still harmless, because nothing sorts or filters
    // on price: the catalog walks (CreatedAtUtc, Id), and S04's sale windows are compared as
    // INSTANTS, never as amounts. The day "sort by price" ships, it needs this treatment too.
    //
    // ⚠️ S04 GREW THIS SWEEP, and why is the lesson. It used to walk each entity's top-level
    // properties, which was complete — right up until Money and DateRange arrived as COMPLEX
    // PROPERTIES, whose scalars hang off the complex type rather than off the entity. A campaign's
    // StartsAtUtc/EndsAtUtc would have been silently skipped, and the storefront's "which campaigns
    // cover this instant?" filter would have thrown on SQLite: at runtime, in some other test, with a
    // message about a DateTimeOffset and no mention of this file. A sweep over "everything" is only
    // ever as good as its definition of everything, and the model just grew a new place to hide.
    private static void ApplySqliteTimestampWorkaround(ModelBuilder modelBuilder)
    {
        var utcTicks = new ValueConverter<DateTimeOffset, long>(
            value => value.UtcTicks,
            ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            ApplyTo(entityType.GetProperties(), utcTicks);

            foreach (IMutableComplexProperty complexProperty in entityType.GetComplexProperties())
            {
                ApplyTo(complexProperty.ComplexType.GetProperties(), utcTicks);
            }
        }
    }

    private static void ApplyTo(
        IEnumerable<IMutableProperty> properties,
        ValueConverter<DateTimeOffset, long> converter)
    {
        foreach (IMutableProperty property in properties
                     .Where(p => p.ClrType == typeof(DateTimeOffset)
                                 || p.ClrType == typeof(DateTimeOffset?)))
        {
            property.SetValueConverter(converter);
        }
    }
}
