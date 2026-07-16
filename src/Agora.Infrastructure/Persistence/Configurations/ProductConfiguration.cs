using Agora.Domain.Catalog;
using Agora.Domain.Values;
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

        // 📘 MONEY, MAPPED — and the schema did not move. S01 pinned these two columns at
        // decimal(18,2) + char(3) and predicted that promoting the pair to a value object would be a
        // domain change, not a schema break. This is that promotion, and the AddPricing migration
        // touches no column on this table: the Money is FLATTENED back into the same PriceAmount and
        // PriceCurrency the database has had since day one. A value object is a fact about your CODE;
        // the storage can stay exactly as boring as it was.
        //
        // A complex property (EF 8+), not OwnsOne: Money is a struct with no identity, and complex
        // types model precisely that — flattened into the owner's row, no key, no separate tracking.
        // OwnsOne would demand a reference type and invent a hidden key, i.e. model a value as an
        // entity, which is the thing value objects exist to stop.
        builder.ComplexProperty(p => p.Price, price =>
        {
            price.Property(m => m.Amount)
                .HasColumnName("PriceAmount")
                .HasPrecision(18, 2)
                .IsRequired();

            // The CurrencyCode unwraps to its three letters through a converter — the same shape as
            // Slug and Sku above. GOTCHA: a converter is what makes EF treat this as a SCALAR;
            // without one it would try to nest CurrencyCode as a complex type inside a complex type.
            price.Property(m => m.Currency)
                .HasColumnName("PriceCurrency")
                .HasConversion(currency => currency.Code, value => CurrencyCode.From(value))
                .HasMaxLength(CurrencyCode.Length)
                .IsFixedLength()
                .IsRequired();
        });

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
