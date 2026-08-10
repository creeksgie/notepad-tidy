using NotepadTidy.Core;

namespace NotepadTidy.Core.Tests;

/// <summary>
/// These tests exist for one reason: to prove that classification relies on no
/// hardcoded list, and therefore works for someone else, in another language.
/// The same code runs over French, English and German samples — none of which
/// is named anywhere in the library.
/// </summary>
public class CorpusProfileTests
{
    private static readonly string[] French =
    [
        "je pense que le site est pas mal mais il faut revoir la page de contact",
        "salut Alex, je te confirme que le devis est parti hier soir",
        "il faut que je pense à relancer le client pour le devis du site",
        "salut Robin, est-ce que le paiement est bien passé de ton côté ?",
        "le serveur est tombé cette nuit, il faut que je regarde les logs",
        "docker compose up et le conteneur redémarre tout seul",
    ];

    private static readonly string[] English =
    [
        "i think the website is fine but we need to fix the contact page",
        "hi Alex, just confirming that the quote went out yesterday",
        "we need to follow up with the client about the website quote",
        "hi Robin, did the payment go through on your side ?",
        "the server went down last night, need to check the logs",
        "docker compose up and the container restarts by itself",
    ];

    [Fact]
    public void StopWords_EmergeFromFrenchCorpus_WithoutAnyFrenchList()
    {
        var profile = new CorpusProfile(French);

        Assert.Contains("le", profile.StopWords);
        Assert.Contains("que", profile.StopWords);
        // A rare, meaningful word must never become a stop word.
        Assert.DoesNotContain("docker", profile.StopWords);
        Assert.DoesNotContain("alex", profile.StopWords);
    }

    [Fact]
    public void StopWords_EmergeFromEnglishCorpus_WithTheSameCode()
    {
        var profile = new CorpusProfile(English);

        Assert.Contains("the", profile.StopWords);
        Assert.DoesNotContain("docker", profile.StopWords);
        Assert.DoesNotContain("alex", profile.StopWords);
    }

    [Fact]
    public void OpeningWords_DetectGreetingsInEitherLanguage()
    {
        // "salut" and "hi" are discovered the same way: they are the words
        // several notes begin with. Neither is listed anywhere.
        Assert.Contains("salut", new CorpusProfile(French, openingThreshold: 0.2).OpeningWords);
        Assert.Contains("hi", new CorpusProfile(English, openingThreshold: 0.2).OpeningWords);
    }

    [Fact]
    public void OpensLikeCorrespondence_MatchesDraftsInEitherLanguage()
    {
        var fr = new CorpusProfile(French, openingThreshold: 0.2);
        var en = new CorpusProfile(English, openingThreshold: 0.2);

        Assert.True(fr.OpensLikeCorrespondence("salut Camille, petite question"));
        Assert.False(fr.OpensLikeCorrespondence("docker compose down"));
        Assert.True(en.OpensLikeCorrespondence("hi Camille, quick question"));
        Assert.False(en.OpensLikeCorrespondence("docker compose down"));
    }

    /// <summary>
    /// The "capitalised means proper noun" heuristic collapses in German,
    /// where every common noun is capitalised. The statistical approach does
    /// not look at case at all.
    /// </summary>
    [Fact]
    public void Tokenize_HandlesGermanWhereEveryNounIsCapitalised()
    {
        string[] german =
        [
            "die Rechnung für den Kunden ist noch nicht bezahlt",
            "die Webseite für den Kunden ist fast fertig",
            "der Server ist heute Nacht abgestürzt, ich prüfe die Logs",
            "die Rechnung für die Webseite geht morgen raus",
        ];

        var profile = new CorpusProfile(german);

        Assert.Contains("die", profile.StopWords);
        Assert.DoesNotContain("server", profile.StopWords);
        Assert.True(profile.InverseDocumentFrequency("server")
                  > profile.InverseDocumentFrequency("die"));
    }

    [Fact]
    public void InverseDocumentFrequency_RanksRareWordsAboveCommon()
    {
        var profile = new CorpusProfile(French);

        Assert.True(profile.InverseDocumentFrequency("docker")
                  > profile.InverseDocumentFrequency("le"));
    }

    [Fact]
    public void DistinctiveTokens_IgnoreNumbersAndStopWords()
    {
        string[] corpus =
        [
            "meeting at 14 30 with the client about project atlas",
            "meeting at 14 30 tomorrow about project atlas",
            "the client approved 14 30 for the project",
        ];
        var profile = new CorpusProfile(corpus);

        var tokens = profile.DistinctiveTokens(corpus[0], 5).Select(t => t.Token).ToList();

        Assert.DoesNotContain("14", tokens);
        Assert.DoesNotContain("30", tokens);
        Assert.DoesNotContain("the", tokens);
    }

    [Fact]
    public void Tokenize_StripsDiacriticsSoVariantsMatch()
    {
        var withAccent = CorpusProfile.Tokenize("Événement").ToList();
        var without = CorpusProfile.Tokenize("evenement").ToList();

        Assert.Equal(without, withAccent);
    }

    [Fact]
    public void Tokenize_KeepsAlphanumericIdentifiers()
    {
        var tokens = CorpusProfile.Tokenize("migrate to net10 and http2").ToList();

        Assert.Contains("net10", tokens);
        Assert.Contains("http2", tokens);
    }
}
