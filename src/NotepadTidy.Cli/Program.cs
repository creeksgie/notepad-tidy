using System.Text;
using NotepadTidy.Core;
using NotepadTidy.Core.IO;

Console.OutputEncoding = Encoding.UTF8;

// --path makes the tool work on an isolated copy instead of the real Notepad
// folder. This is the recommended mode for any experiment.
var pathOption = ReadOption(args, "--path");
var paths = pathOption is null ? TabPaths.Default() : new TabPaths(pathOption);
var store = new TabStore(paths, new WindowsTabFileSystem());

// In a sandbox Notepad cannot overwrite anything, so waiting for it to close
// would be meaningless. On the real folder the guard is mandatory.
INotepadGuard guard = paths.IsRealNotepadState
    ? new NotepadProcessGuard()
    : new SandboxGuard();

if (!Directory.Exists(store.Paths.TabStateDir))
{
    Console.Error.WriteLine($"TabState not found: {store.Paths.TabStateDir}");
    return 2;
}

if (!paths.IsRealNotepadState)
    Console.WriteLine($"[sandbox] {paths.LocalState}{Environment.NewLine}");

var command = args.Length > 0 && !args[0].StartsWith("--") ? args[0].ToLowerInvariant() : "stats";

// The service logs its failures and stays alive; the CLI has no such net. A
// stack trace is not a diagnosis for someone whose notes are at stake, so
// unexpected errors are reported as one line and a distinct exit code.
try
{
    return Dispatch();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"nptidy: {ex.GetType().Name}: {ex.Message}");
    Console.Error.WriteLine("Nothing was written. Report this with the command you ran.");
    return 5;
}

int Dispatch()
{
switch (command)
{
    case "stats": return Stats(store, guard);
    case "analyze": return Analyze(store);
    case "themes": return Themes(store);
    case "tidy": return Tidy(store, guard, args);
    case "list": return List(store);
    case "dump": return Dump(store, args);
    case "backup": return Backup(store, args);
    case "restore": return Restore(store, guard, args);
    case "merge": return Merge(store, guard, args);
    default:
        Console.WriteLine("""
            nptidy — tidy up your Notepad tabs

              stats                       health of the TabState folder
              analyze                     measure the signals in your corpus
              themes                      group notes by theme
              list                        list tabs with a preview
              dump <guid>                 content and header of one tab
              backup <folder>             copy TabState and WindowState
              merge <target> <src...>     merge notes into a container tab

            Options:
              --path <folder>             work on an isolated copy instead of
                                          the real Notepad folder
              --apply                     actually perform the merge

            Without --apply, merge writes nothing.

            To try it safely:
              nptidy backup C:\sandbox
              nptidy list --path C:\sandbox
            """);
        return 1;
}
}

static string? ReadOption(string[] args, string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static int Stats(TabStore store, INotepadGuard guard)
{
    var records = store.ReadAll().ToList();
    var byStatus = records.GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.Count());

    Console.WriteLine($"TabState : {store.Paths.TabStateDir}");
    Console.WriteLine($"Notepad running : {(guard.IsRunning ? "YES (writing disabled)" : "no")}");
    Console.WriteLine($"Tabs : {records.Count}");
    Console.WriteLine();

    foreach (var status in Enum.GetValues<TabStatus>())
    {
        if (!byStatus.TryGetValue(status, out int n)) continue;
        var note = status switch
        {
            TabStatus.Ok => "safe to rewrite",
            TabStatus.FileBacked => "real files — do not touch",
            TabStatus.LayoutMismatch => "layout not verified — skipped",
            TabStatus.UnknownVariant => "unknown variant — Notepad may have changed",
            TabStatus.BadCrc => "corrupted",
            _ => "",
        };
        Console.WriteLine($"  {status,-16} {n,4}   {note}");
    }

    var safe = records.Where(r => r.IsSafeToRewrite).ToList();
    Console.WriteLine();
    Console.WriteLine($"Usable : {safe.Count} tabs, {safe.Sum(r => r.Text.Length):N0} characters");
    return 0;
}

