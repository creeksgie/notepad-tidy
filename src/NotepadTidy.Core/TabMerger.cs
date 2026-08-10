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
/// Merges notes into a container tab, then deletes the absorbed notes.
///
/// <para>The container is an <b>existing</b> tab: its GUID is already listed in
/// WindowState, which avoids having to touch that file — whose checksum has not
/// been reverse engineered. See docs/FORMAT.md §7.</para>
///
/// <para>This is the only place in the project that destroys data. All of its
/// dependencies are injected so that it can be tested end to end without ever
/// touching a real TabState folder.</para>
/// </summary>
public sealed class TabMerger(TabStore store, INotepadGuard guard)
{
    public static TabMerger Default() => new(TabStore.Default(), new NotepadProcessGuard());

    public MergeResult Merge(Guid container, IReadOnlyList<Guid> sources, string separator)
    {
        if (guard.IsRunning)
            return MergeResult.Refused(MergeRefusal.NotepadRunning,
                "Notepad is running — it would overwrite the write.");

        var containerRecord = store.Read(container);
        if (!containerRecord.IsSafeToRewrite)
            return MergeResult.Refused(MergeRefusal.ContainerUnreadable,
                $"Container is not safe to rewrite: {containerRecord.Status}");

        var texts = new List<string>();
        var absorbed = new List<Guid>();
        foreach (var id in sources)
        {
            if (!store.FileSystem.FileExists(store.Paths.TabFile(id))) continue;

            var record = store.Read(id);
            // Refuse as a whole rather than absorb part of the batch: a file we
            // misread is a note we risk losing.
            if (!record.IsSafeToRewrite)
                return MergeResult.Refused(MergeRefusal.SourceUnreadable,
                    $"Source {id} is not readable: {record.Status}");

            texts.Add(record.Text);
            absorbed.Add(id);
        }

        if (absorbed.Count == 0)
            return MergeResult.Refused(MergeRefusal.NoUsableSource, "No usable source.");

        var merged = containerRecord.Text + separator + string.Join(separator, texts);
        var bytes = TabRecord.Build(merged);

        // The writer re-reads its own output before writing it. If it is wrong,
        // we find out on an in-memory copy, never on real data.
        var verification = TabRecord.Parse(container, bytes);
        if (verification.Status != TabStatus.Ok || verification.Text != merged)
            return MergeResult.Refused(MergeRefusal.WriterSelfCheckFailed,
                "Writer self-check failed — nothing written.");

        // Second check: Notepad may have restarted while we were working.
        if (guard.IsRunning)
            return MergeResult.Refused(MergeRefusal.NotepadRunning,
                "Notepad came back during the operation.");

        store.FileSystem.WriteAllBytes(store.Paths.TabFile(container), bytes);

        foreach (var id in absorbed)
            store.FileSystem.DeleteFile(store.Paths.TabFile(id));

        // The container's state records still announce the old length and would
        // contradict the new content. Notepad recreates them.
        foreach (var path in store.Paths.StateRecordFiles(container))
            if (store.FileSystem.FileExists(path))
                store.FileSystem.DeleteFile(path);

        return MergeResult.Success(absorbed.Count, merged.Length, bytes.Length);
    }
}
