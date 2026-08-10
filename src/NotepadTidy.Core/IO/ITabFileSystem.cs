namespace NotepadTidy.Core.IO;

/// <summary>
/// Accès disque, derrière une interface pour une seule raison : la fusion
/// supprime des fichiers irrécupérables, elle doit être testable sans toucher
/// au vrai TabState.
/// </summary>
public interface ITabFileSystem
{
    bool FileExists(string path);
    byte[] ReadAllBytes(string path);
    void WriteAllBytes(string path, byte[] data);
    void DeleteFile(string path);
    IEnumerable<string> EnumerateFiles(string directory, string pattern);
    void CreateDirectory(string path);
}

/// <summary>
/// Notepad tourne-t-il ? Toute écriture est interdite si oui : il maintient
/// l'état en mémoire et écraserait le travail à sa prochaine sauvegarde.
/// </summary>
public interface INotepadGuard
{
    bool IsRunning { get; }
}
