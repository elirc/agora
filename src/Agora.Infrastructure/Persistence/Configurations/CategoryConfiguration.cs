using Agora.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agora.Infrastructure.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Name)
            .HasMaxLength(Category.MaxNameLength)
            .IsRequired();

        // LEARN: the value object lives in the Domain; the fact that it's a VARCHAR column
        // lives HERE. Neither knows about the other — the converter is the border crossing,
        // and it re-validates on the way in (Slug.From). ⚠️ every read-only property is listed
        // explicitly: EF SILENTLY skips getter-only auto-properties, so an omission here is an
        // invisible missing column (ModelMappingTests turns that silence into a red build).
        builder.Property(c => c.Slug)
            .HasConversion(slug => slug.Value, value => Slug.From(value))
            .HasMaxLength(Slug.MaxLength)
            .IsRequired();

        // WHY: slugs are how the storefront routes to a category (/api/v1/categories/{slug}),
        // so two categories may not share one. The database is the last line that enforces it.
        builder.HasIndex(c => c.Slug).IsUnique();

        builder.Property(c => c.CreatedAtUtc).IsRequired();

        // The same explicit-int concurrency token Product carries — see ProductConfiguration and
        // ADR-0005. A category rename is rare, but the token costs one column and one line.
        builder.Property(c => c.Version).IsConcurrencyToken().IsRequired();

        // WHY: the newest-first cursor walk (ADR 0002) orders by (CreatedAtUtc, Id) — this
        // index IS that query. Pagination without its index is a slow table scan wearing a
        // fast API.
        builder.HasIndex(c => new { c.CreatedAtUtc, c.Id });
    }
}
