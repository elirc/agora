using System.Reflection;
using Agora.Api.Tests.Infrastructure;
using Agora.Domain.Catalog;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Agora.Api.Tests;

/// <summary>
/// Fitness function: every public property on a mapped entity must be mapped (a column, a complex
/// property, or a navigation). EF Core SILENTLY skips getter-only auto-properties; this test turns
/// that silence into a red build.
/// </summary>
// 📘 EF materializes entities through their private constructor and then sets each MAPPED
// property. A read-only auto-property that no configuration mentions is simply never written —
// the compiler is happy, the schema is missing a column, and only a behavior test would ever
// notice (e.g. a Product whose CreatedAtUtc reads back as year one). This test makes the MODEL
// itself the thing under test. Fitness functions grow from incidents — that's the pattern.
//
// ⚠️ AND S04 IS ONE OF THOSE INCIDENTS. Until this sprint the test asked the model for properties
// and navigations, which was a complete list of the ways a thing could be mapped — until Money and
// DateRange arrived as COMPLEX PROPERTIES, a third way that neither call returns. It would have
// reported `Product.Price` and `SaleCampaign.Window` as unmapped: a FALSE ALARM, on the two
// best-mapped properties in the sprint. Notice which direction that failure pushes. A fitness
// function that cries wolf gets an allowlist entry from the next person in a hurry — and then
// "Product.Price" is on the allowlist and the guard is gone from the property most worth guarding.
// Teaching the test about complex types is the fix; silencing it would have been the bug. When a
// fitness function fails, work out whether it is RIGHT before you make it quiet.
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
    //
    // 🔍 S04 adds NOTHING to this list, and that is the number to notice. The sprint added a Money, a
    // DateRange, a Discount and a whole entity, and every public property of every one of them is
    // mapped for real. Value objects cost this fitness function no coverage at all — they only needed
    // it to learn a third way of being mapped.
    private static readonly HashSet<string> DeliberatelyUnmapped = ["Product.IsArchived"];

    [Fact]
    public void Every_public_property_of_every_mapped_entity_is_mapped()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgoraDbContext>();

        var unmapped = new List<string>();

        foreach (var entityType in db.Model.GetEntityTypes())
        {
            // The three ways a public property can legitimately be mapped: a scalar column, a complex
            // type flattened into this table (Money, DateRange, Discount), or a navigation.
            var mapped = entityType.GetProperties().Select(p => p.Name)
                .Concat(entityType.GetComplexProperties().Select(c => c.Name))
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

    // 🔍 THE "THE SCHEMA DID NOT MOVE" CLAIM, PINNED. Product.cs, ProductConfiguration.cs and
    // ADR-0006 all assert loudly that promoting the price to a Money value object cost the database
    // exactly nothing — same two columns, same names, no migration on this table. That is a claim, and
    // a claim in three comments is worth less than one test. If someone later drops the
    // HasColumnName calls, EF's conventions quietly rename these to Price_Amount / Price_Currency:
    // the model is still valid, the entity test above is still green, and the next deploy hits a
    // production table whose columns no longer exist. This is the test that goes red instead.
    [Fact]
    public void Money_maps_to_the_two_price_columns_the_catalog_has_always_had()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgoraDbContext>();

        var product = db.Model.FindEntityType(typeof(Product))!;
        var price = product.GetComplexProperties().Single(complex => complex.Name == nameof(Product.Price));

        var storeObject = StoreObjectIdentifier.Table(product.GetTableName()!, product.GetSchema());

        string[] columns = [.. price.ComplexType.GetProperties()
            .Select(property => property.GetColumnName(storeObject)!)
            .Order()];

        Assert.Equal(["PriceAmount", "PriceCurrency"], columns);
    }
}
