using System.Net;
using System.Net.Http.Json;
using Agora.Api.Tests.Infrastructure;
using Agora.Contracts.V1;

namespace Agora.Api.Tests;

public class ProductEndpointsTests : IClassFixture<AgoraApiFactory>
{
    private readonly HttpClient _client;

    public ProductEndpointsTests(AgoraApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Lists_seeded_products()
    {
        var products = await _client.GetFromJsonAsync<List<ProductDto>>("/api/v1/products");

        Assert.NotNull(products);
        Assert.Equal(12, products.Count);
        Assert.Contains(products, p => p.Sku == "AGA-KIT-001");
    }

    [Fact]
    public async Task Filters_products_by_category_slug()
    {
        var products = await _client.GetFromJsonAsync<List<ProductDto>>(
            "/api/v1/products?category=kitchen-dining");

        Assert.NotNull(products);
        Assert.Equal(3, products.Count);
        Assert.All(products, p => Assert.StartsWith("AGA-KIT-", p.Sku));
    }

    [Fact]
    public async Task Gets_a_product_by_slug()
    {
        var product = await _client.GetFromJsonAsync<ProductDto>(
            "/api/v1/products/cast-iron-skillet-10-inch");

        Assert.Equal("Cast Iron Skillet, 10-inch", product!.Name);
        Assert.Equal("USD", product.PriceCurrency);
        Assert.Equal(34.50m, product.PriceAmount);
    }

    [Fact]
    public async Task Unknown_product_is_problem_json_404()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/v1/products/no-such-product");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        ProblemShape? problem = await response.Content.ReadFromJsonAsync<ProblemShape>();
        Assert.Equal("https://agora.example/problems/not-found", problem!.Type);
    }

    [Fact]
    public async Task Out_of_range_limit_is_400()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/v1/products?limit=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Pagination_walks_the_collection_via_the_header_cursor()
    {
        var seen = new List<string>();
        string url = "/api/v1/products?limit=5";

        // LEARN: this loop IS the client contract — follow X-Next-Cursor until it stops
        // appearing. If the header never stops, or items repeat, pagination is broken; this
        // test would hang or the distinct-check would fail.
        for (int hop = 0; hop < 25; hop++)
        {
            HttpResponseMessage page = await _client.GetAsync(url);
            page.EnsureSuccessStatusCode();

            List<ProductDto> items =
                (await page.Content.ReadFromJsonAsync<List<ProductDto>>())!;
            seen.AddRange(items.Select(i => i.Sku));

            if (!page.Headers.TryGetValues("X-Next-Cursor", out var cursors))
            {
                break;
            }

            url = $"/api/v1/products?limit=5&cursor={Uri.EscapeDataString(cursors.Single())}";
        }

        Assert.Equal(12, seen.Count);
        Assert.Equal(seen.Count, seen.Distinct().Count());
    }
}
