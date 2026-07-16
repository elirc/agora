using Agora.Domain.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agora.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.Email)
            .HasConversion(email => email.Value, value => EmailAddress.From(value))
            .HasMaxLength(EmailAddress.MaxLength)
            .IsRequired();

        // WHY case-insensitive uniqueness is achieved by NORMALIZATION, not a collation: the
        // EmailAddress value object lowercases at the boundary, so every stored value is already
        // lowercase and a plain unique index IS case-insensitive — no provider-specific citext or
        // COLLATE NOCASE needed, and the same guarantee holds on Postgres and SQLite alike. The
        // index closes the race two concurrent signups could win; RegisterCustomer's check is just
        // the friendly error. Belt and suspenders, deliberately.
        builder.HasIndex(u => u.Email).IsUnique();

        builder.Property(u => u.PasswordHash).HasMaxLength(256).IsRequired();

        // Enum stored as its NAME, not its number: a "StoreAdmin" row stays readable in the
        // database and reordering the enum can't silently reassign everyone's role.
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(32).IsRequired();

        // GOTCHA: EF Core does NOT map getter-only auto-properties by convention — it silently
        // skips them and the column simply never exists. CreatedAtUtc is read-only, so it is
        // mapped EXPLICITLY here; ModelMappingTests fails the build if any mapped entity grows a
        // public property EF quietly ignores.
        builder.Property(u => u.CreatedAtUtc).IsRequired();
    }
}

internal sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("sessions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.UserId).IsRequired();

        // The at-rest hash is hex of SHA-256 → 64 chars. Unique because two sessions cannot share
        // a token, and the index is the auth middleware's one lookup per request.
        builder.Property(s => s.TokenHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(s => s.TokenHash).IsUnique();

        builder.HasIndex(s => s.UserId);

        // Read-only properties: mapped explicitly (see the GOTCHA above). Without these lines the
        // session's expiry would never reach the database and every token would read back as
        // expired-at-year-one — silent schema loss the ModelMappingTests fitness test guards.
        builder.Property(s => s.CreatedAtUtc).IsRequired();
        builder.Property(s => s.ExpiresAtUtc).IsRequired();
        builder.Property(s => s.RevokedAtUtc);

        // WHY a foreign key WITHOUT a navigation property: a Session does not drag a whole User
        // graph around — the aggregate boundary is the raw UserId — but the database still
        // enforces that the id points at a real user, and deleting a user takes their sessions.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
