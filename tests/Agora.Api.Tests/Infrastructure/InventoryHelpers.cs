using Agora.Domain.Inventory;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Agora.Api.Tests.Infrastructure;

/// <summary>Arranging a shelf, for tests that need to know exactly what is on it.</summary>
// 🔍 THIS REACHES PAST THE API AND INTO THE DATABASE, which every other test in this project
// deliberately does not do — so it deserves a defence rather than a shrug.
//
// The alternative is arranging stock the way a shopper would: buy things until one is left. That is
// slower, depends on a checkout that does not exist until S07, and — worst — makes the ARRANGEMENT
// the thing most likely to break the test. When "two shoppers race for the last unit" goes red, the
// question must be "did we oversell?", not "did the setup work?".
//
// So: the assertions still go through HTTP, exactly like every other API test. Only the fixture
// cheats, and it cheats in the direction of making the interesting failure legible.
public static class InventoryHelpers
{
    /// <summary>Put exactly <paramref name="onHand"/> on the shelf for a product.</summary>
    public static async Task SetOnHandAsync(this AgoraApiFactory factory, Guid productId, int onHand)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgoraDbContext>();

        StockLevel? stock = await db.StockLevels.SingleOrDefaultAsync(s => s.ProductId == productId);

        if (stock is null)
        {
            db.StockLevels.Add(StockLevel.Create(productId, onHand, DateTimeOffset.UtcNow));
        }
        else
        {
            stock.CountedAt(onHand, DateTimeOffset.UtcNow);
        }

        // Any claim already outstanding would keep counting against availability and make "exactly
        // one on the shelf" mean something else entirely. A stock take clears the decks.
        List<Reservation> live = await db.Reservations
            .Where(r => r.ProductId == productId && r.ReleasedAtUtc == null)
            .ToListAsync();

        foreach (Reservation reservation in live)
        {
            reservation.Release(DateTimeOffset.UtcNow);
        }

        await db.SaveChangesAsync();
    }
}
