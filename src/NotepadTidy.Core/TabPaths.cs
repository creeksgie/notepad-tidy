namespace NotepadTidy.Core;

/// <summary>
/// Path resolution for the TabState folder. Pure, no I/O — so it is testable
/// without Windows and without Notepad installed.
/// </summary>
public sealed class TabPaths(string localState)
{
    public string LocalState { get; } = localState;

    public string TabStateDir => Path.Combine(LocalState, "TabState");
    public string WindowStateDir => Path.Combine(LocalState, "WindowState");

    public static TabPaths Default() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Packages", "Microsoft.WindowsNotepad_8wekyb3d8bbwe", "LocalState"));

    /// <summary>
    /// True when this path is Notepad's real folder. Decides whether the
    /// process-related protections apply: they are pointless on a sandbox and
    /// mandatory on the real folder.
    /// </summary>
    public bool IsRealNotepadState => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(LocalState)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(Default().LocalState)),
        StringComparison.OrdinalIgnoreCase);

    public string TabFile(Guid id) => Path.Combine(TabStateDir, $"{id}.bin");

    /// <summary>
    /// The .0.bin / .1.bin state records. These are not backups: they replay
    /// the content length and would contradict a rewritten tab.
    /// See docs/FORMAT.md §6.
    /// </summary>
    public IEnumerable<string> StateRecordFiles(Guid id)
    {
        yield return Path.Combine(TabStateDir, $"{id}.0.bin");
        yield return Path.Combine(TabStateDir, $"{id}.1.bin");
    }

    /// <summary>True for a .0.bin / .1.bin file, which are not tabs.</summary>
    public static bool IsStateRecord(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".0.bin", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".1.bin", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryTabId(string path, out Guid id)
        => Guid.TryParse(Path.GetFileNameWithoutExtension(path), out id);
}
