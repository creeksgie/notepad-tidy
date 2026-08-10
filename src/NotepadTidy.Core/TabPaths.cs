namespace NotepadTidy.Core;

/// <summary>
/// Résolution des chemins du TabState. Pure, sans I/O — donc testable sans
/// Windows et sans Notepad installé.
/// </summary>
public sealed class TabPaths(string localState)
{
    public string LocalState { get; } = localState;

    public string TabStateDir => Path.Combine(LocalState, "TabState");
    public string WindowStateDir => Path.Combine(LocalState, "WindowState");

    public static TabPaths Default() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Packages", "Microsoft.WindowsNotepad_8wekyb3d8bbwe", "LocalState"));

    public string TabFile(Guid id) => Path.Combine(TabStateDir, $"{id}.bin");

    /// <summary>
    /// Les enregistrements d'état .0.bin / .1.bin. Ce ne sont pas des
    /// sauvegardes : ils rejouent la longueur du contenu et contrediraient un
    /// onglet réécrit. Voir docs/FORMAT.md §6.
    /// </summary>
    public IEnumerable<string> StateRecordFiles(Guid id)
    {
        yield return Path.Combine(TabStateDir, $"{id}.0.bin");
        yield return Path.Combine(TabStateDir, $"{id}.1.bin");
    }

    /// <summary>Vrai pour un .0.bin / .1.bin, qui ne sont pas des onglets.</summary>
    public static bool IsStateRecord(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".0.bin", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".1.bin", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryTabId(string path, out Guid id)
        => Guid.TryParse(Path.GetFileNameWithoutExtension(path), out id);
}
