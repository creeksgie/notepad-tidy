using NotepadTidy.Core;

namespace NotepadTidy.Core.Tests;

/// <summary>
/// Merging is the only code in the project that deletes files. These tests
/// exist above all to pin down the REFUSAL paths: they are what guarantees we
/// never overwrite a note we misread.
/// </summary>
public class TabMergerTests
{
    private const string Separator = "\r\n---\r\n";

    private static readonly Guid Container = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SourceA = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SourceB = new("33333333-3333-3333-3333-333333333333");

    private static (TabMerger merger, FakeTabFileSystem fs, TabPaths paths) Build(
        bool notepadRunning = false,
        params (Guid id, string text)[] tabs)
    {
        var paths = new TabPaths(@"X:\LocalState");
        var fs = new FakeTabFileSystem();
        foreach (var (id, text) in tabs)
            fs.Add(paths.TabFile(id), TabRecord.Build(text));

        var guard = notepadRunning ? new FakeNotepadGuard(true) : new FakeNotepadGuard(false, false);
        return (new TabMerger(new TabStore(paths, fs), guard), fs, paths);
    }

    [Fact]
    public void Merge_AppendsSourcesIntoContainer()
    {
        var (merger, fs, paths) = Build(
            tabs: [(Container, "container"), (SourceA, "note A"), (SourceB, "note B")]);

        var result = merger.Merge(Container, [SourceA, SourceB], Separator);

        Assert.True(result.Ok);
        Assert.Equal(2, result.Absorbed);

        var merged = TabRecord.Parse(Container, fs.ReadAllBytes(paths.TabFile(Container)));
        Assert.Equal(TabStatus.Ok, merged.Status);
        Assert.Equal($"container{Separator}note A{Separator}note B", merged.Text);
    }

    [Fact]
    public void Merge_DeletesAbsorbedSources()
    {
        var (merger, fs, paths) = Build(
            tabs: [(Container, "c"), (SourceA, "a"), (SourceB, "b")]);

        merger.Merge(Container, [SourceA, SourceB], Separator);

        Assert.False(fs.FileExists(paths.TabFile(SourceA)));
        Assert.False(fs.FileExists(paths.TabFile(SourceB)));
        Assert.True(fs.FileExists(paths.TabFile(Container)));
    }

    [Fact]
    public void Merge_DeletesStaleStateRecordsOfContainer()
    {
        // Those records replay the old length: leaving them would contradict
        // the freshly written content.
        var (merger, fs, paths) = Build(tabs: [(Container, "c"), (SourceA, "a")]);
        foreach (var path in paths.StateRecordFiles(Container)) fs.Add(path, [0x4E, 0x50]);

        merger.Merge(Container, [SourceA], Separator);

        foreach (var path in paths.StateRecordFiles(Container))
            Assert.False(fs.FileExists(path));
    }

    [Fact]
    public void Merge_RefusesWhenNotepadIsRunning()
    {
        var (merger, fs, _) = Build(notepadRunning: true,
            tabs: [(Container, "c"), (SourceA, "a")]);

        var result = merger.Merge(Container, [SourceA], Separator);

        Assert.False(result.Ok);
        Assert.Equal(MergeRefusal.NotepadRunning, result.Refusal);
        Assert.Empty(fs.Written);
        Assert.Empty(fs.Deleted);
    }

    [Fact]
    public void Merge_RefusesWhenNotepadReturnsMidOperation()
    {
        // Absent on the first check, back on the second: nothing must reach
        // the disk.
        var paths = new TabPaths(@"X:\LocalState");
        var fs = new FakeTabFileSystem();
        fs.Add(paths.TabFile(Container), TabRecord.Build("container"));
        fs.Add(paths.TabFile(SourceA), TabRecord.Build("note"));
        var merger = new TabMerger(new TabStore(paths, fs), new FakeNotepadGuard(false, true));

        var result = merger.Merge(Container, [SourceA], Separator);

        Assert.False(result.Ok);
        Assert.Equal(MergeRefusal.NotepadRunning, result.Refusal);
        Assert.Empty(fs.Written);
        Assert.Empty(fs.Deleted);
    }

