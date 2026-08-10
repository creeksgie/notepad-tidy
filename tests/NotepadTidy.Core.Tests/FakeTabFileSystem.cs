using NotepadTidy.Core.IO;

namespace NotepadTidy.Core.Tests;

/// <summary>
/// Système de fichiers en mémoire. Permet de tester la fusion — qui supprime
/// des fichiers — sans jamais approcher un vrai TabState.
/// </summary>
public sealed class FakeTabFileSystem : ITabFileSystem
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Deleted { get; } = [];
    public List<string> Written { get; } = [];

    public void Add(string path, byte[] content) => _files[path] = content;

    public bool FileExists(string path) => _files.ContainsKey(path);

    public byte[] ReadAllBytes(string path) => _files.TryGetValue(path, out var data)
        ? data
        : throw new FileNotFoundException(path);

    public void WriteAllBytes(string path, byte[] data)
    {
        _files[path] = data;
        Written.Add(path);
    }

    public void DeleteFile(string path)
    {
        _files.Remove(path);
        Deleted.Add(path);
    }

    public IEnumerable<string> EnumerateFiles(string directory, string pattern)
        => _files.Keys.Where(k => Path.GetDirectoryName(k)?
            .Equals(directory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) == true);

    public void CreateDirectory(string path) { }
}

public sealed class FakeNotepadGuard : INotepadGuard
{
    private readonly Queue<bool> _answers;

    public FakeNotepadGuard(params bool[] answers) => _answers = new Queue<bool>(answers);

    /// <summary>
    /// Chaque interrogation consomme une réponse, ce qui permet de simuler un
    /// Notepad qui redémarre au milieu de l'opération.
    /// </summary>
    public bool IsRunning => _answers.Count > 0 ? _answers.Dequeue() : false;
}
