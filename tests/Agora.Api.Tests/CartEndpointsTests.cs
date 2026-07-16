using System.Net;
using System.Net.Http.Json;
using Agora.Api.Carts;
using Agora.Api.Tests.Infrastructure;
using Agora.Contracts.V1;
using Agora.Infrastructure.Persistence;

namespace Agora.Api.Tests;

/// <summary>
/// The cart over real HTTP: two cookies, an anonymous shopper who becomes a customer, and the
/// refusals.
/// </summary>
public class CartEndpointsTests : IClassFixture<AgoraApiFactory>, IAsyncLifetime
{
    private readonly AgoraApiFactory _factory;
    private Guid _skilletId;
    private Guid _boardId;

    public CartEndpointsTests(AgoraApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        List<ProductDto> products =
            (await _factory.NewClient().GetFromJsonAsync<List<ProductDto>>("/api/v1/products?limit=50"))!;

        _skilletId = products.Single(p => p.Sku == "AGA-KIT-002").Id;
        _boardId = products.Single(p => p.Sku == "AGA-KIT-003").Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── The anonymous shopper ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_shopper_who_has_never_been_here_has_an_empty_cart_and_gets_no_cookie()
    {
        HttpResponseMessage response = await _factory.NewClient().GetAsync("/api/v1/cart");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CartDto cart = (await response.Content.ReadFromJsonAsync<CartDto>())!;
        Assert.Empty(cart.Lines);
        Assert.Equal(0m, cart.SubtotalAmount);
        Assert.Equal("USD", cart.SubtotalCurrency);

        // ⚠️ A READ MINTS NOTHING. A GET that Set-Cookies is a GET with a side effect, and the side
        // effect is a row: every crawler and link preview that ever touched the storefront would
        // leave a cart behind.
        Assert.Null(CartHelpers.CartTokenFrom(response));
    }

    // 📘 THE SPRINT'S CENTRAL CLAIM, PINNED: `agora_cart` is a SEPARATE, UNPRIVILEGED cookie. This
    // response mints one and does NOT mint a session — the shopper is still nobody. If these two ever
    // become one cookie, this test is where it shows.
    [Fact]
    public async Task The_first_write_mints_an_unprivileged_cart_cookie_that_is_not_a_session()
    {
        HttpResponseMessage response = await _factory.NewClient()
            .PostAsJsonAsync("/api/v1/cart/lines", new AddCartLineRequest(_skilletId, 2));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string cookie = AuthHelpers.SetCookieFor(response, CartCookie.Name)!;
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);

        // Two cookies for a reason: adding to a cart does not sign you in.
        Assert.Null(AuthHelpers.SetCookieFor(response, Agora.Api.Auth.SessionCookie.Name));
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _factory.AsShopper(cartToken: CartHelpers.CartTokenFrom(response))
                .GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task A_cart_survives_across_requests_that_present_the_token()
    {
        HttpResponseMessage first = await _factory.NewClient()
            .PostAsJsonAsync("/api/v1/cart/lines", new AddCartLineRequest(_skilletId, 2));
        string token = CartHelpers.CartTokenFrom(first)!;

        HttpClient shopper = _factory.AsShopper(cartToken: token);
        await shopper.PostAsJsonAsync("/api/v1/cart/lines", new AddCartLineRequest(_boardId, 1));

        CartDto cart = (await shopper.GetFromJsonAsync<CartDto>("/api/v1/cart"))!;
        Assert.Equal(2, cart.Lines.Count);
        Assert.Equal(2, cart.Lines.Single(line => line.ProductId == _skilletId).Quantity);
    }

    [Fact]
    public async Task Adding_the_same_product_again_increases_the_line_and_answers_200_not_201()
    {
        HttpResponseMessage first = await _factory.NewClient()
            .PostAsJsonAsync("/api/v1/cart/lines", new AddCartLineRequest(_skilletId, 2));
        HttpClient shopper = _factory.AsShopper(cartToken: CartHelpers.CartTokenFrom(first));

        HttpResponseMessage second = await shopper
            .PostAsJsonAsync("/api/v1/cart/lines", new AddCartLineRequest(_skilletId, 3));

        // 201 would be a lie: nothing was created. See CartEndpoints.
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        CartDto cart = (await second.Content.ReadFromJsonAsync<CartDto>())!;
        Assert.Equal(5, Assert.Single(cart.Lines).Quantity);
    }

    // ── Money on the wire ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Every_amount_arrives_resolved_so_the_client_never_multiplies_anything()
    {
        HttpResponseMessage response = await _factory.NewClient()
            .PostAsJsonAsync("/api/v1/cart/lines", new AddCartLineRequest(_boardId, 3));

        CartDto cart = (await response.Content.ReadFromJsonAsync<CartDto>())!;
        CartLineDto line = Assert.Single(cart.Lines);

        Assert.Equal("Bamboo Cutting Board", line.Name);
        Assert.Equal(3, line.Quantity);
        Assert.Equal("USD", line.Currency);
        // unit × quantity and the subtotal are both the SERVER's arithmetic, in decimal.
        Assert.Equal(line.UnitPriceAmount * 3, line.LineTotalAmount);
        Assert.Equal(line.LineTotalAmount, cart.SubtotalAmount);
        Assert.False(line.PriceChanged);
    }

    // ── Merge on login: the flagship ─────────────────────────────────────────────────────────

    // 📘 THE SPRINT IN ONE TEST. A shopper fills a cart with no account, signs in to an account that
    // already has one, and everything is there — with the collision resolved by the policy (2 and 3 →
    // 3, not 5).
    [Fact]
    public async Task Signing_in_merges_the_anonymous_cart_into_the_customers_own()
    {
        string email = $"merge-{Guid.NewGuid():N}@example.com";
        const string password = "a-long-enough-password";

        // Ada, signed in on her laptop, already has 3 skillets and a board.
        string session = await _factory.RegisterCustomerAsync(email, password);
        HttpClient laptop = _factory.AsShopper(session);
        await laptop.PostAsJsonAsync("/api/v1/cart/lines", new AddCartLineRequest(_skilletId, 3));
        await laptop.PostAsJsonAsync("/api/v1/cart/lines", new AddCartLineRequest(_boardId, 1));

        // On her phone, signed out, she picks up 2 skillets.
        HttpResponseMessage minted = await _factory.NewClient()
            .PostAsJsonAsync("/api/v1/cart/lines", new AddCartLineRequest(_skilletId, 2));
        string cartToken = CartHelpers.CartTokenFrom(minted)!;

        // Then signs in on the phone, presenting the cart cookie she is holding.
        HttpResponseMessage login = await _factory.AsShopper(cartToken: cartToken)
            .PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        login.EnsureSuccessStatusCode();

        // ⚠️ and the anonymous cart cookie is CLEARED — after this response there is no state in
        // which keeping it is right: whatever it held is hers now.
        Assert.True(CartHelpers.CartCookieCleared(login));

        CartDto cart = (await _factory.AsShopper(AuthHelpers.SessionTokenFrom(login))
            .GetFromJsonAsync<CartDto>("/api/v1/cart"))!;

        Assert.Equal(2, cart.Lines.Count);
        // The collision: 3 in the account, 2 on the phone → 3. Not 5. (ADR-0007 §Decision 3.)
        Assert.Equal(3, cart.Lines.Single(line => line.ProductId == _skilletId).Quantity);
        Assert.Equal(1, cart.Lines.Single(line => line.ProductId == _boardId).Quantity);
    }

    // ⚠️ REGISTER MERGES TOO — "create an account to check out" is the oldest funnel in retail, so
    // this is the path more likely to be carrying a full cart than login is.
    [Fact]
    public async Task Registering_brings_the_cart_with_you()
    {
        HttpResponseMessage minted = await _factory.NewClient()
            .PostAsJsonAsync("/api/v1/cart/lines", new AddCartLineRequest(_skilletId, 2));
        string cartToken = CartHelpers.CartTokenFrom(minted)!;

        HttpResponseMessage registered = await _factory.AsShopper(cartToken: cartToken)
            .PostAsJsonAsync(
                "/api/v1/auth/register",
                new RegisterRequest($"reg-{Guid.NewGuid():N}@example.com", "a-long-enough-password"));
        registered.EnsureSuccessStatusCode();

        CartDto cart = (await _factory.AsShopper(AuthHelpers.SessionTokenFrom(registered))
            .GetFromJsonAsync<CartDto>("/api/v1/cart"))!;

        Assert.Equal(2, Assert.Single(cart.Lines).Quantity);
    }

    // 🔍 THE MERGED CART IS CONSUMED. Presenting the old anonymous token afterwards finds nothing —
    // not the merged cart, and not the old one. That is the repository's retired filter doing its
    // job, and it is where the merge's idempotence comes from.
    [Fact]
    public async Task The_anonymous_cart_is_retired_and_its_token_finds_nothing_afterwards()
    {
        HttpResponseMessage minted = await _factory.NewClient()
            .PostAsJsonAsync("/api/v1/cart/lines", new AddCartLineRequest(_skilletId, 2));
        string cartToken = CartHelpers.CartTokenFrom(minted)!;

        await _factory.AsShopper(cartToken: cartToken).PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest($"retire-{Guid.NewGuid():N}@example.com", "a-long-enough-password"));

        CartDto stale = (await _factory.AsShopper(cartToken: cartToken)
            .GetFromJsonAsync<CartDto>("/api/v1/cart"))!;

        Assert.Empty(stale.Lines);
    }