/// <summary>
/// Measures the usable signals in a corpus without printing any content.
/// This is the decision tool: it says which filing strategies have material
/// to work with, before a single line of classifier is written.
/// </summary>
static int Analyze(TabStore store)
{
    var notes = store.ReadAll().Where(r => r.IsSafeToRewrite).Select(r => r.Text).ToList();
    if (notes.Count == 0) { Console.Error.WriteLine("no usable note"); return 2; }

    int drafts = notes.Count(NoteSignals.LooksLikeMessageDraft);
    int epistolary = notes.Count(NoteSignals.HasEpistolaryMarker);
    int technical = notes.Count(NoteSignals.HasTechnicalMarker);
    int withUrl = notes.Count(n => NoteSignals.UrlCount(n) > 0);
    int linkDumps = notes.Count(NoteSignals.IsMostlyLinks);

    Console.WriteLine($"Notes analysed : {notes.Count}");
    Console.WriteLine();
    Console.WriteLine("— Type detectable without a model —");
    Console.WriteLine($"  message draft (opening greeting)      {drafts,4}  {Pct(drafts, notes.Count)}");
    Console.WriteLine($"  correspondence register (politeness)  {epistolary,4}  {Pct(epistolary, notes.Count)}");
    Console.WriteLine($"  technical markers                     {technical,4}  {Pct(technical, notes.Count)}");
    Console.WriteLine($"  contains at least one URL             {withUrl,4}  {Pct(withUrl, notes.Count)}");
    Console.WriteLine($"  almost entirely links                 {linkDumps,4}  {Pct(linkDumps, notes.Count)}");

    // How many notes already declare their theme on the first line? This is
    // the only language-independent filing mechanism.
    var explicitly = notes.Select(NoteHeading.ExtractExplicit).Where(h => h is not null).ToList();
    var guessed = notes.Select(NoteHeading.GuessImplicit).Where(h => h is not null).ToList();
    Console.WriteLine();
    Console.WriteLine("— Heading on the first line —");
    Console.WriteLine($"  explicit heading (leading #)          {explicitly.Count,4}  {Pct(explicitly.Count, notes.Count)}");
    Console.WriteLine($"  first line GUESSED as a heading       {guessed.Count,4}  {Pct(guessed.Count, notes.Count)}");
    if (guessed.Count > 0)
    {
        Console.WriteLine("  what guessing would propose as themes:");
        foreach (var h in guessed.Select(h => NoteHeading.Normalize(h!))
                                 .Where(h => h.Length > 0)
                                 .Distinct(StringComparer.OrdinalIgnoreCase).Take(8))
            Console.WriteLine($"    {h}");
        Console.WriteLine("  (inspect these: guessing yields mostly false positives)");
    }

    var lengths = notes.Select(n => n.Length).OrderBy(x => x).ToList();
    Console.WriteLine();
    Console.WriteLine("— Lengths —");
    Console.WriteLine($"  median {lengths[lengths.Count / 2]}  mean {lengths.Average():N0}  max {lengths[^1]}");
    Console.WriteLine($"  notes under 80 characters : {lengths.Count(l => l < 80)}  (little semantic signal)");

    // Nothing below uses a hardcoded list: everything is derived from the
    // corpus, so it transfers to another user and another language.
    var profile = new CorpusProfile(notes);

    Console.WriteLine();
    Console.WriteLine($"— Stop words DERIVED from the corpus ({profile.StopWords.Count}) —");
    Console.WriteLine("  " + string.Join(", ", profile.StopWords
        .OrderByDescending(profile.DocumentFrequency).Take(14)));

    Console.WriteLine();
    Console.WriteLine($"— Opening words DERIVED ({profile.OpeningWords.Count}) —");
    Console.WriteLine("  " + (profile.OpeningWords.Count > 0
        ? string.Join(", ", profile.OpeningWords.OrderByDescending(profile.DocumentFrequency))
        : "none"));
    Console.WriteLine($"  notes opening that way : {notes.Count(profile.OpensLikeCorrespondence)}");

    Console.WriteLine();
    Console.WriteLine("— Most distinctive words, by TF-IDF —");
    var best = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    foreach (var note in notes)
        foreach (var (token, score) in profile.DistinctiveTokens(note, 6))
            if (score > best.GetValueOrDefault(token)) best[token] = score;
    foreach (var (token, score) in best.OrderByDescending(k => k.Value).Take(20))
        Console.WriteLine($"  {token,-24} {score,6:N1}   ({profile.DocumentFrequency(token)} notes)");

    var proper = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    foreach (var note in notes)
        foreach (var p in NoteSignals.ProperNouns(note).Distinct(StringComparer.OrdinalIgnoreCase))
            proper[p] = proper.GetValueOrDefault(p) + 1;

    Console.WriteLine();
    Console.WriteLine("— Recurring proper nouns (project / person candidates) —");
    foreach (var (token, count) in proper.Where(k => k.Value >= 2)
                                         .OrderByDescending(k => k.Value).Take(25))
        Console.WriteLine($"  {token,-24} {count,3} notes");
    Console.WriteLine($"  ... {proper.Count(k => k.Value == 1)} proper nouns appear in a single note");

    var domains = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    foreach (var note in notes)
        foreach (var d in NoteSignals.Domains(note).Distinct(StringComparer.OrdinalIgnoreCase))
            domains[d] = domains.GetValueOrDefault(d) + 1;

    if (domains.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("— Domains mentioned —");
        foreach (var (d, c) in domains.OrderByDescending(k => k.Value).Take(12))
            Console.WriteLine($"  {d,-32} {c,3} notes");
    }

    return 0;

    static string Pct(int n, int total) => $"({100.0 * n / total,5:N1} %)";
}