    [Fact]
    public void Merge_RefusesFileBackedContainer()
    {
        var paths = new TabPaths(@"X:\LocalState");
        var fs = new FakeTabFileSystem();
        var fileBacked = TabRecord.Build("a real file");
        fileBacked[3] = 0x01;
        fs.Add(paths.TabFile(Container), fileBacked);
        fs.Add(paths.TabFile(SourceA), TabRecord.Build("note"));
        var merger = new TabMerger(new TabStore(paths, fs), new FakeNotepadGuard(false, false));

        var result = merger.Merge(Container, [SourceA], Separator);

        Assert.False(result.Ok);
        Assert.Equal(MergeRefusal.ContainerUnreadable, result.Refusal);
        Assert.Empty(fs.Written);
        Assert.Empty(fs.Deleted);
    }

    [Fact]
    public void Merge_RefusesEverythingWhenOneSourceIsUnreadable()
    {
        // The crucial point: a single doubtful source cancels the WHOLE
        // operation. A partial absorb would destroy notes for nothing.
        var paths = new TabPaths(@"X:\LocalState");
        var fs = new FakeTabFileSystem();
        fs.Add(paths.TabFile(Container), TabRecord.Build("container"));
        fs.Add(paths.TabFile(SourceA), TabRecord.Build("healthy note"));
        var corrupt = TabRecord.Build("corrupted note");
        corrupt[^1] ^= 0xFF;
        fs.Add(paths.TabFile(SourceB), corrupt);
        var merger = new TabMerger(new TabStore(paths, fs), new FakeNotepadGuard(false, false));

        var result = merger.Merge(Container, [SourceA, SourceB], Separator);

        Assert.False(result.Ok);
        Assert.Equal(MergeRefusal.SourceUnreadable, result.Refusal);
        Assert.Empty(fs.Deleted);
        Assert.True(fs.FileExists(paths.TabFile(SourceA)));
        Assert.True(fs.FileExists(paths.TabFile(SourceB)));
    }

    [Fact]
    public void Merge_RefusesUnknownFormatVariant()
    {
        // Simulates a Notepad update changing the header.
        var paths = new TabPaths(@"X:\LocalState");
        var fs = new FakeTabFileSystem();
        fs.Add(paths.TabFile(Container), TabRecord.Build("container"));
        var future = TabRecord.Build("note from a future version");
        future[4] = 0x02;
        fs.Add(paths.TabFile(SourceA), future);
        var merger = new TabMerger(new TabStore(paths, fs), new FakeNotepadGuard(false, false));

        var result = merger.Merge(Container, [SourceA], Separator);

        Assert.False(result.Ok);
        Assert.Equal(MergeRefusal.SourceUnreadable, result.Refusal);
        Assert.Empty(fs.Deleted);
    }

    [Fact]
    public void Merge_RefusesWhenNoSourceExists()
    {
        var (merger, fs, _) = Build(tabs: [(Container, "container")]);

        var result = merger.Merge(Container, [SourceA], Separator);

        Assert.False(result.Ok);
        Assert.Equal(MergeRefusal.NoUsableSource, result.Refusal);
        Assert.Empty(fs.Written);
    }

    [Fact]
    public void Merge_IsIdempotentAcrossRepeatedRuns()
    {
        // A second pass with no new source must not grow the container. This is
        // what prevents duplication on every cycle.
        var (merger, fs, paths) = Build(tabs: [(Container, "container"), (SourceA, "note")]);
        merger.Merge(Container, [SourceA], Separator);
        var afterFirst = TabRecord.Parse(Container, fs.ReadAllBytes(paths.TabFile(Container))).Text;

        var second = new TabMerger(new TabStore(paths, fs), new FakeNotepadGuard(false, false))
            .Merge(Container, [SourceA], Separator);

        Assert.False(second.Ok);
        Assert.Equal(afterFirst, TabRecord.Parse(Container, fs.ReadAllBytes(paths.TabFile(Container))).Text);
    }

    [Fact]
    public void Merge_PreservesLongUnicodeContentAcrossVarintGrowth()
    {
        // The container crosses the 16383-character boundary: the length varint
        // grows from two bytes to three and shifts the whole text block.
        var big = new string('é', 16_000);
        var (merger, fs, paths) = Build(tabs: [(Container, big), (SourceA, new string('à', 1_000))]);

        var result = merger.Merge(Container, [SourceA], Separator);

        Assert.True(result.Ok);
        var merged = TabRecord.Parse(Container, fs.ReadAllBytes(paths.TabFile(Container)));
        Assert.Equal(TabStatus.Ok, merged.Status);
        Assert.Equal(16_000 + Separator.Length + 1_000, merged.Text.Length);
        Assert.StartsWith(big, merged.Text, StringComparison.Ordinal);
    }
}