    // 📘 A SIGNED-IN SHOPPER'S CART IS FOUND BY THEIR USER ID, and the cart cookie is not consulted.
    // If that ever inverts, a stale or stolen agora_cart hands a signed-in customer somebody else's
    // shopping — and every test with only one browser in it still passes.
    [Fact]
    public async Task A_signed_in_shopper_presenting_a_strangers_cart_token_gets_their_own_cart()
    {
        HttpResponseMessage strangers = await _factory.NewClient()
            .PostAsJsonAsync("/api/v1/cart/lines", new AddCartLineRequest(_boardId, 7));
        string strangersToken = CartHelpers.CartTokenFrom(strangers)!;

        string session = await _factory.RegisterCustomerAsync($"own-{Guid.NewGuid():N}@example.com");
        await _factory.AsShopper(session)
            .PostAsJsonAsync("/api/v1/cart/lines", new AddCartLineRequest(_skilletId, 1));

        CartDto cart = (await _factory.AsShopper(session, strangersToken)
            .GetFromJsonAsync<CartDto>("/api/v1/cart"))!;

        CartLineDto line = Assert.Single(cart.Lines);
        Assert.Equal(_skilletId, line.ProductId);
    }

    // ── Editing ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_quantity_can_be_set_and_the_response_is_the_whole_repriced_cart()
    {
        (HttpClient shopper, CartDto cart) = await CartWith(_skilletId, 1);

        HttpResponseMessage response = await shopper.PatchAsJsonAsync(
            $"/api/v1/cart/lines/{cart.Lines[0].Id}", new UpdateCartLineQuantityRequest(4));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CartDto updated = (await response.Content.ReadFromJsonAsync<CartDto>())!;
        Assert.Equal(4, updated.Lines[0].Quantity);
        Assert.Equal(updated.Lines[0].LineTotalAmount, updated.SubtotalAmount);
    }

