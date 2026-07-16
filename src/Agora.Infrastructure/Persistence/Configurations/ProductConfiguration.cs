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
