using System.Diagnostics;

namespace NotepadTidy.Core.IO;

/// <summary>Real on-disk implementation.</summary>
public sealed class WindowsTabFileSystem : ITabFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    /// <summary>
    /// Reads through the exclusive lock Notepad holds on its open tabs. A plain
    /// File.ReadAllBytes fails for as long as Notepad is running.
    /// </summary>
    public byte[] ReadAllBytes(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[fs.Length];
        fs.ReadExactly(buffer);
        return buffer;
    }

    public void WriteAllBytes(string path, byte[] data) => File.WriteAllBytes(path, data);

    public void DeleteFile(string path) => File.Delete(path);

    public IEnumerable<string> EnumerateFiles(string directory, string pattern)
        => Directory.EnumerateFiles(directory, pattern);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public DateTime GetCreationTime(string path) => File.GetCreationTime(path);

    public void SetCreationTime(string path, DateTime when) => File.SetCreationTime(path, when);
}

public sealed class NotepadProcessGuard : INotepadGuard
{
    public bool IsRunning => Process.GetProcessesByName("Notepad").Length > 0;
}

/// <summary>
/// Neutral guard, for working on an isolated copy of the TabState folder.
/// Notepad only knows its own directory, so it cannot overwrite anything in a
/// sandbox and waiting for it would make no sense.
///
/// Only use this on a path verified NOT to be the real LocalState — see
/// <see cref="TabPaths.IsRealNotepadState"/>.
/// </summary>
public sealed class SandboxGuard : INotepadGuard
{
    public bool IsRunning => false;
}
