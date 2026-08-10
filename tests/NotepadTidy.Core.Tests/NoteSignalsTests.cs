using NotepadTidy.Core;

namespace NotepadTidy.Core.Tests;

/// <summary>
/// Ces signaux sont la base du classement gratuit : ils identifient le type
/// d'une note et son sujet sans modèle, sans réseau et sans coût.
/// </summary>
public class NoteSignalsTests
{
    [Theory]
    [InlineData("Salut Alex, dis-moi si ça te va", true)]
    [InlineData("Bonjour,\r\nJe suis tombé sur votre site", true)]
    [InlineData("coucou, petite question", true)]
    [InlineData("docker compose up -d", false)]
    [InlineData("il faut que je pense à salut au fait", false)] // pas en tête
    public void LooksLikeMessageDraft_DetectsOpeningGreeting(string text, bool expected)
        => Assert.Equal(expected, NoteSignals.LooksLikeMessageDraft(text));

    [Theory]
    [InlineData("… merci d'avance, cordialement", true)]
    [InlineData("contact@project.fr", true)]
    [InlineData("bonne journée à toi", true)]
    [InlineData("select * from users", false)]
    public void HasEpistolaryMarker_DetectsCorrespondence(string text, bool expected)
        => Assert.Equal(expected, NoteSignals.HasEpistolaryMarker(text));

    [Theory]
    [InlineData("npm install --save", true)]
    [InlineData("const x = () => 1", true)]
    [InlineData("sudo systemctl restart nginx", true)]
    [InlineData("passe une bonne soirée", false)]
    public void HasTechnicalMarker_DetectsCode(string text, bool expected)
        => Assert.Equal(expected, NoteSignals.HasTechnicalMarker(text));

    [Fact]
    public void Domains_ExtractsHostsWithoutScheme()
    {
        var domains = NoteSignals.Domains(
            "voir https://api.contoso.tv/delta et http://www.example.com/page").ToList();

        Assert.Equal(["api.contoso.tv", "example.com"], domains);
    }

    [Fact]
    public void IsMostlyLinks_DetectsLinkDumps()
    {
        Assert.True(NoteSignals.IsMostlyLinks(
            "https://a.example.com https://b.example.com https://c.example.com ok"));
        Assert.False(NoteSignals.IsMostlyLinks(
            "Un long paragraphe qui explique beaucoup de choses en détail, avec " +
            "des phrases entières et un seul lien https://a.example.com au milieu " +
            "de tout ce texte qui domine largement le contenu de la note."));
    }

    /// <summary>
    /// Le filtre décisif : en français un nom commun n'est pas capitalisé en
    /// milieu de phrase. Ce qui l'est désigne un projet, un produit ou une
    /// personne — le seul signal de sujet fiable du corpus.
    /// </summary>
    [Fact]
    public void ProperNouns_KeepsMidSentenceCapitalsOnly()
    {
        const string text = "Je regarde le compte Contoso de Project avec Alex. " +
                            "Ensuite je passe sur Fabrikam.";

        var found = NoteSignals.ProperNouns(text).ToList();

        Assert.Contains("Contoso", found);
        Assert.Contains("Project", found);
        Assert.Contains("Alex", found);
        Assert.Contains("Fabrikam", found);
        // "Je" et "Ensuite" ouvrent une phrase : ce ne sont pas des noms propres.
        Assert.DoesNotContain("Je", found);
        Assert.DoesNotContain("Ensuite", found);
    }

    [Fact]
    public void ProperNouns_IgnoresGreetingsAndCalendarWords()
    {
        const string text = "Bref, Bonjour et Merci ne sont pas des projets, " +
                            "contrairement à Gamma. Rendez-vous Lundi.";

        var found = NoteSignals.ProperNouns(text).ToList();

        Assert.Contains("Gamma", found);
        Assert.DoesNotContain("Bonjour", found);
        Assert.DoesNotContain("Merci", found);
        Assert.DoesNotContain("Lundi", found);
    }

}
