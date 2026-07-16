using Agora.Application.Abstractions;
using Agora.Domain.Access;
using Agora.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Persistence;

/// <summary>
/// Idempotent dev/demo seed: a small home-goods catalog plus the three accounts you need to see
/// every role in action. No real brands, no assets — just enough to browse and sign in.
/// </summary>
public static class AgoraSeeder
{
    // ── Documented dev credentials (DEV/TEST ONLY — never real secrets) ──────────────────────
    // These accounts are seeded by `dotnet run --project src/Agora.Api -- seed` and by the API
    // test factory. Passwords are long enough to satisfy the register rule; they are published on
    // purpose so a learner can sign in as each role. See docs/http/auth.http and ADR-0004.
    public const string StoreAdminEmail = "admin@agora.test";
    public const string StoreAdminPassword = "admin-password-dev";
    public const string OpsEmail = "ops@agora.test";
    public const string OpsPassword = "ops-password-dev";
    public const string CustomerEmail = "shopper@agora.test";
    public const string CustomerPassword = "shopper-password-dev";

    public static async Task SeedAsync(
        AgoraDbContext db,
        IPasswordHasher hasher,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        await SeedCatalogAsync(db, clock, cancellationToken);
        await SeedIdentityAsync(db, hasher, clock, cancellationToken);
    }

    private static async Task SeedIdentityAsync(
        AgoraDbContext db,
        IPasswordHasher hasher,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        // WHY a separate guard from the catalog block: identity and catalog seed independently, so
        // re-running `seed` (or the test factory re-seeding the same in-memory DB) is harmless for
        // each. The unique email index would otherwise trip on a second insert.
        if (await db.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        DateTimeOffset now = clock.GetUtcNow();

        // LEARN: StoreAdmin and Ops are SEEDED, never self-registered — there is no public path to
        // a privileged role (RegisterCustomer always mints a Customer). The demo shopper exists so
        // the storefront has an owned cart to work with from S05.
        db.Users.AddRange(
            User.Create(EmailAddress.From(StoreAdminEmail), hasher.Hash(StoreAdminPassword), Role.StoreAdmin, now),
            User.Create(EmailAddress.From(OpsEmail), hasher.Hash(OpsPassword), Role.Ops, now),
            User.Create(EmailAddress.From(CustomerEmail), hasher.Hash(CustomerPassword), Role.Customer, now));

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedCatalogAsync(
        AgoraDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        // WHY: guard on categories so `-- migrate seed` can run twice without duplicate rows
        // (and so the Api.Tests factory can re-seed the same in-memory DB harmlessly).
        if (await db.Categories.AnyAsync(cancellationToken))
        {
            return;
        }

        // LEARN: staggered, deterministic timestamps derived from the injected clock — never a
        // bare DateTimeOffset.Now. Distinct CreatedAtUtc values give the newest-first cursor walk
        // something real to order by; the newest product listed is the last one seeded here.
        DateTimeOffset anchor = clock.GetUtcNow().AddMinutes(-30);

        var kitchen = Category.Create("Kitchen & Dining", Slug.From("kitchen-dining"), anchor.AddMinutes(-4));
        var office = Category.Create("Home Office", Slug.From("home-office"), anchor.AddMinutes(-3));
        var outdoor = Category.Create("Outdoor & Garden", Slug.From("outdoor-garden"), anchor.AddMinutes(-2));
        var bath = Category.Create("Bath & Wellness", Slug.From("bath-wellness"), anchor.AddMinutes(-1));

        db.Categories.AddRange(kitchen, office, outdoor, bath);

        (Category Category, string Name, string Slug, string Sku, string Description, decimal Price)[] specs =
        [
            (kitchen, "Stoneware Dinner Plate Set", "stoneware-dinner-plate-set", "AGA-KIT-001",
                "A four-piece set of reactive-glaze stoneware plates, each fired to a slightly different finish. Dishwasher and microwave safe.", 48.00m),
            (kitchen, "Cast Iron Skillet, 10-inch", "cast-iron-skillet-10-inch", "AGA-KIT-002",
                "Pre-seasoned cast iron that moves from stovetop to oven and only gets better with use. Pour spouts on both sides.", 34.50m),
            (kitchen, "Bamboo Cutting Board", "bamboo-cutting-board", "AGA-KIT-003",
                "End-grain bamboo board with a juice groove and hand grips. Gentle on knife edges, kind to the dishwasher-free lifestyle.", 22.00m),
            (office, "Standing Desk Converter", "standing-desk-converter", "AGA-OFF-001",
                "A spring-assisted riser that lifts your laptop and monitor to standing height in one motion. Sits on any existing desk.", 189.00m),
            (office, "Linen Task Chair Cushion", "linen-task-chair-cushion", "AGA-OFF-002",
                "A firm buckwheat-hull seat cushion in a washable linen cover. Adds a little height and a lot of comfort.", 29.99m),
            (office, "Walnut Monitor Riser", "walnut-monitor-riser", "AGA-OFF-003",
                "Solid walnut shelf that lifts your screen to eye level and hides a keyboard underneath when the day is done.", 54.00m),
            (outdoor, "Galvanized Watering Can", "galvanized-watering-can", "AGA-GDN-001",
                "A two-gallon galvanized-steel can with a long spout and a removable brass rose for a soft shower.", 26.00m),
            (outdoor, "Cedar Raised Planter Box", "cedar-raised-planter-box", "AGA-GDN-002",
                "Untreated cedar planter that weathers to a silvery grey. Ships flat; assembles with the included hardware in minutes.", 79.00m),
            (outdoor, "Hand-Forged Garden Trowel", "hand-forged-garden-trowel", "AGA-GDN-003",
                "A one-piece forged trowel with an ash handle and a leather hanging loop. Balanced enough to use all afternoon.", 18.50m),
            (bath, "Waffle-Weave Bath Towel", "waffle-weave-bath-towel", "AGA-BTH-001",
                "Quick-drying long-staple cotton in an open waffle weave. Lighter than terry, and it packs down small.", 32.00m),
            (bath, "Ceramic Soap Dispenser", "ceramic-soap-dispenser", "AGA-BTH-002",
                "A matte-glaze ceramic pump bottle that refills from bulk, so the last plastic dispenser can be the last one.", 19.00m),
            (bath, "Eucalyptus Shower Steamers", "eucalyptus-shower-steamers", "AGA-BTH-003",
                "A tin of six aromatherapy steamers. Set one on the shower floor and let the eucalyptus do the rest.", 14.00m),
        ];

        for (int i = 0; i < specs.Length; i++)
        {
            (Category category, string name, string slug, string sku, string description, decimal price) = specs[i];

            db.Products.Add(Product.Create(
                category.Id,
                name,
                Slug.From(slug),
                Sku.From(sku),
                description,
                price,
                "USD",
                anchor.AddMinutes(i + 1)));
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