/// <summary>
/// Groups notes by explicit heading. Grouping is insensitive to case:
/// "#Project" and "#project" denote the same theme.
/// </summary>
static int Themes(TabStore store)
{
    var records = store.ReadAll().Where(r => r.IsSafeToRewrite).ToList();

    // The theme is often glued to the content ("#themeHi!"), so it cannot be
    // delimited note by note. It is inferred from shared prefixes instead.
    var vocabulary = ThemeVocabulary.Discover(records.Select(r => r.Text));

    var groups = new Dictionary<string, List<Guid>>(StringComparer.OrdinalIgnoreCase);
    var untitled = new List<Guid>();
    var byMention = new List<(Guid Id, string Theme, string Why)>();

    foreach (var record in records)
    {
        var theme = ThemeVocabulary.Match(record.Text, vocabulary);

        // No heading: look for a theme the user ALREADY declared elsewhere.
        // A new one is never invented — the vocabulary stays theirs.
        if (theme is null)
        {
            var mention = ThemeMention.FindInBody(record.Text, vocabulary);
            if (!mention.Found) { untitled.Add(record.Id); continue; }
            theme = mention.Theme!;
            byMention.Add((record.Id, theme, $"{mention.Hits} mention(s)"));
        }

        if (!groups.TryGetValue(theme, out var list)) groups[theme] = list = [];
        list.Add(record.Id);
    }

    int titled = records.Count - untitled.Count - byMention.Count;
    Console.WriteLine($"Usable notes : {records.Count}");
    Console.WriteLine($"  explicit # heading   : {titled}");
    Console.WriteLine($"  filed by mention     : {byMention.Count}");
    Console.WriteLine($"  unfiled              : {untitled.Count}");
    if (byMention.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("— Filed by mention in the body —");
        foreach (var (id, theme, why) in byMention)
            Console.WriteLine($"  {id.ToString()[..8]}  → {theme,-16} ({why})");
    }

    Console.WriteLine();
    Console.WriteLine($"— {groups.Count} themes —");
    foreach (var (theme, ids) in groups.OrderByDescending(g => g.Value.Count).ThenBy(g => g.Key))
        Console.WriteLine($"  {theme,-28} {ids.Count,3} note{(ids.Count > 1 ? "s" : "")}");

    if (untitled.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("— Unfiled notes (first-line preview) —");
        foreach (var id in untitled.Take(20))
        {
            var text = records.First(r => r.Id == id).Text;
            var first = text.Split('\r', '\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
            if (first.Length > 52) first = first[..52] + "…";
            Console.WriteLine($"  {id.ToString()[..8]}  {first}");
        }
    }
    return 0;
}

/// <summary>
/// The full job: group every note by theme and merge each group into one
/// container tab. Writes nothing without --apply.
/// </summary>
static int Tidy(TabStore store, INotepadGuard guard, string[] args)
{
    bool apply = args.Contains("--apply");

    if (apply && guard.IsRunning)
    {
        Console.Error.WriteLine("Notepad is running. Close it first — it would overwrite the work.");
        return 3;
    }

    // Same planner and same runner as the background service: there is no
    // shortcut path that skips a guard.
    var runner = new TidyRunner(store, guard);
    var plan = runner.Plan();

    Console.WriteLine($"{plan.UsableNotes} usable notes, {plan.ThemeCount} themes, {plan.Unfiled} unfiled");
    Console.WriteLine($"{plan.Groups.Count} themes have more than one note and would be merged.");
    Console.WriteLine();

    foreach (var group in plan.Groups)
        Console.WriteLine($"  {group.Theme,-20} → container {group.Container.ToString()[..8]} " +
                          $"({group.ContainerLength} chars, absorbs {group.Sources.Count})");

    Console.WriteLine();
    Console.WriteLine($"Tabs before : {plan.UsableNotes}");
    Console.WriteLine($"Tabs after  : {plan.TabsAfter}");

    if (!apply)
    {
        Console.WriteLine();
        Console.WriteLine("Dry run — nothing was written. Add --apply to perform it.");
        return 0;
    }

    var outcome = runner.Run(plan);
    Console.WriteLine();
    if (outcome.BackupPath is not null) Console.WriteLine($"Backup → {outcome.BackupPath}");
    Console.WriteLine(outcome.Message);
    return outcome.Refused > 0 ? 4 : 0;
}

/// <summary>
/// Puts a backup back in place. This is the command that makes every other one
/// safe, so it is deliberately blunt: Notepad must be closed, the backup must
/// parse, and the current state is itself backed up first.
/// </summary>
static int Restore(TabStore store, INotepadGuard guard, string[] args)
{
    if (args.Length < 2) { Console.Error.WriteLine("usage: nptidy restore <backup folder>"); return 1; }

    var source = args[1];
    var sourceTabs = Path.Combine(source, "TabState");
    if (!Directory.Exists(sourceTabs))
    {
        Console.Error.WriteLine($"no TabState folder in {source}");
        return 2;
    }

    if (guard.IsRunning)
    {
        Console.Error.WriteLine("Notepad is running. Close it first — it would overwrite the restore.");
        return 3;
    }

    // Read the backup through the parser before trusting it. Restoring a
    // corrupted backup over live notes would turn a bad day into a disaster.
    var backupStore = new TabStore(new TabPaths(source), store.FileSystem);
    var usable = backupStore.ReadAll().Count(r => r.IsSafeToRewrite);
    if (usable == 0)
    {
        Console.Error.WriteLine("that backup contains no readable note — refusing to restore");
        return 4;
    }
    Console.WriteLine($"Backup checked: {usable} readable notes.");

    // The current state goes into a backup of its own. A restore is itself an
    // operation one may want to undo.
    var safety = Path.Combine(TidyRunner.DefaultBackupRoot,
        "before-restore_" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss"));
    Console.WriteLine($"Current state saved to {safety} ({store.Backup(safety)} files).");

    foreach (var existing in store.FileSystem.EnumerateFiles(store.Paths.TabStateDir, "*"))
        store.FileSystem.DeleteFile(existing);

    int restored = 0;
    foreach (var file in store.FileSystem.EnumerateFiles(sourceTabs, "*"))
    {
        var target = Path.Combine(store.Paths.TabStateDir, Path.GetFileName(file));
        store.FileSystem.WriteAllBytes(target, store.FileSystem.ReadAllBytes(file));
        store.FileSystem.SetCreationTime(target, store.FileSystem.GetCreationTime(file));
        restored++;
    }

    Console.WriteLine($"Restored {restored} files. Reopen Notepad to check.");
    return 0;
}

static int List(TabStore store)
{
    foreach (var r in store.ReadAll().OrderByDescending(r => r.Text.Length))
    {
        var preview = r.Text.ReplaceLineEndings(" ").Trim();
        if (preview.Length > 60) preview = preview[..60] + "…";
        Console.WriteLine($"{(r.IsSafeToRewrite ? " " : "!")} {r.Id}  {r.Text.Length,6}c  {r.Status,-16}  {preview}");
    }
    return 0;
}

static int Dump(TabStore store, string[] args)
{
    if (args.Length < 2 || !Guid.TryParse(args[1], out var id))
    {
        Console.Error.WriteLine("usage: nptidy dump <guid>");
        return 1;
    }
    if (!store.FileSystem.FileExists(store.Paths.TabFile(id)))
    {
        Console.Error.WriteLine($"not found: {store.Paths.TabFile(id)}");
        return 2;
    }

    var record = store.Read(id);
    Console.WriteLine($"GUID       {record.Id}");
    Console.WriteLine($"Status     {record.Status}");
    Console.WriteLine($"Size       {record.FileLength} bytes");
    Console.WriteLine($"Length     {record.DeclaredLength} characters");
    Console.WriteLine($"Text at    offset {record.TextOffset}");
    Console.WriteLine($"Caret      {record.CursorStart}/{record.CursorEnd}");
    Console.WriteLine(new string('-', 60));
    Console.WriteLine(record.Text);
    Console.WriteLine(new string('-', 60));
    return 0;
}

static int Backup(TabStore store, string[] args)
{
    if (args.Length < 2) { Console.Error.WriteLine("usage: nptidy backup <folder>"); return 1; }
    Console.WriteLine($"{store.Backup(args[1])} files copied to {args[1]}");
    return 0;
}

static int Merge(TabStore store, INotepadGuard guard, string[] args)
{
    bool apply = args.Contains("--apply");
    // Only GUIDs are kept: options and their values are discarded.
    var ids = args.Skip(1)
                  .Select(a => Guid.TryParse(a, out var g) ? g : Guid.Empty)
                  .Where(g => g != Guid.Empty).ToList();

    if (ids.Count < 2)
    {
        Console.Error.WriteLine("usage: nptidy merge <target> <source...> [--apply]");
        return 1;
    }

    var container = ids[0];
    var sources = ids.Skip(1).ToList();
    var separator = $"{Environment.NewLine}{Environment.NewLine}--- merged on {DateTime.Now:yyyy-MM-dd} ---{Environment.NewLine}";

    if (!apply)
    {
        var target = store.Read(container);
        Console.WriteLine($"[dry run] container {container} — {target.Status}, {target.Text.Length} characters");
        int total = target.Text.Length;
        foreach (var id in sources)
        {
            var r = store.Read(id);
            Console.WriteLine($"  + {id}  {r.Text.Length,6}c  {r.Status}");
            total += r.Text.Length + separator.Length;
        }
        Console.WriteLine($"  result : {total} characters, {sources.Count} notes absorbed");
        Console.WriteLine("Nothing was written. Add --apply to perform the merge.");
        return 0;
    }

    var result = new TabMerger(store, guard).Merge(container, sources, separator);
    if (!result.Ok)
    {
        Console.Error.WriteLine($"refused ({result.Refusal}): {result.Message}");
        return 3;
    }

    Console.WriteLine($"Merged : {result.Absorbed} notes, {result.Chars} characters, {result.Bytes} bytes");
    return 0;
}
