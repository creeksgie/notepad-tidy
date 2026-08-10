using NotepadTidy.Core.IO;

namespace NotepadTidy.Core;

/// <summary>
/// Lecture du TabState. Ne fait que lire et énumérer — la logique de fusion
/// vit dans <see cref="TabMerger"/>.
/// </summary>
public sealed class TabStore(TabPaths paths, ITabFileSystem fs)
{
    public TabPaths Paths { get; } = paths;
    public ITabFileSystem FileSystem { get; } = fs;

    public static TabStore Default() => new(TabPaths.Default(), new WindowsTabFileSystem());

    /// <summary>Les .bin d'onglets, hors enregistrements d'état et .bak.</summary>
    public IEnumerable<string> EnumerateTabFiles()
    {
        foreach (var path in FileSystem.EnumerateFiles(Paths.TabStateDir, "*.bin"))
        {
            if (TabPaths.IsStateRecord(path)) continue;
            if (TabPaths.TryTabId(path, out _)) yield return path;
        }
    }

    public IEnumerable<TabRecord> ReadAll()
    {
        foreach (var path in EnumerateTabFiles())
        {
            TabPaths.TryTabId(path, out var id);
            yield return Read(id);
        }
    }

    public TabRecord Read(Guid id) => TabRecord.Parse(id, FileSystem.ReadAllBytes(Paths.TabFile(id)));

    /// <summary>
    /// Copie intégrale de LocalState. À appeler avant toute écriture : ces
    /// notes n'existent nulle part ailleurs, c'est tout le principe des
    /// onglets non sauvegardés.
    /// </summary>
    public int Backup(string destination)
    {
        FileSystem.CreateDirectory(destination);
        int count = 0;
        foreach (var directory in new[] { Paths.TabStateDir, Paths.WindowStateDir })
        {
            if (!Directory.Exists(directory)) continue;
            var target = Path.Combine(destination, Path.GetFileName(directory));
            FileSystem.CreateDirectory(target);
            foreach (var src in FileSystem.EnumerateFiles(directory, "*"))
            {
                FileSystem.WriteAllBytes(
                    Path.Combine(target, Path.GetFileName(src)),
                    FileSystem.ReadAllBytes(src));
                count++;
            }
        }
        return count;
    }
}