    [Fact]
    public async Task A_line_can_be_removed_and_the_response_carries_the_new_subtotal()
    {
        (HttpClient shopper, CartDto cart) = await CartWith(_skilletId, 1);
        await shopper.PostAsJsonAsync("/api/v1/cart/lines", new AddCartLineRequest(_boardId, 1));

        HttpResponseMessage response = await shopper.DeleteAsync($"/api/v1/cart/lines/{cart.Lines[0].Id}");

        // ⚠️ 200 with the cart, not 204: removing a line changes the subtotal, and a 204 would leave
        // the drawer holding a number that is now false.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CartDto updated = (await response.Content.ReadFromJsonAsync<CartDto>())!;
        Assert.Equal(_boardId, Assert.Single(updated.Lines).ProductId);
    }

    [Fact]
    public async Task The_cart_can_be_cleared_and_clearing_an_empty_one_is_still_a_success()
    {
        (HttpClient shopper, _) = await CartWith(_skilletId, 2);

        HttpResponseMessage first = await shopper.DeleteAsync("/api/v1/cart");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Empty((await first.Content.ReadFromJsonAsync<CartDto>())!.Lines);

        // DELETE is idempotent — that is what the verb MEANS, and a double-click relies on it.
        HttpResponseMessage second = await shopper.DeleteAsync("/api/v1/cart");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    // ── The refusals ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_archived_product_cannot_be_added_and_reads_as_404()
    {
        string admin = await _factory.LoginAsync(AgoraSeeder.StoreAdminEmail, AgoraSeeder.StoreAdminPassword);
        AdminProductDto product = await CreateAndArchiveProductAsync(admin);

        HttpResponseMessage response = await _factory.NewClient()
            .PostAsJsonAsync("/api/v1/cart/lines", new AddCartLineRequest(product.Id, 1));

        // 404 and not 422: an archived product reads as not-found to a shopper everywhere else, so it
        // reads as not-found here. A 422 would confirm it exists to anyone guessing ids.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        ProblemShape problem = (await response.Content.ReadFromJsonAsync<ProblemShape>())!;
        Assert.Equal("https://agora.example/problems/not-found", problem.Type);
    }

    [Fact]
    public async Task A_product_that_does_not_exist_is_a_404()
    {
        HttpResponseMessage response = await _factory.NewClient()
            .PostAsJsonAsync("/api/v1/cart/lines", new AddCartLineRequest(Guid.NewGuid(), 1));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(100)]
    public async Task An_impossible_quantity_is_a_422(int quantity)
    {
        HttpResponseMessage response = await _factory.NewClient()
            .PostAsJsonAsync("/api/v1/cart/lines", new AddCartLineRequest(_skilletId, quantity));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        ProblemShape problem = (await response.Content.ReadFromJsonAsync<ProblemShape>())!;
        Assert.Equal("https://agora.example/problems/domain-rule-violated", problem.Type);
    }

    // ⚠️ QUANTITY 0 IS A 422, NOT A DELETE. `parseInt(input.value) || 0` is one of the most common
    // lines in front-end code, so "0" is what an EMPTY BOX sends. Being helpful here would let a
    // client bug silently delete a shopper's line.
    [Fact]
    public async Task Setting_a_quantity_to_zero_is_a_422_and_not_a_removal()
    {
        (HttpClient shopper, CartDto cart) = await CartWith(_skilletId, 2);

        HttpResponseMessage response = await shopper.PatchAsJsonAsync(
            $"/api/v1/cart/lines/{cart.Lines[0].Id}", new UpdateCartLineQuantityRequest(0));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        CartDto after = (await shopper.GetFromJsonAsync<CartDto>("/api/v1/cart"))!;
        Assert.Equal(2, Assert.Single(after.Lines).Quantity);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private async Task<(HttpClient Shopper, CartDto Cart)> CartWith(Guid productId, int quantity)
    {
        HttpResponseMessage minted = await _factory.NewClient()
            .PostAsJsonAsync("/api/v1/cart/lines", new AddCartLineRequest(productId, quantity));

        HttpClient shopper = _factory.AsShopper(cartToken: CartHelpers.CartTokenFrom(minted));
        return (shopper, (await minted.Content.ReadFromJsonAsync<CartDto>())!);
    }

    private async Task<AdminProductDto> CreateAndArchiveProductAsync(string admin)
    {
        List<CategoryDto> categories =
            (await _factory.NewClient().GetFromJsonAsync<List<CategoryDto>>("/api/v1/categories"))!;

        HttpResponseMessage created = await _factory.As(admin).PostAsJsonAsync(
            "/api/v1/admin/products",
            new CreateProductRequest(
                categories[0].Id, $"Doomed {Guid.NewGuid():N}", null,
                $"SKU-{Guid.NewGuid():N}"[..20], "Briefly available.", 9.99m, "USD"));
        created.EnsureSuccessStatusCode();
        AdminProductDto product = (await created.Content.ReadFromJsonAsync<AdminProductDto>())!;

        HttpResponseMessage archived = await _factory.As(admin).PostAsJsonAsync(
            $"/api/v1/admin/products/{product.Id}/archive", new ProductVersionRequest(product.Version));
        archived.EnsureSuccessStatusCode();

        return product;
    }
}
