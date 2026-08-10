using System.Text.RegularExpressions;

namespace NotepadTidy.Core;

/// <summary>
/// Signaux déterministes extractibles d'une note, sans modèle et sans réseau.
///
/// <para><b>Portée — à lire avant de s'en servir.</b> Une partie de ces
/// signaux est spécifique au français et à l'alphabet latin : les salutations,
/// les formules de politesse, et surtout <see cref="ProperNouns"/>, qui
/// suppose qu'un nom commun n'est pas capitalisé en milieu de phrase — faux en
/// allemand, sans objet en japonais ou en arabe.</para>
///
/// <para>Ce sont donc des <b>compléments optionnels</b>, pas le socle du
/// classement. Le socle, c'est <see cref="CorpusProfile"/>, qui ne dépend
/// d'aucune langue parce qu'il déduit tout de la distribution des données.
/// Les seuls signaux réellement universels ici sont les URL et les domaines.
/// </para>
///
/// Tout est en fonctions pures : testable sans disque ni Notepad.
/// </summary>
public static partial class NoteSignals
{
    [GeneratedRegex(@"^\s*(salut|bonjour|coucou|bonsoir|hello|hey|yo|cc|re)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex GreetingRegex { get; }

    [GeneratedRegex(@"cordialement|bonne journ|bonne soir|bon week|merci d.avance|à bientôt|a bientot|bien à (toi|vous)",
        RegexOptions.IgnoreCase)]
    private static partial Regex EpistolaryRegex { get; }

    [GeneratedRegex(@"https?://([^\s/]+)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex { get; }

    [GeneratedRegex(@"(\bnpm\b|\bdocker\b|\bsudo\b|\bgit\b|\bapt\b|\bSELECT\b|\bfunction\b|\bconst\b|\bimport\b|=>|\{|\}|\$\w+|\bpsql\b|\bsystemctl\b)")]
    private static partial Regex TechnicalRegex { get; }

    [GeneratedRegex(@"[A-Za-zÀ-ÖØ-öø-ÿ][A-Za-zÀ-ÖØ-öø-ÿ0-9_-]{3,}")]
    private static partial Regex TokenRegex { get; }

    /// <summary>
    /// Mot capitalisé qui n'ouvre pas une phrase. En français, un nom commun
    /// n'est pas capitalisé en milieu de phrase : ce filtre isole donc les
    /// noms propres — projets, personnes, produits — sans aucun modèle.
    /// </summary>
    [GeneratedRegex(@"(?<![.!?\r\n]\s{0,4}|^\s{0,4})\b\p{Lu}[\p{Ll}\p{Lu}0-9_-]{2,}\b",
        RegexOptions.Multiline)]
    private static partial Regex ProperNounRegex { get; }

    /// <summary>Une salutation en tête trahit un brouillon de message.</summary>
    public static bool LooksLikeMessageDraft(string text) => GreetingRegex.IsMatch(text);

    /// <summary>Formule de politesse ou adresse mail : registre épistolaire.</summary>
    public static bool HasEpistolaryMarker(string text) => EpistolaryRegex.IsMatch(text)
        || text.Contains('@', StringComparison.Ordinal);

    public static bool HasTechnicalMarker(string text) => TechnicalRegex.IsMatch(text);

    public static IEnumerable<string> Domains(string text)
    {
        foreach (Match m in UrlRegex.Matches(text))
            yield return m.Groups[1].Value.TrimStart("www.".ToCharArray()).ToLowerInvariant();
    }

    public static int UrlCount(string text) => UrlRegex.Matches(text).Count;

    /// <summary>
    /// Une note faite presque uniquement de liens : la similarité sémantique
    /// n'a presque rien à mordre dessus, il lui faut un traitement séparé.
    /// </summary>
    public static bool IsMostlyLinks(string text)
    {
        int urls = UrlCount(text);
        if (urls == 0) return false;
        int nonUrlChars = UrlRegex.Replace(text, "").Trim().Length;
        return nonUrlChars < text.Length / 3;
    }

    // Une liste de mots vides écrite à la main est toujours incomplète et ne
    // vaut que pour une langue. Elle a été supprimée au profit de
    // CorpusProfile, qui déduit les mots vides de la distribution réelle.

    /// <summary>
    /// Noms propres candidats. C'est le signal le plus discriminant du corpus
    /// pour identifier un projet ou un interlocuteur — bien plus que la
    /// fréquence brute des mots, noyée par le vocabulaire courant.
    /// </summary>
    public static IEnumerable<string> ProperNouns(string text)
    {
        foreach (Match m in ProperNounRegex.Matches(text))
        {
            var value = m.Value;
            if (!ProperNounNoise.Contains(value)) yield return value;
        }
    }

    /// <summary>Capitalisés fréquents qui ne désignent ni projet ni personne.</summary>
    private static readonly HashSet<string> ProperNounNoise = new(StringComparer.OrdinalIgnoreCase)
    {
        "Bonjour","Salut","Coucou","Bonsoir","Merci","Cordialement","Hello",
        "Lundi","Mardi","Mercredi","Jeudi","Vendredi","Samedi","Dimanche",
        "Janvier","Février","Mars","Avril","Juin","Juillet","Août","Septembre",
        "Octobre","Novembre","Décembre","Https","Http",
    };

}
