using System.Net;
using System.Net.Http.Json;
using Agora.Api.Tests.Infrastructure;
using Agora.Contracts.V1;
using Agora.Domain.Inventory;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Agora.Api.Tests;

public class InventoryEndpointsTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private readonly AgoraApiFactory _factory = factory;

    [Fact]
    public async Task Stock_is_reported_for_a_seeded_product()
    {
        HttpClient client = _factory.As(null);

        ProductDto product = await FirstProductAsync(client);

        StockDto? stock = await client.GetFromJsonAsync<StockDto>($"/api/v1/products/{product.Id}/stock");

        Assert.NotNull(stock);
        Assert.True(stock.Available >= 0);
        Assert.Equal(stock.Available > 0, stock.InStock);
    }

    [Fact]
    public async Task Reserving_holds_stock_and_reduces_what_is_available()
    {
        HttpClient client = _factory.As(null);
        ProductDto product = await FirstProductAsync(client);

        StockDto before = (await client.GetFromJsonAsync<StockDto>($"/api/v1/products/{product.Id}/stock"))!;

        HttpResponseMessage response = await client
            .PostAsJsonAsync("/api/v1/cart/reservations", new ReserveStockRequest(product.Id, 1));

        response.EnsureSuccessStatusCode();
        ReservationResultDto result = (await response.Content.ReadFromJsonAsync<ReservationResultDto>())!;

        Assert.True(result.Success);
        Assert.NotNull(result.Reservation);
        Assert.Equal(1, result.Reservation.Quantity);

        // The claim is held BY THE CART, and the cart token came back on this very response — so the
        // shopper who reserved must present it to see the reduction. A fresh anonymous client is a
        // different shopper and sees the stock as still spoken for by somebody else, which is the
        // same number for a different reason.
        HttpClient sameShopper = _factory.AsShopper(null, CartHelpers.CartTokenFrom(response));
        StockDto after = (await sameShopper.GetFromJsonAsync<StockDto>($"/api/v1/products/{product.Id}/stock"))!;

        Assert.Equal(before.Available - 1, after.Available);
    }

    [Fact]
    public async Task Asking_for_more_than_exists_is_a_200_with_the_number_that_is_left()
    {
        HttpClient client = _factory.As(null);
        ProductDto product = await FirstProductAsync(client);

        StockDto stock = (await client.GetFromJsonAsync<StockDto>($"/api/v1/products/{product.Id}/stock"))!;

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/cart/reservations",
            new ReserveStockRequest(product.Id, stock.Available + 5));

        // 🔍 NOT a 4xx. The request was perfectly well formed and the answer is simply no — "you
        // cannot have nine of these" is Tuesday, not an error. And the UI needs the NUMBER: "only 3
        // left" is a sentence a shopper can act on, whereas a 409 with the count buried in a `detail`
        // string is a number somebody will eventually parse out of prose.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        ReservationResultDto result = (await response.Content.ReadFromJsonAsync<ReservationResultDto>())!;
        Assert.False(result.Success);
        Assert.Null(result.Reservation);
        Assert.Equal(stock.Available, result.Available);
    }

    [Fact]
    public async Task Reserving_stock_for_a_product_that_does_not_exist_is_a_404()
    {
        HttpResponseMessage response = await _factory.As(null)
            .PostAsJsonAsync("/api/v1/cart/reservations", new ReserveStockRequest(Guid.NewGuid(), 1));

        // The other kind of "no": you asked about nothing. Two different nos, two different shapes.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Releasing_gives_the_stock_back()
    {
        HttpClient client = _factory.As(null);
        ProductDto product = await FirstProductAsync(client);

        StockDto before = (await client.GetFromJsonAsync<StockDto>($"/api/v1/products/{product.Id}/stock"))!;

        HttpResponseMessage reserved = await client
            .PostAsJsonAsync("/api/v1/cart/reservations", new ReserveStockRequest(product.Id, 1));
        reserved.EnsureSuccessStatusCode();

        HttpClient shopper = _factory.AsShopper(null, CartHelpers.CartTokenFrom(reserved));

        Assert.Equal(HttpStatusCode.NoContent, (await shopper.DeleteAsync("/api/v1/cart/reservations")).StatusCode);

        StockDto after = (await shopper.GetFromJsonAsync<StockDto>($"/api/v1/products/{product.Id}/stock"))!;
        Assert.Equal(before.Available, after.Available);
    }

    // ⚠️ THE TEST THIS SPRINT EXISTS FOR — and it does not test an `if`.
    //
    // Two shoppers, one skillet. The domain's CanTake check cannot stop this: at the instant each
    // request read availability, one skillet WAS available, and both reads were correct. That is what
    // a check-then-act race IS, and no amount of re-checking closes it — a narrower window is still a
    // window. What stops it is StockLevel's concurrency token.
    //
    // 🔍 WHY THIS IS TWO DbContexts AND NOT TWO HTTP REQUESTS ON THREADS, which is what I tried first
    // and is the version you would expect to see. The whole suite runs on ONE in-memory SQLite
    // connection (AgoraApiFactory holds it open so the database survives between requests). A single
    // SqliteConnection is not safe for concurrent use, so firing two real requests at once does not
    // reproduce the race — it reproduces a 500 from the test harness, which proves nothing about
    // Agora and would rot into a flaky test somebody eventually deletes.
    //
    // So the interleaving is written out explicitly instead: both readers load version N, one commits,
    // the other tries. That is EXACTLY the sequence two racing requests produce, minus the threads —
    // deterministic, and it fails for the reason we care about rather than for a reason we do not.
    // This is the same honesty the DbContext already owes about SQLite (see ADR-0001's consequences):
    // the test provider is not the production one, and where that matters, say so.
    //
    // The proof that the token is what matters: delete `.IsConcurrencyToken()` from
    // StockLevelConfiguration and run this. Nothing else changes — the `if` is still there, the domain
    // is still correct, every unit test still passes — and the second write silently wins.
    [Fact]
    public async Task A_stale_writer_loses_the_last_unit_rather_than_overselling_it()
    {
        ProductDto product = await FirstProductAsync(_factory.As(null));
        await _factory.SetOnHandAsync(product.Id, 1);

        using IServiceScope firstScope = _factory.Services.CreateScope();
        using IServiceScope secondScope = _factory.Services.CreateScope();

        var firstDb = firstScope.ServiceProvider.GetRequiredService<AgoraDbContext>();
        var secondDb = secondScope.ServiceProvider.GetRequiredService<AgoraDbContext>();

        // Both shoppers read the shelf. Both see version N and one skillet. Both are RIGHT.
        StockLevel mine = await firstDb.StockLevels.SingleAsync(s => s.ProductId == product.Id);
        StockLevel theirs = await secondDb.StockLevels.SingleAsync(s => s.ProductId == product.Id);

        Assert.Equal(mine.Version, theirs.Version);

        // The first shopper commits: a claim, and the version bump that announces the shelf moved.
        firstDb.Reservations.Add(
            Reservation.Take(product.Id, Guid.NewGuid(), 1, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(15)));
        mine.ClaimedAgainst(DateTimeOffset.UtcNow);
        await firstDb.SaveChangesAsync();

        // The second shopper acts on what they read a moment ago, which is no longer true. Their
        // UPDATE carries `WHERE Version = @N`, matches zero rows, and EF says so.
        secondDb.Reservations.Add(
            Reservation.Take(product.Id, Guid.NewGuid(), 1, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(15)));
        theirs.ClaimedAgainst(DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondDb.SaveChangesAsync());

        // And the shelf tells the truth afterwards: one claim, nothing available. Not two claims on
        // one skillet, discovered at the packing bench.
        StockDto left = (await _factory.As(null)
            .GetFromJsonAsync<StockDto>($"/api/v1/products/{product.Id}/stock"))!;

        Assert.Equal(0, left.Available);
    }

    // The sequential version of the same story, through HTTP, which is what a shopper actually meets:
    // somebody else got there first, and you are told so honestly rather than sold something that
    // does not exist.
    [Fact]
    public async Task The_second_shopper_to_ask_for_the_last_unit_is_told_it_is_gone()
    {
        ProductDto product = await FirstProductAsync(_factory.As(null));
        await _factory.SetOnHandAsync(product.Id, 1);

        HttpResponseMessage won = await _factory.As(null)
            .PostAsJsonAsync("/api/v1/cart/reservations", new ReserveStockRequest(product.Id, 1));

        ReservationResultDto winner = (await won.Content.ReadFromJsonAsync<ReservationResultDto>())!;
        Assert.True(winner.Success);

        HttpResponseMessage lost = await _factory.As(null)
            .PostAsJsonAsync("/api/v1/cart/reservations", new ReserveStockRequest(product.Id, 1));

        ReservationResultDto loser = (await lost.Content.ReadFromJsonAsync<ReservationResultDto>())!;
        Assert.False(loser.Success);
        Assert.Equal(0, loser.Available);
    }

    private static async Task<ProductDto> FirstProductAsync(HttpClient client)
    {
        List<ProductDto>? products = await client.GetFromJsonAsync<List<ProductDto>>("/api/v1/products?limit=1");

        return products![0];
    }
}
