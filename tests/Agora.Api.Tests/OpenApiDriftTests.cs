using Agora.Api.Tests.Infrastructure;

namespace Agora.Api.Tests;

/// <summary>
/// The committed OpenAPI document must match what the API actually serves.
/// </summary>
// 🔗 this is a CONTRACT test, and it is one half of "one contract, two consumers" (ADR 0002):
// the spec in docs/openapi/ is what integrators read AND what the React app generates its types
// from (web/src/api/schema.d.ts). If code and document can drift apart, the document is
// marketing. CI runs this on every PR, so "update the spec" cannot be forgotten — only done.
//
// To bless an intentional change:  AGORA_UPDATE_OPENAPI=1 dotnet test
// (the test then rewrites docs/openapi/agora-v1.json and passes; commit the diff — and
// regenerate the frontend types with `npm run api-types`).
public class OpenApiDriftTests : IClassFixture<AgoraApiFactory>
{
    private readonly HttpClient _client;

    public OpenApiDriftTests(AgoraApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Served_spec_matches_the_committed_document()
    {
        string served = Normalize(await _client.GetStringAsync("/openapi/v1.json"));

        string specPath = Path.Combine(RepoRoot(), "docs", "openapi", "agora-v1.json");

        if (Environment.GetEnvironmentVariable("AGORA_UPDATE_OPENAPI") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(specPath)!);
            await File.WriteAllTextAsync(specPath, served + Environment.NewLine);
            return;
        }

        Assert.True(File.Exists(specPath),
            $"Committed OpenAPI document missing at {specPath}. " +
            "Run with AGORA_UPDATE_OPENAPI=1 to create it.");

        string committed = Normalize(await File.ReadAllTextAsync(specPath));

        // WHY: a plain string equality gives an unreadable failure for a 1-line drift, so point
        // at the first differing line instead.
        if (committed != served)
        {
            string[] a = committed.Split('\n');
            string[] b = served.Split('\n');
            int line = Enumerable.Range(0, Math.Min(a.Length, b.Length))
                .FirstOrDefault(i => a[i] != b[i], Math.Min(a.Length, b.Length));

            Assert.Fail(
                $"OpenAPI drift at line {line + 1}:\n" +
                $"  committed: {(line < a.Length ? a[line] : "<eof>")}\n" +
                $"  served:    {(line < b.Length ? b[line] : "<eof>")}\n" +
                "If this change is intentional, bless it: AGORA_UPDATE_OPENAPI=1 dotnet test, " +
                "then commit docs/openapi/agora-v1.json and regenerate web/src/api/schema.d.ts.");
        }
    }

    private static string Normalize(string json) =>
        json.Replace("\r\n", "\n").TrimEnd('\n', ' ');

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Agora.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
