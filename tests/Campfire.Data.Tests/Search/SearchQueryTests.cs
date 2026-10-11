using System.Text;
using Campfire.Data.Searching;

namespace Campfire.Data.Tests.Searching;

// SearchQuery, SearchesController#query's port.
public class SearchQueryTests
{
    // Oracle/search_word.rb generated WordRanges inside the reference image, from the Ruby
    // reference/.ruby-version pins. Each Ruby's table differs (Ruby 3.3's has no U+200C or U+200D,
    // Ruby 3.4's stops at Unicode 15.0), so this pins the version and the size.
    [Fact]
    public void Word_ranges_are_the_reference_rubys()
    {
        var ruby = File.ReadAllText(Path.Combine(TestDatabase.RepositoryRoot, "reference", ".ruby-version")).Trim();
        var generated = File.ReadAllText(Path.Combine(TestDatabase.RepositoryRoot, "src", "Campfire.Data", "Search", "WordRanges.cs"));
        Assert.Contains($"from Ruby {ruby} (Unicode", generated, StringComparison.Ordinal);
        Assert.Equal(802 * 2, WordRanges.Pairs.Length);
    }

    // Letters Unicode 16 and 17 added, which Ruby 4.0.7's /[[:word:]]/ matches.
    [Theory]
    [InlineData(0x1437A), InlineData(0x14148), InlineData(0x2EDF0)]
    public void Newer_unicode_letters_are_word_characters(int codePoint) => Assert.True(SearchQuery.IsWord(new Rune(codePoint)));

    // Probed against the reference's Ruby: alphabetic (including letter numbers and circled
    // letters), marks, decimal digits, connector punctuation and join controls.
    [Theory]
    [InlineData("a"), InlineData("Z"), InlineData("0"), InlineData("_"), InlineData("é"), InlineData("ß"), InlineData("日"), InlineData("‿")]
    [InlineData("́"), InlineData("٣"), InlineData("ǅ"), InlineData("ʰ"), InlineData("Ⅻ"), InlineData("Ⓐ"), InlineData("‍"), InlineData("𠀀")]
    public void Word_characters(string character) => Assert.True(SearchQuery.IsWord(Rune.GetRuneAt(character, 0)));

    [Theory]
    [InlineData(" "), InlineData("-"), InlineData("*"), InlineData("\""), InlineData(" "), InlineData("❤"), InlineData("€"), InlineData("½"), InlineData("²"), InlineData("😀")]
    public void Not_word_characters(string character) => Assert.False(SearchQuery.IsWord(Rune.GetRuneAt(character, 0)));

    [Fact]
    public void Every_code_point_outside_a_word_becomes_one_space()
    {
        Assert.Equal("héllo wörld_1 ２ 日本語 ‿ a b ️   é", SearchQuery.Sanitize("héllo wörld_1 ２ 日本語 ‿ a-b ️ ❤ é"));
        Assert.Equal(" quoted  OR NEAR x  ", SearchQuery.Sanitize("\"quoted\" OR NEAR(x*)"));
        Assert.Equal("a b", SearchQuery.Sanitize("a😀b"));
        Assert.Equal("a b", SearchQuery.Sanitize("a\ud800b"));
        Assert.Equal("", SearchQuery.Sanitize(""));
        Assert.Null(SearchQuery.Sanitize(null));
    }

    [Fact]
    public void Blank_queries_are_not_searched()
    {
        Assert.Null(SearchQuery.Searchable(null));
        Assert.Null(SearchQuery.Searchable(""));
        Assert.Null(SearchQuery.Searchable(" -*\"() "));
        Assert.Equal(" hello ", SearchQuery.Searchable("(hello)"));
    }
}
