using Agora.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agora.Infrastructure.Persistence.Configurations;

internal sealed class StockLevelConfiguration : IEntityTypeConfiguration<StockLevel>
{
    public void Configure(EntityTypeBuilder<StockLevel> builder)
    {
        builder.ToTable("stock_levels");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.ProductId).IsRequired();
        builder.Property(s => s.OnHand).IsRequired();
        builder.Property(s => s.UpdatedAtUtc).IsRequired();

        // 📘 THE CONCURRENCY TOKEN, and this one line is what protects the last unit in the shop.
        // Without IsConcurrencyToken, `Version` is just a number the domain increments and nobody
        // checks — the UPDATE would be `SET Version = 2 WHERE Id = @id`, which two racing requests
        // both perform happily, and both oversell. With it, EF writes
        // `... WHERE Id = @id AND Version = @loaded`, so the second writer matches zero rows and
        // throws. The `if` in ReserveStock is a courtesy. This is the enforcement.
        builder.Property(s => s.Version).IsConcurrencyToken();

        // One shelf per product. The unique index is the same belt-and-braces as S02's users.email:
        // nothing in the code creates two stock rows for one product, and the day something does, it
        // fails loudly instead of splitting the count in half.
        builder.HasIndex(s => s.ProductId).IsUnique();
    }
}

internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("reservations");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.ProductId).IsRequired();
        builder.Property(r => r.CartId).IsRequired();
        builder.Property(r => r.Quantity).IsRequired();
        builder.Property(r => r.ReleasedAtUtc);

        // The window, flattened to two instants — the same shape SaleCampaign uses, and subject to
        // the same GOTCHA: these are DateTimeOffsets that the repository COMPARES in SQL (the sweep's
        // `Ends <= asOf`), which SQLite cannot do natively. AgoraDbContext's UTC-ticks converter is
        // what makes that work on both providers, and it recurses into complex types precisely so
        // that this one is covered.
        builder.ComplexProperty(r => r.Window, window =>
        {
            window.Property(w => w.Starts).HasColumnName("StartsAtUtc").IsRequired();
            window.Property(w => w.Ends).HasColumnName("EndsAtUtc").IsRequired();
        });

        // ⚠️ IsArchived's cousin: IsReleased is computed from ReleasedAtUtc and is NOT a column.
        // Third entry on ModelMappingTests' DeliberatelyUnmapped allowlist, same reasoning as the
        // first two — a stored bool beside the timestamp is a second opinion waiting to disagree.
        builder.Ignore(r => r.IsReleased);

        // The reservation lookup: every availability question filters by product.
        builder.HasIndex(r => r.ProductId);

        // The cart's claims: checkout and abandonment both start here.
        builder.HasIndex(r => r.CartId);

        // 🔗 The sweep's index. It asks for "unreleased claims whose window closed", and without this
        // that is a table scan over every reservation the shop has ever issued — growing forever,
        // run every minute. S14 hunts query shapes like this; this one is cheap enough to get right
        // on the way past.
        builder.HasIndex(r => new { r.ReleasedAtUtc });

        // WHY no foreign key to carts: a reservation OUTLIVES the cart that made it. A merged cart is
        // retired (S05) and a checked-out cart becomes an order (S07), and in both cases the claim's
        // history is evidence worth keeping — "who was holding the last skillet at 14:03" is a
        // question the shop will eventually ask. A cascade delete here would erase the answer.
    }
}
