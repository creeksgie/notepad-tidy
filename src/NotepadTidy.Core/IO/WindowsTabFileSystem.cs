using System.Diagnostics;

namespace NotepadTidy.Core.IO;

/// <summary>Implémentation réelle sur le disque.</summary>
public sealed class WindowsTabFileSystem : ITabFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    /// <summary>
    /// Lecture tolérante au verrou exclusif que Notepad garde sur ses onglets
    /// ouverts. Un File.ReadAllBytes échoue tant qu'il tourne.
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
}

public sealed class NotepadProcessGuard : INotepadGuard
{
    public bool IsRunning => Process.GetProcessesByName("Notepad").Length > 0;
}
