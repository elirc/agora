using System.Reflection;
using Agora.Api.Tests.Infrastructure;
using Agora.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Agora.Api.Tests;

/// <summary>
/// Fitness function: every public property on a mapped entity must be mapped (a column or a
/// navigation). EF Core SILENTLY skips getter-only auto-properties; this test turns that
/// silence into a red build.
/// </summary>
// 📘 EF materializes entities through their private constructor and then sets each MAPPED
// property. A read-only auto-property that no configuration mentions is simply never written —
// the compiler is happy, the schema is missing a column, and only a behavior test would ever
// notice (e.g. a Product whose CreatedAtUtc reads back as year one). This test makes the MODEL
// itself the thing under test. Fitness functions grow from incidents — that's the pattern.
public class ModelMappingTests : IClassFixture<AgoraApiFactory>
{
    private readonly AgoraApiFactory _factory;

    public ModelMappingTests(AgoraApiFactory factory) => _factory = factory;

    // WHY: EF's runtime model cannot distinguish "deliberately Ignore()d" from "silently
    // forgotten" — so deliberate exceptions are declared HERE, visibly, where a reviewer sees
    // every one. A computed property belongs on this list; a timestamp never does.
    //
    // Product.IsArchived is computed from ArchivedAtUtc (its single source of truth), so it has no
    // column by design — S03 adds the first entry to what was an empty allowlist in S01.
    private static readonly HashSet<string> DeliberatelyUnmapped = ["Product.IsArchived"];

    [Fact]
    public void Every_public_property_of_every_mapped_entity_is_mapped()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgoraDbContext>();

        var unmapped = new List<string>();

        foreach (var entityType in db.Model.GetEntityTypes())
        {
            var mapped = entityType.GetProperties().Select(p => p.Name)
                .Concat(entityType.GetNavigations().Select(n => n.Name))
                .ToHashSet();

            IEnumerable<string> publicProps = entityType.ClrType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0)
                .Select(p => p.Name);

            unmapped.AddRange(
                publicProps.Where(name => !mapped.Contains(name))
                    .Select(name => $"{entityType.ClrType.Name}.{name}")
                    .Where(qualified => !DeliberatelyUnmapped.Contains(qualified)));
        }

        Assert.True(unmapped.Count == 0,
            "Public entity properties EF is silently ignoring (map them explicitly or mark the " +
            "design decision): " + string.Join(", ", unmapped));
    }
}
