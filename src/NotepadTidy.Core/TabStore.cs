using NotepadTidy.Core.IO;

namespace NotepadTidy.Core;

/// <summary>
/// Reads the TabState folder. Only reads and enumerates — the merging logic
/// lives in <see cref="TabMerger"/>.
/// </summary>
public sealed class TabStore(TabPaths paths, ITabFileSystem fs)
{
    public TabPaths Paths { get; } = paths;
    public ITabFileSystem FileSystem { get; } = fs;

    public static TabStore Default() => new(TabPaths.Default(), new WindowsTabFileSystem());

    /// <summary>Tab .bin files, excluding state records and .bak files.</summary>
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

    public TabRecord Read(Guid id)
    {
        var path = Paths.TabFile(id);
        var record = TabRecord.Parse(id, FileSystem.ReadAllBytes(path));

        // Creation time comes from the file system, not the format — Notepad
        // stores no date inside a tab. It is what keeps merges chronological.
        return new TabRecord
        {
            Id = record.Id,
            Status = record.Status,
            Text = record.Text,
            CursorStart = record.CursorStart,
            CursorEnd = record.CursorEnd,
            DeclaredLength = record.DeclaredLength,
            TextOffset = record.TextOffset,
            FileLength = record.FileLength,
            Created = FileSystem.GetCreationTime(path),
        };
    }

    /// <summary>
    /// Full copy of LocalState. Call this before any write: these notes exist
    /// nowhere else, which is the whole point of unsaved tabs.
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
                var copy = Path.Combine(target, Path.GetFileName(src));
                FileSystem.WriteAllBytes(copy, FileSystem.ReadAllBytes(src));
                // Carry the creation time over, otherwise a copy loses the
                // chronology and a restored backup would merge in a different
                // order than the original.
                FileSystem.SetCreationTime(copy, FileSystem.GetCreationTime(src));
                count++;
            }
        }
        return count;
    }
}
