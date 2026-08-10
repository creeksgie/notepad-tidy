namespace NotepadTidy.Core;

/// <summary>
/// Discovers the vocabulary of themes from <b>shared prefixes</b>.
///
/// <para>Motivation, observed on a real corpus: when a user prepends
/// "#theme" to the first line, the theme usually ends up glued to the
/// existing content — "#themeHi there!", "#projectabout the columns". No
/// separator tells us where the theme ends.</para>
///
/// <para>But a theme is used more than once. Thirteen notes starting with
/// "#project…" all share that prefix, and only that. So there is nothing to
/// guess: it is enough to count.</para>
///
/// <para>No dictionary, no language, no model — only prefixes and counts.</para>
/// </summary>
public static class ThemeVocabulary
{
    public const int MinThemeLength = 2;
    public const int MaxThemeLength = 30;
    public const int MinNotesPerTheme = 2;

    /// <summary>
    /// Length beyond which a prefix earns no further score.
    ///
    /// Without this cap, a long prefix covering few notes beats the real
    /// theme: "theme// ===== configuration" (2 notes × 27) would outrank
    /// "theme" (8 notes × 5) and split one theme into three.
    /// </summary>
    public const int LengthCredit = 8;

    /// <summary>
    /// First-line text following the marker, or <c>null</c> when the note does
    /// not start with one.
    /// </summary>
    public static string? RawHeadingLine(string note)
    {
        // Splitting on '\n' alone is not enough: Notepad also writes lone '\r'
        // characters. Without this, the whole note is treated as a single line
        // and the extracted "theme" swallows the entire content.
        foreach (var line in note.Split('\r', '\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            return trimmed.Length >= 2 && trimmed[0] == NoteHeading.Marker
                ? trimmed[1..].TrimStart()
                : null;
        }
        return null;
    }

    /// <summary>
    /// Themes inferred from a corpus, most used first.
    ///
    /// Greedy: each round keeps the prefix explaining the most notes — longest
    /// wins ties — then drops the covered notes and starts over.
    /// </summary>
    public static IReadOnlyList<string> Discover(IEnumerable<string> notes)
    {
        var heads = notes.Select(RawHeadingLine)
                         .Where(h => !string.IsNullOrWhiteSpace(h))
                         .Select(h => h!.ToLowerInvariant())
                         .ToList();

        var themes = new List<string>();
        var remaining = new List<string>(heads);

        while (remaining.Count > 0)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var head in remaining)
            {
                int limit = Math.Min(MaxThemeLength, head.Length);
                for (int length = MinThemeLength; length <= limit; length++)
                {
                    var prefix = head[..length];
                    counts[prefix] = counts.GetValueOrDefault(prefix) + 1;
                }
            }

            // Score = note count × prefix length, with length capped.
            //
            // Sorting by count alone fails: a short prefix is always at least
            // as frequent as its extensions, so "pr" would outrank "project"
            // and merge two unrelated themes sharing those letters.
            //
            // Sorting by length alone fails too: two notes opening with the
            // same sentence would turn that sentence into a theme.
            //
            // The capped product arbitrates between the two failure modes.
            var best = counts
                .Where(kv => kv.Value >= MinNotesPerTheme)
                .OrderByDescending(kv => kv.Value * Math.Min(kv.Key.Length, LengthCredit))
                .ThenByDescending(kv => kv.Value)
                .ThenByDescending(kv => kv.Key.Length)
                .Select(kv => kv.Key)
                .FirstOrDefault();

            if (best is null) break;

            themes.Add(best);
            remaining.RemoveAll(h => h.StartsWith(best, StringComparison.Ordinal));
        }

        return themes;
    }

    /// <summary>
    /// Theme of a note, taken from a known vocabulary. The longest matching
    /// prefix wins, so "projectx" beats "project".
    /// </summary>
    public static string? Match(string note, IEnumerable<string> vocabulary)
    {
        var head = RawHeadingLine(note)?.ToLowerInvariant();
        if (head is null) return null;

        return vocabulary
            .Where(theme => head.StartsWith(theme, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(theme => theme.Length)
            .FirstOrDefault();
    }
}
