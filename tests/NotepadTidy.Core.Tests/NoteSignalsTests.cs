using NotepadTidy.Core;

namespace NotepadTidy.Core.Tests;

/// <summary>
/// Optional, language-specific signals. They complement the corpus statistics
/// but never replace them — see the remarks on <see cref="NoteSignals"/>.
/// </summary>
public class NoteSignalsTests
{
    [Theory]
    [InlineData("Salut Alex, dis-moi si ça te va", true)]
    [InlineData("Bonjour,\r\nJe suis tombé sur votre site", true)]
    [InlineData("coucou, petite question", true)]
    [InlineData("docker compose up -d", false)]
    [InlineData("il faut que je pense à salut au fait", false)] // not at the start
    public void LooksLikeMessageDraft_DetectsOpeningGreeting(string text, bool expected)
        => Assert.Equal(expected, NoteSignals.LooksLikeMessageDraft(text));

    [Theory]
    [InlineData("… merci d'avance, cordialement", true)]
    [InlineData("contact@example.com", true)]
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
            "see https://api.example.org/v1 and http://www.example.com/page").ToList();

        Assert.Equal(["api.example.org", "example.com"], domains);
    }

    [Fact]
    public void IsMostlyLinks_DetectsLinkDumps()
    {
        Assert.True(NoteSignals.IsMostlyLinks(
            "https://a.example.com https://b.example.com https://c.example.com ok"));
        Assert.False(NoteSignals.IsMostlyLinks(
            "A long paragraph explaining many things in detail, with whole " +
            "sentences and a single link https://a.example.com buried in the " +
            "middle of text that clearly dominates the note."));
    }

    /// <summary>
    /// The decisive filter for Latin-script French: a common noun is not
    /// capitalised mid-sentence, so what is must be a project, product or
    /// person. Deliberately not used as the backbone of classification.
    /// </summary>
    [Fact]
    public void ProperNouns_KeepsMidSentenceCapitalsOnly()
    {
        const string text = "Je regarde le compte Twitter de Contoso avec Alex. " +
                            "Ensuite je passe sur Fabrikam.";

        var found = NoteSignals.ProperNouns(text).ToList();

        Assert.Contains("Twitter", found);
        Assert.Contains("Contoso", found);
        Assert.Contains("Alex", found);
        Assert.Contains("Fabrikam", found);
        // "Je" and "Ensuite" open a sentence: not proper nouns.
        Assert.DoesNotContain("Je", found);
        Assert.DoesNotContain("Ensuite", found);
    }

    [Fact]
    public void ProperNouns_IgnoresGreetingsAndCalendarWords()
    {
        const string text = "Bref, Bonjour et Merci ne sont pas des projets, " +
                            "contrairement à Contoso. Rendez-vous Lundi.";

        var found = NoteSignals.ProperNouns(text).ToList();

        Assert.Contains("Contoso", found);
        Assert.DoesNotContain("Bonjour", found);
        Assert.DoesNotContain("Merci", found);
        Assert.DoesNotContain("Lundi", found);
    }
}
