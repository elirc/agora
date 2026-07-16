using Agora.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agora.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.CategoryId).IsRequired();

        builder.Property(p => p.Name)
            .HasMaxLength(Product.MaxNameLength)
            .IsRequired();

        builder.Property(p => p.Description)
            .HasMaxLength(Product.MaxDescriptionLength)
            .IsRequired();

        builder.Property(p => p.Slug)
            .HasConversion(slug => slug.Value, value => Slug.From(value))
            .HasMaxLength(Slug.MaxLength)
            .IsRequired();

        builder.Property(p => p.Sku)
            .HasConversion(sku => sku.Value, value => Sku.From(value))
            .HasMaxLength(Sku.MaxLength)
            .IsRequired();

        // WHY: precision pinned to (18,2) — money is exact and base-10. This is still a bare
        // decimal (S04 promotes it to Money); the column shape is already correct so that
        // promotion is a domain change, not a schema break.
        builder.Property(p => p.PriceAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(p => p.PriceCurrency).HasMaxLength(3).IsFixedLength().IsRequired();

        builder.Property(p => p.CreatedAtUtc).IsRequired();

        // 📘 the optimistic-concurrency token, made real. IsConcurrencyToken() adds Version to the
        // WHERE clause of every UPDATE EF generates for a product — so a save only succeeds if the
        // row is still at the version we loaded. Zero rows matched → DbUpdateConcurrencyException,
        // translated to a 409 in EfUnitOfWork. This is an EXPLICIT int we manage in the domain, not
        // SQL Server rowversion or Postgres xmin: those are provider-native and absent on SQLite,
        // where the test suite runs — an int behaves identically on every provider. (ADR-0005.)
        builder.Property(p => p.Version).IsConcurrencyToken().IsRequired();

        // Soft-archive state. Nullable timestamp: NULL = active, set = archived-at. The SQLite
        // UTC-ticks converter (AgoraDbContext) already covers DateTimeOffset?, so this needs no
        // per-provider handling. IsArchived is a computed C# property with no column — see
        // ModelMappingTests' DeliberatelyUnmapped allowlist.
        builder.Property(p => p.ArchivedAtUtc);

        builder.HasIndex(p => p.Slug).IsUnique();
        builder.HasIndex(p => p.Sku).IsUnique();

        // WHY: the ?category= filter and the newest-first walk are the two shapes S01 queries
        // by. Give each its index — the category foreign key for the filter, (CreatedAtUtc, Id)
        // for the keyset page.
        builder.HasIndex(p => p.CategoryId);
        builder.HasIndex(p => new { p.CreatedAtUtc, p.Id });

        // WHY: a foreign key WITHOUT a navigation property. The aggregate boundary is the raw
        // CategoryId — a Product does not drag a whole Category graph around — but the database
        // still enforces that the id points at a real category.
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
