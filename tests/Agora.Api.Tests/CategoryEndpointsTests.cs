using System.Net;
using System.Net.Http.Json;
using Agora.Api.Tests.Infrastructure;
using Agora.Contracts.V1;

namespace Agora.Api.Tests;

public class CategoryEndpointsTests : IClassFixture<AgoraApiFactory>
{
    private readonly HttpClient _client;

    public CategoryEndpointsTests(AgoraApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Lists_seeded_categories()
    {
        var categories =
            await _client.GetFromJsonAsync<List<CategoryDto>>("/api/v1/categories");

        Assert.NotNull(categories);
        Assert.Contains(categories, c => c.Slug == "kitchen-dining");
        Assert.Contains(categories, c => c.Slug == "bath-wellness");
    }

    [Fact]
    public async Task Gets_a_category_by_slug_case_insensitively()
    {
        var category = await _client.GetFromJsonAsync<CategoryDto>("/api/v1/categories/Home-Office");

        Assert.Equal("Home Office", category!.Name);
    }

    [Fact]
    public async Task Unknown_category_is_problem_json_404()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/v1/categories/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        ProblemShape? problem = await response.Content.ReadFromJsonAsync<ProblemShape>();
        Assert.Equal("https://agora.example/problems/not-found", problem!.Type);
    }

    [Fact]
    public async Task Malformed_slug_is_a_422_domain_rule_problem()
    {
        // LEARN: /categories/Not A Slug isn't a missing resource — it's a request the business
        // vocabulary rejects. Contract: 422 + domain-rule-violated.
        HttpResponseMessage response = await _client.GetAsync("/api/v1/categories/Not%20A%20Slug");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        ProblemShape? problem = await response.Content.ReadFromJsonAsync<ProblemShape>();
        Assert.Equal("https://agora.example/problems/domain-rule-violated", problem!.Type);
    }
}
