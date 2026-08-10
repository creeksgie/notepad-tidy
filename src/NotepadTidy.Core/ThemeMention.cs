namespace NotepadTidy.Core;

/// <summary>Outcome of looking for a known theme inside a note's body.</summary>
public readonly record struct ThemeMatch(string? Theme, int Hits, int RunnerUpHits, string Reason)
{
    public bool Found => Theme is not null;

    public static ThemeMatch None(string reason) => new(null, 0, 0, reason);
}

/// <summary>
/// Fallback for notes that carry no explicit heading: look for the name of an
/// <b>already declared</b> theme inside the body.
///
/// <para>The key property is that it never invents a theme. The vocabulary is
/// whatever the user wrote after a marker somewhere else, so filing stays in
/// the user's own words and in the user's own language.</para>
///
/// <para>It is deliberately conservative. A note that mentions two themes with
/// comparable weight is not filed at all: a wrong assignment is worse than an
/// unfiled note, because the user stops trusting the tool.</para>
/// </summary>
public static class ThemeMention
{
    /// <summary>
    /// Themes shorter than this are ignored as body mentions. Short names
    /// ("fl", "n", "p") collide with ordinary words far too often, and a
    /// mistaken match costs more than a missed one.
    /// </summary>
    public const int MinMentionLength = 4;

    /// <summary>
    /// How far ahead the winner must be. With a factor of 2, a theme cited
    /// twice beats one cited once, but two themes cited twice each are treated
    /// as genuinely ambiguous.
    /// </summary>
    public const double RequiredMargin = 2.0;

    public static ThemeMatch FindInBody(
        string note,
        IEnumerable<string> vocabulary,
        int minMentionLength = MinMentionLength,
        double margin = RequiredMargin)
    {
        // Whole-token comparison, so "play" does not match inside "display".
        // Tokenisation also folds case and diacritics.
        var tokens = CorpusProfile.Tokenize(note).ToList();
        if (tokens.Count == 0) return ThemeMatch.None("note vide");

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var theme in vocabulary.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (theme.Length < minMentionLength) continue;

            // The theme itself is tokenised: a multi-word theme only counts
            // when all of its tokens are present.
            var themeTokens = CorpusProfile.Tokenize(theme).ToList();
            if (themeTokens.Count == 0) continue;

            int hits = themeTokens.Count == 1
                ? tokens.Count(t => t.Equals(themeTokens[0], StringComparison.OrdinalIgnoreCase))
                : CountSequences(tokens, themeTokens);

            if (hits > 0) counts[theme] = hits;
        }

        if (counts.Count == 0) return ThemeMatch.None("aucun thème connu cité");

        var ranked = counts.OrderByDescending(kv => kv.Value)
                           .ThenByDescending(kv => kv.Key.Length)
                           .ToList();

        var best = ranked[0];
        int runnerUp = ranked.Count > 1 ? ranked[1].Value : 0;

        if (runnerUp > 0 && best.Value < runnerUp * margin)
            return new ThemeMatch(null, best.Value, runnerUp,
                $"ambigu : {best.Key} ({best.Value}) contre {ranked[1].Key} ({runnerUp})");

        return new ThemeMatch(best.Key, best.Value, runnerUp, "mention dans le corps");
    }

    private static int CountSequences(List<string> tokens, List<string> sequence)
    {
        int found = 0;
        for (int i = 0; i + sequence.Count <= tokens.Count; i++)
        {
            bool all = true;
            for (int k = 0; k < sequence.Count && all; k++)
                all = tokens[i + k].Equals(sequence[k], StringComparison.OrdinalIgnoreCase);
            if (all) found++;
        }
        return found;
    }
}
