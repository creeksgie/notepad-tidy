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
        // The marker is not always on the very first line: notes often begin
        // with residue — a pasted HTML entity, a leftover fragment — and the
        // heading sits a couple of lines below.
        foreach (var line in NoteHeading.FirstNonEmptyLines(note, NoteHeading.HeadingSearchDepth))
            if (line.Length >= 2 && line[0] == NoteHeading.Marker)
                return line[1..].TrimStart();

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

        // Clean headings are themes by declaration, whatever their frequency.
        // Requiring two occurrences would drop a compound name used once.
        var themes = notes.Select(n => CleanHeading(n))
                          .Where(t => t is not null)
                          .Distinct(StringComparer.OrdinalIgnoreCase)
                          .Select(t => t!)
                          .ToList();

        var remaining = heads.Where(h => !themes.Any(
            t => h.StartsWith(t, StringComparison.OrdinalIgnoreCase))).ToList();

        while (remaining.Count > 0)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var head in remaining)
            {
                int limit = Math.Min(MaxThemeLength, head.Length);
                for (int length = MinThemeLength; length <= limit; length++)
                {
                    // A theme never ends on whitespace. Without this, "project "
                    // outscores "project" — it is one character longer for the
                    // same note count — and every theme ends up padded.
                    if (char.IsWhiteSpace(head[length - 1])) continue;

                    counts[head[..length]] = counts.GetValueOrDefault(head[..length]) + 1;
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
    /// A heading that needs no statistics: a single whitespace-delimited token,
    /// short enough to be a name rather than a sentence.
    ///
    /// <para>This takes precedence over prefix matching, and that ordering
    /// matters. "#project-mail" is a deliberate compound theme — the hyphen is
    /// how a user writes a multi-word name — but it also starts with "project",
    /// so prefix matching would silently swallow it into the wrong theme. A
    /// clean heading is an explicit statement of intent and must win.</para>
    ///
    /// <para>Glued headings ("#projectHi there!") are longer than a name and
    /// fall through to prefix discovery, which is what they need.</para>
    /// </summary>
    public static string? CleanHeading(string note, int maxLength = 30)
    {
        var head = RawHeadingLine(note);
        if (head is null) return null;

        var trimmed = head.Trim();
        if (trimmed.Length == 0 || trimmed.Length > maxLength) return null;

        // A single token: no internal whitespace. Hyphens are part of the name.
        foreach (var c in trimmed) if (char.IsWhiteSpace(c)) return null;

        return trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// Theme of a note, taken from a known vocabulary. The longest matching
    /// prefix wins, so "projectx" beats "project".
    /// </summary>
    public static string? Match(string note, IEnumerable<string> vocabulary)
    {
        // An explicit, clean heading always wins over statistics.
        var clean = CleanHeading(note);
        if (clean is not null) return clean;

        var head = RawHeadingLine(note)?.ToLowerInvariant();
        if (head is null) return null;

        return vocabulary
            .Where(theme => head.StartsWith(theme, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(theme => theme.Length)
            .FirstOrDefault();
    }
}
