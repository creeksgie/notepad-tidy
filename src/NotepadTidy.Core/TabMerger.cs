using NotepadTidy.Core.IO;

namespace NotepadTidy.Core;

public enum MergeRefusal
{
    None,
    NotepadRunning,
    ContainerUnreadable,
    SourceUnreadable,
    NoUsableSource,
    WriterSelfCheckFailed,
}

public readonly record struct MergeResult(
    bool Ok, MergeRefusal Refusal, string Message, int Absorbed, int Chars, int Bytes)
{
    public static MergeResult Refused(MergeRefusal reason, string message)
        => new(false, reason, message, 0, 0, 0);

    public static MergeResult Success(int absorbed, int chars, int bytes)
        => new(true, MergeRefusal.None, "ok", absorbed, chars, bytes);
}

/// <summary>
/// Fusionne des notes dans un onglet conteneur, puis supprime les notes
/// absorbées.
///
/// Le conteneur est un onglet <b>existant</b> : son GUID est déjà indexé dans
/// le WindowState, ce qui évite d'avoir à toucher à ce fichier — dont le
/// checksum n'est pas résolu. Voir docs/FORMAT.md §7.
///
/// C'est le seul endroit du projet qui détruit des données. Toutes ses
/// dépendances sont injectées pour qu'il soit intégralement testable.
/// </summary>
public sealed class TabMerger(TabStore store, INotepadGuard guard)
{
    public static TabMerger Default() => new(TabStore.Default(), new NotepadProcessGuard());

    public MergeResult Merge(Guid container, IReadOnlyList<Guid> sources, string separator)
    {
        if (guard.IsRunning)
            return MergeResult.Refused(MergeRefusal.NotepadRunning,
                "Notepad tourne — il écraserait l'écriture.");

        var containerRecord = store.Read(container);
        if (!containerRecord.IsSafeToRewrite)
            return MergeResult.Refused(MergeRefusal.ContainerUnreadable,
                $"Conteneur non réécrivable : {containerRecord.Status}");

        var texts = new List<string>();
        var absorbed = new List<Guid>();
        foreach (var id in sources)
        {
            if (!store.FileSystem.FileExists(store.Paths.TabFile(id))) continue;

            var record = store.Read(id);
            // Refus en bloc plutôt qu'absorption partielle : un fichier mal
            // compris est une note qu'on risque de perdre.
            if (!record.IsSafeToRewrite)
                return MergeResult.Refused(MergeRefusal.SourceUnreadable,
                    $"Source {id} non lisible : {record.Status}");

            texts.Add(record.Text);
            absorbed.Add(id);
        }

        if (absorbed.Count == 0)
            return MergeResult.Refused(MergeRefusal.NoUsableSource, "Aucune source exploitable.");

        var merged = containerRecord.Text + separator + string.Join(separator, texts);
        var bytes = TabRecord.Build(merged);

        // Le writer relit son propre produit avant de l'écrire. S'il se trompe,
        // on le découvre sur une copie mémoire, jamais sur les données réelles.
        var verification = TabRecord.Parse(container, bytes);
        if (verification.Status != TabStatus.Ok || verification.Text != merged)
            return MergeResult.Refused(MergeRefusal.WriterSelfCheckFailed,
                "Auto-vérification du writer échouée — rien écrit.");

        // Second contrôle : Notepad a pu redémarrer pendant qu'on travaillait.
        if (guard.IsRunning)
            return MergeResult.Refused(MergeRefusal.NotepadRunning,
                "Notepad est revenu pendant l'opération.");

        store.FileSystem.WriteAllBytes(store.Paths.TabFile(container), bytes);

        foreach (var id in absorbed)
            store.FileSystem.DeleteFile(store.Paths.TabFile(id));

        // Les enregistrements d'état du conteneur annoncent l'ancienne longueur
        // et contrediraient le nouveau contenu. Notepad les recrée.
        foreach (var path in store.Paths.StateRecordFiles(container))
            if (store.FileSystem.FileExists(path))
                store.FileSystem.DeleteFile(path);

        return MergeResult.Success(absorbed.Count, merged.Length, bytes.Length);
    }
}
