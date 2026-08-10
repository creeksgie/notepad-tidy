using NotepadTidy.Core;

namespace NotepadTidy.Core.Tests;

/// <summary>
/// Cas réel : en ajoutant « #thème » au début de la première ligne, le thème
/// se retrouve collé au contenu — « #themeSalut ! ». Aucun séparateur ne dit
/// où il s'arrête. Mais un thème sert plusieurs fois, donc le préfixe partagé
/// le révèle. Ces tests figent ce raisonnement.
/// </summary>
public class ThemeVocabularyTests
{
    [Fact]
    public void RawHeadingLine_ReadsWhatFollowsTheMarker()
    {
        Assert.Equal("themeSalut !", ThemeVocabulary.RawHeadingLine("#themeSalut !\r\nsuite"));
        Assert.Equal("plexo", ThemeVocabulary.RawHeadingLine("#plexo\r\n\r\ncontenu"));
        Assert.Null(ThemeVocabulary.RawHeadingLine("pas de marqueur ici"));
    }

    [Fact]
    public void Discover_SeparatesThemesGluedToContent()
    {
        string[] notes =
        [
            "#themeSalut ! petit message",
            "#themeÉvénement reçu du canal",
            "#theme// configuration contoso",
            "#projectpour les colonnes",
            "#projectsite ecommerce",
            "#projecttaux horaire du mois",
        ];

        var themes = ThemeVocabulary.Discover(notes);

        Assert.Contains("theme", themes);
        Assert.Contains("project", themes);
    }

    /// <summary>
    /// Un préfixe court est toujours au moins aussi fréquent que ses
    /// extensions. Trier par nombre seul fusionnerait donc plexo et play
    /// sous « pl ».
    /// </summary>
    [Fact]
    public void Discover_DoesNotCollapseDistinctThemesSharingAPrefix()
    {
        string[] notes =
        [
            "#plexocreate table public", "#plexoliste des membres", "#plexoweb app securite",
            "#playcaravane sand witch", "#playbas gauche haut", "#playconfig manette",
        ];

        var themes = ThemeVocabulary.Discover(notes);

        Assert.Contains("plexo", themes);
        Assert.Contains("play", themes);
        Assert.DoesNotContain("pl", themes);
    }

    /// <summary>
    /// Symétrique du précédent : sans plafond sur la longueur, deux notes
    /// ouvrant pareil donneraient une phrase entière pour thème.
    /// </summary>
    [Fact]
    public void Discover_DoesNotTurnASharedSentenceIntoATheme()
    {
        string[] notes =
        [
            "#themeSalut ! petit message pour la facture",
            "#themeSalut ! je me permets une relance",
            "#themeconfiguration du canal",
            "#themeévénement reçu",
            "#themetoken du live",
        ];

        var themes = ThemeVocabulary.Discover(notes);

        Assert.Contains("theme", themes);
        Assert.DoesNotContain(themes, t => t.StartsWith("themesalut", StringComparison.Ordinal));
    }

    [Fact]
    public void Discover_IgnoresThemesUsedOnlyOnce()
    {
        string[] notes = ["#plexoun", "#plexodeux", "#singleton unique"];

        var themes = ThemeVocabulary.Discover(notes);

        Assert.Contains("plexo", themes);
        Assert.DoesNotContain(themes, t => t.StartsWith("singleton", StringComparison.Ordinal));
    }

    [Fact]
    public void Match_IsCaseInsensitive()
    {
        string[] vocabulary = ["plexo"];

        Assert.Equal("plexo", ThemeVocabulary.Match("#Plexo test\r\ncontenu", vocabulary));
        Assert.Equal("plexo", ThemeVocabulary.Match("#PLEXO autre\r\ncontenu", vocabulary));
        Assert.Equal("plexo", ThemeVocabulary.Match("#plexocollé\r\ncontenu", vocabulary));
    }

    [Fact]
    public void Match_PrefersTheLongestApplicableTheme()
    {
        string[] vocabulary = ["play", "playpokemon"];

        Assert.Equal("playpokemon", ThemeVocabulary.Match("#playpokemon deck\r\nx", vocabulary));
        Assert.Equal("play", ThemeVocabulary.Match("#playconfig manette\r\nx", vocabulary));
    }

    [Fact]
    public void Match_ReturnsNullWithoutMarker()
        => Assert.Null(ThemeVocabulary.Match("juste du texte\r\nsuite", ["plexo"]));

    [Fact]
    public void Discover_ToleratesNotesWithoutMarker()
    {
        string[] notes = ["#plexoun", "#plexodeux", "note sans marqueur", ""];

        var themes = ThemeVocabulary.Discover(notes);

        Assert.Contains("plexo", themes);
    }
}
