using NotepadTidy.Core;

namespace NotepadTidy.Core.Tests;

/// <summary>
/// Ces tests existent pour une raison précise : prouver que le classement ne
/// dépend d'aucune liste codée en dur, et fonctionnerait donc chez quelqu'un
/// d'autre, dans une autre langue. Le même code est passé sur du français, de
/// l'anglais et de l'allemand — aucun n'est nommé dans la bibliothèque.
/// </summary>
public class CorpusProfileTests
{
    private static readonly string[] French =
    [
        "je pense que le site est pas mal mais il faut revoir la page de contact",
        "salut Alex, je te confirme que le devis est parti hier soir",
        "il faut que je pense à relancer le client pour le devis du site",
        "salut Camille, est-ce que le paiement est bien passé de ton côté ?",
        "le serveur est tombé cette nuit, il faut que je regarde les logs",
        "docker compose up et le conteneur redémarre tout seul",
    ];

    private static readonly string[] English =
    [
        "i think the website is fine but we need to fix the contact page",
        "hi Alex, just confirming that the quote went out yesterday",
        "we need to follow up with the client about the website quote",
        "hi Camille, did the payment go through on your side ?",
        "the server went down last night, need to check the logs",
        "docker compose up and the container restarts by itself",
    ];

    [Fact]
    public void StopWords_EmergeFromFrenchCorpus_WithoutAnyFrenchList()
    {
        var profile = new CorpusProfile(French);

        Assert.Contains("le", profile.StopWords);
        Assert.Contains("que", profile.StopWords);
        // Un mot rare et porteur de sens ne doit jamais devenir un mot vide.
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
        // "salut" et "hi" sont découverts de la même façon : ce sont les mots
        // par lesquels plusieurs notes commencent. Aucun n'est listé nulle part.
        Assert.Contains("salut", new CorpusProfile(French, openingThreshold: 0.2).OpeningWords);
        Assert.Contains("hi", new CorpusProfile(English, openingThreshold: 0.2).OpeningWords);
    }

    [Fact]
    public void OpensLikeCorrespondence_MatchesDraftsInEitherLanguage()
    {
        var fr = new CorpusProfile(French, openingThreshold: 0.2);
        var en = new CorpusProfile(English, openingThreshold: 0.2);

        Assert.True(fr.OpensLikeCorrespondence("salut Robin, petite question"));
        Assert.False(fr.OpensLikeCorrespondence("docker compose down"));
        Assert.True(en.OpensLikeCorrespondence("hi Robin, quick question"));
        Assert.False(en.OpensLikeCorrespondence("docker compose down"));
    }

    [Fact]
    public void Tokenize_HandlesGermanWhereEveryNounIsCapitalised()
    {
        // L'heuristique « capitalisé = nom propre » s'effondre en allemand.
        // L'approche statistique, elle, ne s'appuie pas du tout sur la casse.
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
            "rendez-vous à 14 30 avec le client pour le projet gamma",
            "rendez-vous à 14 30 demain pour le projet gamma",
            "le client a validé 14 30 pour le projet",
        ];
        var profile = new CorpusProfile(corpus);

        var tokens = profile.DistinctiveTokens(corpus[0], 5).Select(t => t.Token).ToList();

        Assert.DoesNotContain("14", tokens);
        Assert.DoesNotContain("30", tokens);
        Assert.DoesNotContain("le", tokens);
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
        var tokens = CorpusProfile.Tokenize("migration vers net10 et gpt4").ToList();

        Assert.Contains("net10", tokens);
        Assert.Contains("gpt4", tokens);
    }
}
