using NotepadTidy.Core;

namespace NotepadTidy.Core.Tests;

/// <summary>
/// Real-world case: prepending "#theme" to the first line glues the theme to
/// the content — "#themeHi there!". No separator says where it ends. But a
/// theme is reused, so the shared prefix reveals it. These tests pin that
/// reasoning down.
/// </summary>
public class ThemeVocabularyTests
{
    [Fact]
    public void RawHeadingLine_ReadsWhatFollowsTheMarker()
    {
        Assert.Equal("themeHi!", ThemeVocabulary.RawHeadingLine("#themeHi!\r\nbody"));
        Assert.Equal("project", ThemeVocabulary.RawHeadingLine("#project\r\n\r\nbody"));
        Assert.Null(ThemeVocabulary.RawHeadingLine("no marker here"));
    }

    [Fact]
    public void RawHeadingLine_HandlesLoneCarriageReturns()
    {
        // Notepad does not always write CRLF pairs. Splitting on '\n' only
        // would swallow the whole note into the heading.
        Assert.Equal("project", ThemeVocabulary.RawHeadingLine("#project\rbody\rmore"));
    }

    [Fact]
    public void Discover_SeparatesThemesGluedToContent()
    {
        string[] notes =
        [
            "#alphaHi! short message",
            "#alphaEvent received from the channel",
            "#alpha// configuration block",
            "#betaabout the columns",
            "#betaecommerce site",
            "#betahourly rate",
        ];

        var themes = ThemeVocabulary.Discover(notes);

        Assert.Contains("alpha", themes);
        Assert.Contains("beta", themes);
    }

    /// <summary>
    /// A short prefix is always at least as frequent as its extensions, so
    /// sorting by count alone would merge distinct themes under "pr".
    /// </summary>
    [Fact]
    public void Discover_DoesNotCollapseDistinctThemesSharingAPrefix()
    {
        string[] notes =
        [
            "#projectcreate table", "#projectuser list", "#projectweb security",
            "#promoflyer draft", "#promobanner sizes", "#promolanding copy",
        ];

        var themes = ThemeVocabulary.Discover(notes);

        Assert.Contains("project", themes);
        Assert.Contains("promo", themes);
        Assert.DoesNotContain("pr", themes);
    }

    /// <summary>
    /// Mirror of the previous test: without a cap on length, two notes opening
    /// the same way would turn a whole sentence into a theme.
    /// </summary>
    [Fact]
    public void Discover_DoesNotTurnASharedSentenceIntoATheme()
    {
        string[] notes =
        [
            "#alphaHi! short message about the invoice",
            "#alphaHi! just a quick follow-up",
            "#alphachannel configuration",
            "#alphaevent received",
            "#alphalive token",
        ];

        var themes = ThemeVocabulary.Discover(notes);

        Assert.Contains("alpha", themes);
        Assert.DoesNotContain(themes, t => t.StartsWith("alphahi", StringComparison.Ordinal));
    }

    [Fact]
    public void Discover_IgnoresThemesUsedOnlyOnce()
    {
        string[] notes = ["#projectone", "#projecttwo", "#singleton unique"];

        var themes = ThemeVocabulary.Discover(notes);

        Assert.Contains("project", themes);
        Assert.DoesNotContain(themes, t => t.StartsWith("singleton", StringComparison.Ordinal));
    }

    [Fact]
    public void Match_IsCaseInsensitive()
    {
        string[] vocabulary = ["project"];

        Assert.Equal("project", ThemeVocabulary.Match("#Project test\r\nbody", vocabulary));
        Assert.Equal("project", ThemeVocabulary.Match("#PROJECT other\r\nbody", vocabulary));
        Assert.Equal("project", ThemeVocabulary.Match("#projectglued\r\nbody", vocabulary));
    }

    [Fact]
    public void Match_PrefersTheLongestApplicableTheme()
    {
        string[] vocabulary = ["game", "gameboard"];

        Assert.Equal("gameboard", ThemeVocabulary.Match("#gameboard layout\r\nx", vocabulary));
        Assert.Equal("game", ThemeVocabulary.Match("#gamecontroller setup\r\nx", vocabulary));
    }

    [Fact]
    public void Match_ReturnsNullWithoutMarker()
        => Assert.Null(ThemeVocabulary.Match("just text\r\nmore", ["project"]));

    [Fact]
    public void Discover_ToleratesNotesWithoutMarker()
    {
        string[] notes = ["#projectone", "#projecttwo", "note without marker", ""];

        var themes = ThemeVocabulary.Discover(notes);

        Assert.Contains("project", themes);
    }
}
