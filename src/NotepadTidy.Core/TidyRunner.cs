using NotepadTidy.Core.IO;

namespace NotepadTidy.Core;

public sealed record TidyOutcome(
    bool Ran, int ThemesMerged, int NotesAbsorbed, int Refused, string? BackupPath, string Message);

/// <summary>
/// Executes a tidy pass: back up, then merge each planned group.
///
/// Shared by the CLI and the background service so both take exactly the same
/// safety path — there is no "quick route" that skips the guards.
/// </summary>
public sealed class TidyRunner(TabStore store, INotepadGuard guard)
{
    public const int BackupsKept = 3;

    /// <summary>
    /// Where backups go. Under LocalAppData rather than next to the notes, so a
    /// mistake in the TabState folder cannot take the backups with it.
    /// </summary>
    public static string DefaultBackupRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "notepad-tidy", "backups");

    public TidyPlan Plan() => TidyPlanner.Plan(store.ReadAll());

    public TidyOutcome Run(TidyPlan plan, string? backupRoot = null)
    {
        if (guard.IsRunning)
            return new TidyOutcome(false, 0, 0, 0, null, "Notepad is running.");

        if (!plan.HasWork)
            return new TidyOutcome(false, 0, 0, 0, null, "Nothing to merge.");

        // Backup before writing. Merging concatenates rather than drops text, so
        // a wrong theme is recoverable by hand. This covers the other case:
        // sources are deleted once the container is written, so a malformed
        // container would leave nothing to recover from.
        var root = backupRoot ?? DefaultBackupRoot;
        var backup = Path.Combine(root, DateTime.Now.ToString("yyyy-MM-dd_HHmmss"));
        store.Backup(backup);
        Rotate(root, BackupsKept);

        var merger = new TabMerger(store, guard);
        int merged = 0, absorbed = 0, refused = 0;

        foreach (var group in plan.Groups)
        {
            var result = merger.Merge(
                group.Container, group.Sources, TidyPlanner.Separator(group.Theme, DateTime.Now));

            if (result.Ok) { merged++; absorbed += result.Absorbed; }
            else refused++;
        }

        return new TidyOutcome(true, merged, absorbed, refused, backup,
            $"{merged} themes merged, {absorbed} notes absorbed, {refused} refused.");
    }

    /// <summary>Keeps only the most recent backups.</summary>
    public static void Rotate(string root, int keep)
    {
        if (!Directory.Exists(root)) return;
        foreach (var old in new DirectoryInfo(root).GetDirectories()
                                .OrderByDescending(d => d.Name).Skip(keep))
        {
            // A locked backup folder is not worth failing the run over.
            try { old.Delete(recursive: true); } catch (IOException) { }
        }
    }
}
