using Xunit;
using SpaceCraft.Domain.Common;
using Shouldly;

namespace SpaceCraft.UnitTests;

public class SlugTests
{
    [Theory]
    [InlineData("Acme Corp", "acme-corp")]
    [InlineData("Pet Store API", "pet-store-api")]
    [InlineData("  spaced  out  ", "spaced-out")]
    [InlineData("Already-Slugged", "already-slugged")]
    [InlineData("symbols!!!everywhere???", "symbols-everywhere")]
    [InlineData("trailing---", "trailing")]
    public void From_produces_url_safe_slugs(string input, string expected) =>
        Slug.From(input).ShouldBe(expected);

    [Fact]
    public void From_strips_diacritics_rather_than_dropping_the_word() =>
        // "Café" must not become "caf".
        Slug.From("Café Ürün").ShouldBe("cafe-urun");

    [Fact]
    public void From_truncates_without_leaving_a_trailing_hyphen()
    {
        var slug = Slug.From(new string('a', 40) + " " + new string('b', 40));

        slug.Length.ShouldBeLessThanOrEqualTo(Slug.MaxLength);
        slug.ShouldNotEndWith("-");
    }

    [Fact]
    public void From_rejects_input_with_no_usable_characters() =>
        Should.Throw<ArgumentException>(() => Slug.From("!!!"));

    [Theory]
    [InlineData("acme-corp", true)]
    [InlineData("a", true)]
    [InlineData("a1-b2", true)]
    [InlineData("Acme", false)]
    [InlineData("-leading", false)]
    [InlineData("trailing-", false)]
    [InlineData("double--hyphen", false)]
    [InlineData("under_score", false)]
    [InlineData("", false)]
    public void IsValid_enforces_the_slug_grammar(string value, bool expected) =>
        Slug.IsValid(value).ShouldBe(expected);
}
