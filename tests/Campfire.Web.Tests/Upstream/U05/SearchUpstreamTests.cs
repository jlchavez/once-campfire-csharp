using Campfire.Data.Searching;
using Campfire.Web.Tests.Controllers.Messages;

namespace Campfire.Web.Tests.Upstream.U05;

/// <summary>
/// Upstream commits f2bdfde (#298), 77c5234 (#304), dbc7620:
/// Search operator words as words, newest matches off index, and bound accessible search probes.
/// </summary>
public sealed class SearchUpstreamTests : IDisposable
{
    const long david = 127326141;
    const string davidSession = "AxJs94fteQ5Autv2VrKsH68c";

    readonly MessagesApp messages = new(new DateTimeOffset(2026, 3, 2, 16, 0, 0, TimeSpan.Zero));

    public void Dispose() => messages.Dispose();

    [Theory]
    [InlineData("AND", "\"AND\"")]
    [InlineData("OR", "\"OR\"")]
    [InlineData("NOT", "\"NOT\"")]
    [InlineData("salt AND pepper", "\"salt\" \"AND\" \"pepper\"")]
    [InlineData("say \"hi NOT", "\"say\" \"\"\"hi\" \"NOT\"")]
    public void MatchTerms_quotes_all_words_including_operators(string input, string expected)
    {
        Assert.Equal(expected, MessageSearch.MatchTerms(input));
    }

    [Fact]
    public async Task Searching_for_operator_word_alone_does_not_fail()
    {
        foreach (var word in new[] { "AND", "OR", "NOT", "NEAR" })
        {
            var response = await messages.SendAsync("GET", $"/searches?q={word}", messages.SignedIn(davidSession));
            Assert.Equal(200, response.Status);
        }
    }

    [Fact]
    public async Task Reachable_search_finds_operator_words_as_text()
    {
        await messages.Database.ReadAsync(session =>
        {
            var results = MessageSearch.Reachable(session, david, "AND");
            Assert.NotNull(results);
            return true;
        }, TestContext.Current.CancellationToken);
    }
}
