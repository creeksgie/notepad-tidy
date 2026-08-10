using System.Globalization;
using System.Text;

namespace NotepadTidy.Core;

/// <summary>
/// Profil statistique d'un corpus de notes, entièrement dérivé des données.
///
/// Aucune liste codée en dur : ni mots vides, ni salutations, ni jours de la
/// semaine. Ce qui compte n'est pas <i>quel</i> mot c'est, mais <b>comment il
/// se distribue</b> — et cette question a la même réponse en français, en
/// anglais ou en allemand.
///
/// C'est ce qui rend le classement transposable à quelqu'un d'autre :
/// le vocabulaire change, la statistique non.
/// </summary>
public sealed class CorpusProfile
{
    private readonly Dictionary<string, int> _documentFrequency;
    private readonly Dictionary<string, int> _openingFrequency;

    public int NoteCount { get; }

    /// <summary>
    /// Mots trop répandus dans CE corpus pour distinguer quoi que ce soit.
    /// Ils émergent de la distribution : en français on retrouvera « faut »
    /// ou « coup », en anglais « just » ou « need », sans rien changer au code.
    /// </summary>
    public IReadOnlySet<string> StopWords { get; }

    /// <summary>
    /// Premiers mots récurrents. Une note qui s'ouvre sur un mot que beaucoup
    /// d'autres notes emploient en ouverture est très probablement un
    /// brouillon de message — que ce mot soit « salut », « hi » ou « hallo ».
    /// </summary>
    public IReadOnlySet<string> OpeningWords { get; }

    public CorpusProfile(
        IEnumerable<string> notes,
        double stopWordThreshold = 0.25,
        double openingThreshold = 0.04)
    {
        _documentFrequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        _openingFrequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int count = 0;
        foreach (var note in notes)
        {
            count++;
            var tokens = Tokenize(note).ToList();
            foreach (var token in tokens.Distinct(StringComparer.OrdinalIgnoreCase))
                _documentFrequency[token] = _documentFrequency.GetValueOrDefault(token) + 1;

            if (tokens.Count > 0)
                _openingFrequency[tokens[0]] = _openingFrequency.GetValueOrDefault(tokens[0]) + 1;
        }

        NoteCount = count;
        int stopCutoff = Math.Max(2, (int)(count * stopWordThreshold));
        int openCutoff = Math.Max(2, (int)(count * openingThreshold));

        StopWords = _documentFrequency.Where(kv => kv.Value >= stopCutoff)
            .Select(kv => kv.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        OpeningWords = _openingFrequency.Where(kv => kv.Value >= openCutoff)
            .Select(kv => kv.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Découpage sur les frontières de catégorie Unicode, sans présumer d'un
    /// alphabet. Les chiffres sont conservés : « v2 », « gpt4 » distinguent.
    /// </summary>
    public static IEnumerable<string> Tokenize(string text)
    {
        var buffer = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune)) buffer.Append(rune.ToString());
            else if (buffer.Length > 0) { if (buffer.Length >= 2) yield return Normalize(buffer.ToString()); buffer.Clear(); }
        }
        if (buffer.Length >= 2) yield return Normalize(buffer.ToString());
    }

    /// <summary>
    /// Minuscule et diacritiques retirés, pour que « Événement » et
    /// « evenement » soient le même mot. Neutre du point de vue de la langue.
    /// </summary>
    private static string Normalize(string token)
    {
        var decomposed = token.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    public int DocumentFrequency(string token)
        => _documentFrequency.GetValueOrDefault(Normalize(token));

    /// <summary>
    /// Rareté d'un mot dans le corpus. Élevée = discriminant. C'est la mesure
    /// qui fait remonter « streamelements » et enterre « faut », sans qu'on ait
    /// jamais nommé ni l'un ni l'autre.
    /// </summary>
    public double InverseDocumentFrequency(string token)
    {
        int df = DocumentFrequency(token);
        return df == 0 ? 0 : Math.Log((double)NoteCount / df);
    }

    /// <summary>Mots les plus caractéristiques d'une note, par score TF-IDF.</summary>
    public IEnumerable<(string Token, double Score)> DistinctiveTokens(string note, int take = 5)
    {
        var termFrequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in Tokenize(note))
            termFrequency[token] = termFrequency.GetValueOrDefault(token) + 1;

        return termFrequency
            .Where(kv => !StopWords.Contains(kv.Key)
                      && DocumentFrequency(kv.Key) >= 2
                      && !IsPurelyNumeric(kv.Key))
            // TF sous-linéaire : une note qui répète 40 fois le même mot n'est
            // pas 40 fois plus à propos. Sans ça, les notes longues écrasent
            // tout le classement.
            .Select(kv => (kv.Key, Score: (1 + Math.Log(kv.Value)) * InverseDocumentFrequency(kv.Key)))
            .OrderByDescending(x => x.Score)
            .Take(take);
    }

    /// <summary>
    /// Horaires, dates, montants : très fréquents dans des notes personnelles,
    /// et sans aucune valeur pour identifier un sujet.
    /// </summary>
    private static bool IsPurelyNumeric(string token)
    {
        foreach (var c in token) if (!char.IsDigit(c)) return false;
        return true;
    }

    /// <summary>Note s'ouvrant sur un mot d'ouverture récurrent du corpus.</summary>
    public bool OpensLikeCorrespondence(string note)
    {
        var first = Tokenize(note).FirstOrDefault();
        return first is not null && OpeningWords.Contains(first);
    }
}
