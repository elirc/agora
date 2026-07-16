using Agora.Application.Abstractions;
using Agora.Domain.Access;
using Agora.Domain.Catalog;
using Agora.Domain.Inventory;
using Agora.Domain.Pricing;
using Agora.Domain.Values;
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

        var products = new List<Product>();

        for (int i = 0; i < specs.Length; i++)
        {
            (Category category, string name, string slug, string sku, string description, decimal price) = specs[i];

            products.Add(Product.Create(
                category.Id,
                name,
                Slug.From(slug),
                Sku.From(sku),
                description,
                Money.Of(price, CurrencyCode.Usd),
                anchor.AddMinutes(i + 1)));
        }

        db.Products.AddRange(products);

        SeedSaleCampaigns(db, products, clock.GetUtcNow());
        SeedStock(db, products, clock.GetUtcNow());

        await db.SaveChangesAsync(cancellationToken);
    }

    // S06 — a shelf for every product, and deliberately not the same number on each.
    // WHY the varied counts: a seed where everything has 50 in stock demonstrates nothing. This one
    // gives you a shop with a healthy middle, two items sitting on the low-stock threshold, and one
    // genuinely sold out — so `-- seed` produces an admin low-stock view with rows in it and a
    // storefront with an out-of-stock badge, immediately, without anyone having to buy anything.
    private static void SeedStock(AgoraDbContext db, IReadOnlyList<Product> products, DateTimeOffset now)
    {
        int[] shelf = [12, 8, 3, 25, 0, 6, 2, 40, 15, 1, 9, 30];

        for (int i = 0; i < products.Count; i++)
        {
            db.StockLevels.Add(StockLevel.Create(products[i].Id, shelf[i % shelf.Length], now));
        }
    }

    // A running sale and a scheduled one, so `dotnet run -- seed` gives you a storefront with a
    // strike-through on it and an admin panel with something queued — the two states worth seeing.
    private static void SeedSaleCampaigns(
        AgoraDbContext db,
        IReadOnlyList<Product> products,
        DateTimeOffset now)
    {
        // LEARN: the windows are derived from the INJECTED clock, not from hardcoded dates — a
        // literal "2026-08-01" would be a demo that quietly stops demonstrating anything the week
        // after it was written. Relative-to-now windows keep the seed honest forever.
        Product skillet = products.Single(product => product.Sku == Sku.From("AGA-KIT-002"));
        Product planter = products.Single(product => product.Sku == Sku.From("AGA-GDN-002"));

        db.SaleCampaigns.AddRange(
            // Running right now: $34.50 skillet at 20% off → $27.60, "Save 20%".
            SaleCampaign.Schedule(
                skillet.Id,
                "Cast Iron Week",
                Discount.PercentOff(20m),
                DateRange.From(now.AddDays(-2), now.AddDays(5)),
                now.AddDays(-3)),

            // Scheduled, not started: the admin panel shows it queued; the storefront shows nothing.
            // This is the whole argument for a window in one row — nobody has to remember Friday.
            SaleCampaign.Schedule(
                planter.Id,
                "Spring Planting Weekend",
                Discount.AmountOff(15m),
                DateRange.From(now.AddDays(7), now.AddDays(10)),
                now));
    }
}
