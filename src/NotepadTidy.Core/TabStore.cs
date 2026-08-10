using System.Diagnostics;

namespace NotepadTidy.Core;

/// <summary>
/// Accès au dossier TabState de Notepad. Toutes les garanties de sécurité
/// des données passent par ici — voir docs/ARCHITECTURE.md.
/// </summary>
public sealed class TabStore(string? localStatePath = null)
{
    public string LocalState { get; } = localStatePath ?? DefaultLocalState();

    public string TabStateDir => Path.Combine(LocalState, "TabState");
    public string WindowStateDir => Path.Combine(LocalState, "WindowState");

    public static string DefaultLocalState() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Packages", "Microsoft.WindowsNotepad_8wekyb3d8bbwe", "LocalState");

    public bool Exists => Directory.Exists(TabStateDir);

    /// <summary>Notepad tourne-t-il ? Toute écriture est interdite si oui.</summary>
    public static bool IsNotepadRunning() => Process.GetProcessesByName("Notepad").Length > 0;

    /// <summary>
    /// Lecture tolérante au verrou exclusif que Notepad garde sur ses onglets
    /// ouverts. Un File.ReadAllBytes classique échoue tant qu'il tourne.
    /// </summary>
    public static byte[] ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[fs.Length];
        fs.ReadExactly(buffer);
        return buffer;
    }

    /// <summary>Les .bin d'onglets, en excluant les .0.bin/.1.bin et les .bak.</summary>
    public IEnumerable<string> EnumerateTabFiles()
    {
        foreach (var path in Directory.EnumerateFiles(TabStateDir, "*.bin"))
        {
            var name = Path.GetFileName(path);
            if (name.EndsWith(".0.bin", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.EndsWith(".1.bin", StringComparison.OrdinalIgnoreCase)) continue;
            if (Guid.TryParse(Path.GetFileNameWithoutExtension(path), out _)) yield return path;
        }
    }

    public IEnumerable<TabRecord> ReadAll()
    {
        foreach (var path in EnumerateTabFiles())
        {
            var id = Guid.Parse(Path.GetFileNameWithoutExtension(path));
            yield return TabRecord.Parse(id, ReadShared(path));
        }
    }

    public string PathFor(Guid id) => Path.Combine(TabStateDir, $"{id}.bin");

    /// <summary>
    /// Copie intégrale de LocalState. À appeler avant toute écriture — ces
    /// notes n'existent nulle part ailleurs.
    /// </summary>
    public int Backup(string destination)
    {
        Directory.CreateDirectory(destination);
        int count = 0;
        foreach (var src in Directory.EnumerateFiles(LocalState, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(LocalState, src);
            var dst = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.WriteAllBytes(dst, ReadShared(src));
            count++;
        }
        return count;
    }

    /// <summary>
    /// Fusionne le contenu de plusieurs notes dans un onglet conteneur, puis
    /// supprime les notes absorbées.
    ///
    /// Le conteneur est un onglet existant : son GUID est déjà indexé dans le
    /// WindowState, ce qui évite d'avoir à toucher à ce fichier (dont le
    /// checksum n'est pas résolu). Voir docs/FORMAT.md §7.
    /// </summary>
    public MergeResult Merge(Guid container, IReadOnlyList<Guid> sources, string separator)
    {
        if (IsNotepadRunning())
            return MergeResult.Refused("Notepad tourne — il écraserait l'écriture.");

        var containerRecord = TabRecord.Parse(container, ReadShared(PathFor(container)));
        if (!containerRecord.IsSafeToRewrite)
            return MergeResult.Refused($"Conteneur non réécrivable : {containerRecord.Status}");

        var parts = new List<string>();
        var absorbed = new List<Guid>();
        foreach (var id in sources)
        {
            var path = PathFor(id);
            if (!File.Exists(path)) continue;
            var record = TabRecord.Parse(id, ReadShared(path));
            // On refuse en bloc plutôt que d'absorber à moitié : un fichier
            // mal compris est une note qu'on risque de perdre.
            if (!record.IsSafeToRewrite)
                return MergeResult.Refused($"Source {id} non lisible : {record.Status}");
            parts.Add(record.Text);
            absorbed.Add(id);
        }

        if (parts.Count == 0) return MergeResult.Refused("Aucune source exploitable.");

        var merged = containerRecord.Text + separator + string.Join(separator, parts);
        var bytes = TabRecord.Build(merged);

        // Relecture du produit avant de l'écrire : si notre propre writer se
        // trompe, on le découvre ici et pas sur les données de l'utilisateur.
        var check = TabRecord.Parse(container, bytes);
        if (check.Status != TabStatus.Ok || check.Text != merged)
            return MergeResult.Refused("Auto-vérification du writer échouée — rien écrit.");

        if (IsNotepadRunning())
            return MergeResult.Refused("Notepad est revenu pendant l'opération.");

        File.WriteAllBytes(PathFor(container), bytes);
        foreach (var id in absorbed) File.Delete(PathFor(id));
        // Les enregistrements d'état du conteneur annoncent l'ancienne longueur
        // et contrediraient le nouveau contenu. Notepad les recrée. Voir §6.
        DeleteStateRecords(container);

        return MergeResult.Success(absorbed.Count, merged.Length, bytes.Length);
    }

    private void DeleteStateRecords(Guid id)
    {
        foreach (var suffix in new[] { ".0.bin", ".1.bin" })
        {
            var path = Path.Combine(TabStateDir, $"{id}{suffix}");
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

public readonly record struct MergeResult(bool Ok, string Message, int Absorbed, int Chars, int Bytes)
{
    public static MergeResult Refused(string why) => new(false, why, 0, 0, 0);
    public static MergeResult Success(int absorbed, int chars, int bytes) =>
        new(true, "ok", absorbed, chars, bytes);
}
