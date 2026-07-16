using Agora.Domain.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agora.Infrastructure.Persistence.Configurations;

internal sealed class SaleCampaignConfiguration : IEntityTypeConfiguration<SaleCampaign>
{
    public void Configure(EntityTypeBuilder<SaleCampaign> builder)
    {
        builder.ToTable("sale_campaigns");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.ProductId).IsRequired();

        builder.Property(c => c.Name)
            .HasMaxLength(SaleCampaign.MaxNameLength)
            .IsRequired();

        // 📘 a COMPLEX PROPERTY, not an owned entity. Discount is a struct with no identity of its
        // own — it is part of what a campaign IS, not a row that happens to live next door — and
        // EF's complex types are exactly that: value objects flattened into their parent's table,
        // with no key, no tracking, and no separate lifetime. (OwnsOne would insist on a reference
        // type and a hidden key, modelling a value as if it were an entity.)
        builder.ComplexProperty(c => c.Discount, discount =>
        {
            discount.Property(d => d.Kind)
                .HasColumnName("DiscountKind")
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            // WHY (18,2) even though a percentage is not money: the column also holds AmountOff
            // values, which ARE money, and one column cannot have two precisions. The precision is
            // set by the strictest thing stored in it.
            discount.Property(d => d.Value)
                .HasColumnName("DiscountValue")
                .HasPrecision(18, 2)
                .IsRequired();
        });

        // The window, flattened to two instants. GOTCHA: these are DateTimeOffsets and the repository
        // COMPARES them in SQL — which SQLite cannot do. AgoraDbContext's UTC-ticks converter is what
        // rescues that, and S04 is the sprint that had to teach it to look inside complex types
        // (before this, its sweep only walked top-level properties and would have missed these two
        // entirely — the WHERE clause would have thrown at runtime, not at build).
        builder.ComplexProperty(c => c.Window, window =>
        {
            window.Property(w => w.Starts).HasColumnName("StartsAtUtc").IsRequired();
            window.Property(w => w.Ends).HasColumnName("EndsAtUtc").IsRequired();
        });

        builder.Property(c => c.CreatedAtUtc).IsRequired();

        // ⚠️ GOTCHA — THE ONE PLACE THE VALUE OBJECT COSTS US SOMETHING, so here it is out loud.
        // Every storefront read is "campaigns for THESE products whose window covers NOW": an
        // equality on ProductId then a range on StartsAtUtc, which wants the composite index
        // (ProductId, StartsAtUtc) — equality column first, range column last, so the engine seeks to
        // the product and scans one contiguous slice. We cannot declare it. EF Core 10 does not
        // support indexes over a COMPLEX TYPE's properties: `HasIndex(c => new { c.ProductId,
        // c.Window.Starts })` throws ("not a valid member access expression") and the string form
        // throws too ("no corresponding CLR property"). The column exists and the query uses it; only
        // the model's index builder cannot name it.
        //
        // So: index the equality column, and accept a small scan of one product's campaigns. That is
        // genuinely fine here — a product accumulates a handful of campaigns in its whole life, not
        // thousands — which is why the honest fix is to do nothing rather than to un-make the value
        // object for an index we do not need. If a product ever DID accumulate thousands, the escape
        // hatch is one line of raw SQL in the migration (`migrationBuilder.Sql("CREATE INDEX ... ON
        // sale_campaigns (\"ProductId\", \"StartsAtUtc\")")`) — the columns are right there. Recorded
        // in ADR-0006 under consequences, because a limitation you did not write down is one the next
        // person rediscovers at 2am.
        builder.HasIndex(c => c.ProductId);

        // A campaign belongs to a product; the FK is the aggregate boundary, with no navigation
        // property in either direction (same call the catalog made — a Product does not drag its
        // campaigns around, and a campaign does not drag its product).
        // WHY Restrict and not Cascade: products are soft-archived, never deleted, so a cascade would
        // be dead code guarding a delete that cannot happen — and if one ever could, silently
        // vaporizing the pricing history of everything that product ever sold is not the behaviour
        // anybody would choose. Make the delete fail loudly instead.
        builder.HasOne<Domain.Catalog.Product>()
            .WithMany()
            .HasForeignKey(c => c.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
