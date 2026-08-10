using NotepadTidy.Core;

namespace NotepadTidy.Core.Tests;

/// <summary>
/// La fusion est le seul code du projet qui supprime des fichiers. Ces tests
/// existent surtout pour verrouiller les cas de REFUS : c'est eux qui
/// garantissent qu'on ne détruit pas une note mal comprise.
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
            tabs: [(Container, "conteneur"), (SourceA, "note A"), (SourceB, "note B")]);

        var result = merger.Merge(Container, [SourceA, SourceB], Separator);

        Assert.True(result.Ok);
        Assert.Equal(2, result.Absorbed);

        var merged = TabRecord.Parse(Container, fs.ReadAllBytes(paths.TabFile(Container)));
        Assert.Equal(TabStatus.Ok, merged.Status);
        Assert.Equal($"conteneur{Separator}note A{Separator}note B", merged.Text);
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
        // Ces enregistrements rejouent l'ancienne longueur : les laisser
        // reviendrait à contredire le contenu fraîchement écrit.
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
        // Notepad absent au premier contrôle, revenu au second : rien ne doit
        // partir sur le disque.
        var paths = new TabPaths(@"X:\LocalState");
        var fs = new FakeTabFileSystem();
        fs.Add(paths.TabFile(Container), TabRecord.Build("conteneur"));
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
        var fileBacked = TabRecord.Build("un vrai fichier");
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
        // Le point crucial : une seule source douteuse annule TOUTE l'opération.
        // Absorber à moitié laisserait des notes détruites sans contrepartie.
        var paths = new TabPaths(@"X:\LocalState");
        var fs = new FakeTabFileSystem();
        fs.Add(paths.TabFile(Container), TabRecord.Build("conteneur"));
        fs.Add(paths.TabFile(SourceA), TabRecord.Build("note saine"));
        var corrupt = TabRecord.Build("note corrompue");
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
        // Simule une mise à jour de Notepad qui change l'en-tête.
        var paths = new TabPaths(@"X:\LocalState");
        var fs = new FakeTabFileSystem();
        fs.Add(paths.TabFile(Container), TabRecord.Build("conteneur"));
        var future = TabRecord.Build("note d'une version future");
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
        var (merger, fs, _) = Build(tabs: [(Container, "conteneur")]);

        var result = merger.Merge(Container, [SourceA], Separator);

        Assert.False(result.Ok);
        Assert.Equal(MergeRefusal.NoUsableSource, result.Refusal);
        Assert.Empty(fs.Written);
    }

    [Fact]
    public void Merge_IsIdempotentAcrossRepeatedRuns()
    {
        // Deuxième passe sans nouvelle source : le conteneur ne doit pas
        // enfler. C'est la garantie qui empêche la duplication à chaque cycle.
        var (merger, fs, paths) = Build(tabs: [(Container, "conteneur"), (SourceA, "note")]);
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
        // Le conteneur franchit la frontière des 16383 caractères : le varint
        // de longueur passe de 2 à 3 octets et tout le bloc de texte se décale.
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
