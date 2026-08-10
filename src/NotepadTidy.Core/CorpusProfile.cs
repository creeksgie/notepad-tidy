using System.Globalization;
using System.Text;

namespace NotepadTidy.Core;

/// <summary>
/// Statistical profile of a corpus of notes, derived entirely from the data.
///
/// No hardcoded list: no stop words, no greetings, no weekday names. What
/// matters is not <i>which</i> word it is, but <b>how it is distributed</b> —
/// and that question has the same answer in English, French or German.
///
/// This is what makes filing transferable to someone else: the vocabulary
/// changes, the statistics do not.
/// </summary>
public sealed class CorpusProfile
{
    private readonly Dictionary<string, int> _documentFrequency;
    private readonly Dictionary<string, int> _openingFrequency;

    public int NoteCount { get; }

    /// <summary>
    /// Words too widespread in THIS corpus to distinguish anything. They emerge
    /// from the distribution: an English corpus yields "the" and "and", a French
    /// one yields "le" and "que", with no code change.
    /// </summary>
    public IReadOnlySet<string> StopWords { get; }

    /// <summary>
    /// Recurring first words. A note opening on a word that many other notes
    /// also open with is very likely a message draft — whether that word is
    /// "hi", "salut" or "hallo".
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
    /// Splits on Unicode category boundaries, assuming no particular alphabet.
    /// Digits are kept: "v2" and "http2" carry meaning.
    /// </summary>
    public static IEnumerable<string> Tokenize(string text, bool keepHyphens = false)
    {
        var buffer = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            // A hyphen only stays inside a word, never at its edges — that is
            // what makes "project-mail" one token and a dash on its own line
            // still a separator.
            bool joins = keepHyphens && rune.Value == '-' && buffer.Length > 0;
            if (Rune.IsLetterOrDigit(rune) || joins) buffer.Append(rune.ToString());
            else if (buffer.Length > 0) { if (buffer.Length >= 2) yield return Normalize(buffer.ToString().Trim('-')); buffer.Clear(); }
        }
        if (buffer.Length >= 2) yield return Normalize(buffer.ToString());
    }

    /// <summary>
    /// Lowercased and stripped of diacritics, so that "Événement" and
    /// "evenement" are the same word. Language-neutral.
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
    /// How rare a word is in the corpus. High means discriminating. This is the
    /// measure that surfaces project names and buries filler words, without
    /// either ever being named in code.
    /// </summary>
    public double InverseDocumentFrequency(string token)
    {
        int df = DocumentFrequency(token);
        return df == 0 ? 0 : Math.Log((double)NoteCount / df);
    }

    /// <summary>Most characteristic words of a note, by TF-IDF score.</summary>
    public IEnumerable<(string Token, double Score)> DistinctiveTokens(string note, int take = 5)
    {
        var termFrequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in Tokenize(note))
            termFrequency[token] = termFrequency.GetValueOrDefault(token) + 1;

        return termFrequency
            .Where(kv => !StopWords.Contains(kv.Key)
                      && DocumentFrequency(kv.Key) >= 2
                      && !IsPurelyNumeric(kv.Key))
            // Sublinear TF: a note repeating a word forty times is not forty
            // times more relevant. Without this, long notes crush the ranking.
            .Select(kv => (kv.Key, Score: (1 + Math.Log(kv.Value)) * InverseDocumentFrequency(kv.Key)))
            .OrderByDescending(x => x.Score)
            .Take(take);
    }

    /// <summary>
    /// Times, dates and amounts: very common in personal notes and worthless
    /// for identifying a subject.
    /// </summary>
    private static bool IsPurelyNumeric(string token)
    {
        foreach (var c in token) if (!char.IsDigit(c)) return false;
        return true;
    }

    /// <summary>Note opening on one of the corpus's recurring first words.</summary>
    public bool OpensLikeCorrespondence(string note)
    {
        var first = Tokenize(note).FirstOrDefault();
        return first is not null && OpeningWords.Contains(first);
    }
}
