namespace NotepadTidy.Core;

/// <summary>One theme's merge: a container tab and the notes it absorbs.</summary>
public sealed record TidyGroup(
    string Theme,
    Guid Container,
    IReadOnlyList<Guid> Sources,
    int ContainerLength);

/// <summary>What a tidy pass would do, before anything is written.</summary>
public sealed record TidyPlan(
    int UsableNotes,
    int ThemeCount,
    int Unfiled,
    IReadOnlyList<TidyGroup> Groups)
{
    public int WouldAbsorb => Groups.Sum(g => g.Sources.Count);
    public int TabsAfter => UsableNotes - WouldAbsorb;
    public bool HasWork => Groups.Count > 0;
}

/// <summary>
/// Decides what to merge. Pure: no I/O, no clock, no process lookup — so the
/// same plan can be shown as a dry run and executed by the service, and both
/// are covered by the same tests.
/// </summary>
public static class TidyPlanner
{
    public static TidyPlan Plan(IEnumerable<TabRecord> allRecords)
    {
        // Tabs backed by a real file are excluded here, before anything else.
        // Their content never even reaches the theme vocabulary.
        var records = allRecords.Where(r => r.IsSafeToRewrite).ToList();
        var vocabulary = ThemeVocabulary.Discover(records.Select(r => r.Text));

        var groups = new Dictionary<string, List<TabRecord>>(StringComparer.OrdinalIgnoreCase);
        int unfiled = 0;

        foreach (var record in records)
        {
            var theme = ThemeVocabulary.Match(record.Text, vocabulary)
                     ?? ThemeMention.FindInBody(record.Text, vocabulary).Theme;
            if (theme is null) { unfiled++; continue; }
            if (!groups.TryGetValue(theme, out var list)) groups[theme] = list = [];
            list.Add(record);
        }

        var plan = groups
            .Where(g => g.Value.Count > 1)
            .Select(g =>
            {
                // The largest note becomes the container, so the bulk of the
                // content never moves and the smaller notes are appended to it.
                var container = g.Value.OrderByDescending(n => n.Text.Length)
                                       .ThenBy(n => n.Id).First();
                return new TidyGroup(
                    g.Key,
                    container.Id,
                    g.Value.Where(n => n.Id != container.Id).Select(n => n.Id).ToList(),
                    container.Text.Length);
            })
            .OrderByDescending(g => g.Sources.Count)
            .ThenBy(g => g.Theme)
            .ToList();

        return new TidyPlan(records.Count, groups.Count, unfiled, plan);
    }

    /// <summary>Separator inserted between the container and each absorbed note.</summary>
    public static string Separator(string theme, DateTime when)
        => $"{Environment.NewLine}{Environment.NewLine}--- {theme} · {when:yyyy-MM-dd} ---{Environment.NewLine}";
}
