using NotepadTidy.Core;

namespace NotepadTidy.Core.Tests;

/// <summary>
/// Le titre explicite est le seul mécanisme de classement à la fois sans
/// administration et indépendant de la langue : la catégorie est déclarée par
/// l'utilisateur, pas devinée par le code.
/// </summary>
public class NoteHeadingTests
{
    [Theory]
    [InlineData("# gamma beta\r\nle lien pour les testeurs", "gamma beta")]
    [InlineData("#Alex\r\nrépondre au sujet du devis", "Alex")]
    [InlineData("  # VPS déploiement \r\nsystemctl restart", "VPS déploiement")]
    public void ExtractExplicit_ReadsMarkedHeading(string note, string expected)
        => Assert.Equal(expected, NoteHeading.ExtractExplicit(note));

    [Theory]
    [InlineData("gamma beta\r\nle lien")]          // pas de marqueur
    [InlineData("#\r\ncontenu")]                    // marqueur seul
    [InlineData("#   \r\ncontenu")]                 // titre vide
    [InlineData("")]
    public void ExtractExplicit_ReturnsNullWithoutUsableMarker(string note)
        => Assert.Null(NoteHeading.ExtractExplicit(note));

    /// <summary>
    /// Les langues sans alphabet latin doivent marcher à l'identique : rien
    /// dans le mécanisme ne dépend de l'écriture.
    /// </summary>
    [Theory]
    [InlineData("# Rechnungen\r\nnoch offen", "Rechnungen")]
    [InlineData("# 仕事のメモ\r\n本文", "仕事のメモ")]
    [InlineData("# работа\r\nтекст", "работа")]
    public void ExtractExplicit_IsScriptAgnostic(string note, string expected)
        => Assert.Equal(expected, NoteHeading.ExtractExplicit(note));

    /// <summary>
    /// Ces cas viennent tous du corpus réel. Deviner le titre à la forme de la
    /// première ligne les classerait comme thèmes, alors que ce sont des codes
    /// couleur, des ports, des horaires et des mots de passe. C'est la raison
    /// d'être du marqueur explicite.
    /// </summary>
    [Theory]
    [InlineData("2e343b 4a5568 1c1f26 f7fafc\r\npalette du site")]
    [InlineData("port 5432\r\nconfig postgres")]
    [InlineData("mercredi 10h 14h\r\ndispos de la semaine")]
    public void GuessImplicit_ProducesFalsePositives_HenceTheMarker(string note)
    {
        // On documente le défaut plutôt que de prétendre qu'il n'existe pas.
        Assert.NotNull(NoteHeading.GuessImplicit(note));
        Assert.Null(NoteHeading.ExtractExplicit(note));
    }

    [Theory]
    [InlineData("Je pense que le site est pas mal mais il faudrait revoir la page.", false)]
    [InlineData("Salut Alex,", false)]
    [InlineData("https://example.com/tres/long/lien", false)]
    [InlineData("gamma beta", true)]
    public void LooksLikeHeading_UsesStructureNotVocabulary(string line, bool expected)
        => Assert.Equal(expected, NoteHeading.LooksLikeHeading(line));

    [Fact]
    public void Normalize_CollapsesVariantsOfTheSameTheme()
    {
        Assert.Equal(
            NoteHeading.Normalize("Gamma — Bêta"),
            NoteHeading.Normalize("gamma   beta"));
    }
}
