using NotepadTidy.Core;

namespace NotepadTidy.Core.Tests;

/// <summary>
/// Fallback for notes without a heading. It only ever files into themes the
/// user already declared elsewhere — it never invents one. Most of these tests
/// pin down when it must REFUSE, because a wrong assignment costs more than an
/// unfiled note.
/// </summary>
public class ThemeMentionTests
{
    private static readonly string[] Vocabulary = ["project", "promo", "invoice", "game night"];

    [Fact]
    public void FindInBody_FilesByExplicitMention()
    {
        var match = ThemeMention.FindInBody("need to update the project schema", Vocabulary);

        Assert.True(match.Found);
        Assert.Equal("project", match.Theme);
    }

    [Fact]
    public void FindInBody_MatchesWholeTokensOnly()
    {
        // "promo" must not match inside "promotional", nor "game" inside
        // "gameplay". Substring matching would file notes almost at random.
        var match = ThemeMention.FindInBody("some promotional gameplay ideas", Vocabulary);

        Assert.False(match.Found);
    }

    [Fact]
    public void FindInBody_IsCaseAndDiacriticInsensitive()
    {
        var match = ThemeMention.FindInBody("réunion PROJECT demain", Vocabulary);

        Assert.Equal("project", match.Theme);
    }

    [Fact]
    public void FindInBody_SupportsMultiWordThemes()
    {
        var match = ThemeMention.FindInBody("bring snacks to game night on friday", Vocabulary);

        Assert.Equal("game night", match.Theme);
    }

    [Fact]
    public void FindInBody_RequiresAllTokensOfAMultiWordTheme()
    {
        var match = ThemeMention.FindInBody("the game was fun last night", Vocabulary);

        Assert.False(match.Found);
    }

    /// <summary>
    /// The decisive refusal: two themes cited equally often is genuine
    /// ambiguity, and guessing would teach the user to distrust the tool.
    /// </summary>
    [Fact]
    public void FindInBody_RefusesWhenTwoThemesAreEquallyCited()
    {
        var match = ThemeMention.FindInBody("the project invoice is late", Vocabulary);

        Assert.False(match.Found);
        Assert.Contains("ambigu", match.Reason);
    }

    [Fact]
    public void FindInBody_AcceptsAClearWinner()
    {
        // project ×3 against invoice ×1 clears the margin.
        var match = ThemeMention.FindInBody(
            "project kickoff, project budget, project invoice", Vocabulary);

        Assert.Equal("project", match.Theme);
        Assert.Equal(3, match.Hits);
    }

    [Fact]
    public void FindInBody_IgnoresThemesTooShortToBeDistinctive()
    {
        // A two-letter theme collides with ordinary words constantly.
        var match = ThemeMention.FindInBody("on va au bal ce soir", ["ba", "al"]);

        Assert.False(match.Found);
    }

    [Fact]
    public void FindInBody_ReturnsNoMatchWhenNothingIsCited()
    {
        var match = ThemeMention.FindInBody("completely unrelated content", Vocabulary);

        Assert.False(match.Found);
        Assert.Null(match.Theme);
    }

    [Fact]
    public void FindInBody_HandlesEmptyInput()
    {
        Assert.False(ThemeMention.FindInBody("", Vocabulary).Found);
        Assert.False(ThemeMention.FindInBody("some text", []).Found);
    }
}
