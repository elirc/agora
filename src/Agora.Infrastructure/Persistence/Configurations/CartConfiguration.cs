using Agora.Domain.Carts;
using Agora.Domain.Values;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agora.Infrastructure.Persistence.Configurations;

internal sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("carts");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        // Both nullable, and exactly one is set — the invariant lives in Cart's two factories, not in
        // a CHECK constraint. WHY not the constraint: its syntax differs per provider (and the test
        // provider is not the production one), it would fire at SaveChanges as a DbUpdateException
        // with a message naming a constraint rather than a rule, and the domain already makes the
        // illegal state unconstructable. A database constraint guards against OTHER writers; Agora
        // has none, and the day it does (an import job, a support script) is the day this earns one.
        builder.Property(c => c.UserId);

        // The at-rest hash is hex of SHA-256 → 64 chars, same as sessions (S02) and for the same
        // reason: an exfiltrated `carts` table cannot be replayed. Unique because two carts cannot
        // share a token.
        // GOTCHA: NULLs are DISTINCT in a unique index on both Postgres and SQLite, so every owned
        // cart (TokenHash = NULL) coexists happily under this index. That is standard SQL and it is
        // also exactly the kind of thing worth checking rather than assuming — the rule is not the
        // same everywhere (SQL Server's filtered indexes exist precisely because its unique index
        // treats NULLs as equal).
        builder.Property(c => c.TokenHash).HasMaxLength(64);
        builder.HasIndex(c => c.TokenHash).IsUnique();

        // WHY this index is NOT unique, though "one live cart per customer" is a real rule. The rule
        // is one live cart — and this column will hold retired carts too the moment S07's checkout
        // turns a cart into an order and starts a fresh one. A unique index would encode "one cart
        // EVER", which is a different and false claim, and S07 would have to drop it. The honest
        // expression is a filtered unique index (WHERE RetiredAtUtc IS NULL); EF can only spell that
        // as provider-specific raw SQL, and Postgres and SQLite quote identifiers differently, so it
        // would be two strings neither of which the model checks. Plain index for the lookup, and the
        // rule stays where it is enforced: FindActiveByUserAsync only ever returns one.
        builder.HasIndex(c => c.UserId);

        // Read-only properties mapped explicitly — EF silently skips getter-only auto-properties, and
        // ModelMappingTests fails the build if any mapped entity grows one nobody mentioned.
        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.Property(c => c.UpdatedAtUtc).IsRequired();
        builder.Property(c => c.RetiredAtUtc);

        // 📘 A REAL AGGREGATE, and the first one in Agora — every other entity here has been a lone
        // row with foreign keys and no navigations (a Product does not drag its Category around; a
        // SaleCampaign does not drag its Product). A cart's lines are different in kind: a line has no
        // meaning, no lifetime, and no reason to be loaded without its cart. So this is the one place
        // the boundary encloses more than one table, and the collection navigation is how EF is told
        // where the boundary is.
        // GOTCHA: `Lines` is an IReadOnlyList over a private `_lines` field, so EF must be told to go
        // through the FIELD — the property has no setter and EF would otherwise have nothing to
        // populate. Without this line the cart materialises with zero lines, forever, silently: no
        // exception, no warning, just an empty drawer and a very confusing afternoon.
        builder.HasMany(c => c.Lines)
            .WithOne()
            .HasForeignKey(line => line.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(Cart.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // WHY Cascade here and Restrict everywhere else in this schema: a cart line genuinely cannot
        // outlive its cart — it is part of it, not a neighbour — so the database deleting the lines
        // with the cart is the aggregate boundary being honest. Products get Restrict because
        // vaporising the pricing history of everything a product ever sold is nobody's intent.
        // (Carts are retired, not deleted, so today this cascade only ever runs for the reaper.)

        // A cart's owner must be a real user; the FK is the boundary, with no navigation in either
        // direction. Cascade because a deleted user's cart is not a thing anyone wants to keep — it
        // is the same call sessions made in S02.
        builder.HasOne<Domain.Access.User>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CartLineConfiguration : IEntityTypeConfiguration<CartLine>
{
    public void Configure(EntityTypeBuilder<CartLine> builder)
    {
        builder.ToTable("cart_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.CartId).IsRequired();
        builder.Property(l => l.ProductId).IsRequired();
        builder.Property(l => l.Quantity).IsRequired();
        builder.Property(l => l.AddedAtUtc).IsRequired();

        // Money as a complex property, flattened into this row — the same call ProductConfiguration
        // made in S04, and the same reasoning: a struct with no identity is a complex type, not an
        // owned entity. Note the currency needs its converter to be treated as a SCALAR; without one
        // EF tries to nest CurrencyCode as a complex type inside a complex type.
        builder.ComplexProperty(l => l.PriceAtAddition, price =>
        {
            price.Property(m => m.Amount)
                .HasColumnName("PriceAtAdditionAmount")
                .HasPrecision(18, 2)
                .IsRequired();

            price.Property(m => m.Currency)
                .HasColumnName("PriceAtAdditionCurrency")
                .HasConversion(currency => currency.Code, value => CurrencyCode.From(value))
                .HasMaxLength(CurrencyCode.Length)
                .IsFixedLength()
                .IsRequired();
        });

        // 📘 ONE LINE PER PRODUCT PER CART, enforced by the database and not only by the domain — and
        // this is belt-and-braces on purpose, exactly as S02 did for users.email. Cart.AddOrIncrease
        // gives the friendly behaviour (it finds the line and increments it); this index closes the
        // race that two simultaneous adds of the same new product could win, where both requests read
        // "no such line", both INSERT, and the cart ends up with two rows that both say "skillet" and
        // that every subsequent screen has to reconcile. The domain gives the answer; the index makes
        // it true.
        builder.HasIndex(l => new { l.CartId, l.ProductId }).IsUnique();

        // A line points at a real product; RESTRICT, so a product cannot be deleted out from under a
        // cart. (It never can be — S03 archives instead — which is the point: the constraint documents
        // an intent the code already keeps.)
        builder.HasOne<Domain.Catalog.Product>()
            .WithMany()
            .HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
