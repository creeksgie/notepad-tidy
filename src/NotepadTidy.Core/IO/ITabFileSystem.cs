namespace NotepadTidy.Core.IO;

/// <summary>
/// Disk access, behind an interface for a single reason: merging deletes
/// unrecoverable files, so it must be testable without touching a real
/// TabState folder.
/// </summary>
public interface ITabFileSystem
{
    bool FileExists(string path);
    byte[] ReadAllBytes(string path);
    void WriteAllBytes(string path, byte[] data);
    void DeleteFile(string path);
    IEnumerable<string> EnumerateFiles(string directory, string pattern);
    void CreateDirectory(string path);

    /// <summary>When the tab was first created, used to keep merges chronological.</summary>
    DateTime GetCreationTime(string path);

    /// <summary>Preserves creation time when copying, so backups stay faithful.</summary>
    void SetCreationTime(string path, DateTime when);
}

/// <summary>
/// Is Notepad running? Writing is forbidden while it is: Notepad keeps state
/// in memory and would overwrite our work on its next save.
/// </summary>
public interface INotepadGuard
{
    bool IsRunning { get; }
}
