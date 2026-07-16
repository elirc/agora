using Agora.Domain;
using Agora.Domain.Catalog;

namespace Agora.Domain.Tests.Catalog;

public class CategoryTests
{
    private static readonly DateTimeOffset SomeInstant =
        new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_trims_name_and_assigns_identity()
    {
        var category = Category.Create("  Kitchen & Dining  ", Slug.From("kitchen-dining"), SomeInstant);

        Assert.Equal("Kitchen & Dining", category.Name);
        Assert.Equal("kitchen-dining", category.Slug.Value);
        Assert.NotEqual(Guid.Empty, category.Id);
        Assert.Equal(SomeInstant, category.CreatedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void Create_rejects_missing_name(string? name)
    {
        Assert.Throws<DomainRuleException>(() =>
            Category.Create(name, Slug.From("kitchen-dining"), SomeInstant));
    }

    [Fact]
    public void Create_rejects_names_beyond_max_length()
    {
        string tooLong = new('x', Category.MaxNameLength + 1);

        Assert.Throws<DomainRuleException>(() =>
            Category.Create(tooLong, Slug.From("kitchen-dining"), SomeInstant));
    }

    [Fact]
    public void New_category_is_born_at_version_1()
    {
        var category = Category.Create("Kitchen", Slug.From("kitchen-dining"), SomeInstant);

        Assert.Equal(1, category.Version);
    }

    [Fact]
    public void Rename_changes_the_name_and_bumps_the_version_but_not_the_slug()
    {
        var category = Category.Create("Kitchen", Slug.From("kitchen-dining"), SomeInstant);
        Slug originalSlug = category.Slug;

        category.Rename("  Kitchen & Dining  ");

        Assert.Equal("Kitchen & Dining", category.Name);
        Assert.Equal(originalSlug, category.Slug); // slug is a permalink — immutable
        Assert.Equal(2, category.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_rejects_a_blank_name(string? name)
    {
        var category = Category.Create("Kitchen", Slug.From("kitchen-dining"), SomeInstant);

        Assert.Throws<DomainRuleException>(() => category.Rename(name));
    }
}
