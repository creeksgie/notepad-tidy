using System.Text.RegularExpressions;

namespace NotepadTidy.Core;

/// <summary>
/// Deterministic signals extractable from a note, with no model and no network.
///
/// <para><b>Scope — read this before relying on it.</b> Several of these signals
/// are specific to French and to the Latin script: the greetings, the politeness
/// formulas, and above all <see cref="ProperNouns"/>, which assumes a common
/// noun is not capitalised mid-sentence — false in German, meaningless in
/// Japanese or Arabic.</para>
///
/// <para>They are therefore <b>optional complements</b>, not the backbone of
/// filing. The backbone is <see cref="CorpusProfile"/>, which depends on no
/// language because it derives everything from the distribution of the data.
/// The only genuinely universal signals here are URLs and domains.</para>
///
/// Everything is a pure function: testable without disk or Notepad.
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

    /// <summary>
    /// A capitalised word that does not open a sentence. In French a common noun
    /// is not capitalised mid-sentence, so this isolates proper nouns —
    /// projects, people, products — with no model at all.
    /// </summary>
    [GeneratedRegex(@"(?<![.!?\r\n]\s{0,4}|^\s{0,4})\b\p{Lu}[\p{Ll}\p{Lu}0-9_-]{2,}\b",
        RegexOptions.Multiline)]
    private static partial Regex ProperNounRegex { get; }

    /// <summary>An opening greeting betrays a message draft.</summary>
    public static bool LooksLikeMessageDraft(string text) => GreetingRegex.IsMatch(text);

    /// <summary>Politeness formula or e-mail address: correspondence register.</summary>
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
    /// A note made almost entirely of links: semantic similarity has nearly
    /// nothing to bite on, so it needs separate handling.
    /// </summary>
    public static bool IsMostlyLinks(string text)
    {
        int urls = UrlCount(text);
        if (urls == 0) return false;
        int nonUrlChars = UrlRegex.Replace(text, "").Trim().Length;
        return nonUrlChars < text.Length / 3;
    }

    /// <summary>
    /// Candidate proper nouns. The most discriminating subject signal in a
    /// Latin-script corpus — far more so than raw word frequency, which drowns
    /// in everyday vocabulary.
    /// </summary>
    public static IEnumerable<string> ProperNouns(string text)
    {
        foreach (Match m in ProperNounRegex.Matches(text))
        {
            var value = m.Value;
            if (!ProperNounNoise.Contains(value)) yield return value;
        }
    }

    /// <summary>Frequent capitalised words that name neither a project nor a person.</summary>
    private static readonly HashSet<string> ProperNounNoise = new(StringComparer.OrdinalIgnoreCase)
    {
        "Bonjour","Salut","Coucou","Bonsoir","Merci","Cordialement","Hello",
        "Lundi","Mardi","Mercredi","Jeudi","Vendredi","Samedi","Dimanche",
        "Janvier","Février","Mars","Avril","Juin","Juillet","Août","Septembre",
        "Octobre","Novembre","Décembre","Https","Http",
    };

    // A handwritten stop-word list is always incomplete and only ever valid for
    // one language. It was removed in favour of CorpusProfile, which derives
    // stop words from the actual distribution.
}
