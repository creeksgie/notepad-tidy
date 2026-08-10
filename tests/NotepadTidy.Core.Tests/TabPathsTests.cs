using NotepadTidy.Core;

namespace NotepadTidy.Core.Tests;

public class TabPathsTests
{
    [Fact]
    public void TabFile_UsesGuidAsFileName()
    {
        var paths = new TabPaths(@"X:\LocalState");
        var id = new Guid("28e8c91b-7b1e-4155-a174-f6b1b743dc06");

        Assert.Equal(
            Path.Combine(@"X:\LocalState", "TabState", "28e8c91b-7b1e-4155-a174-f6b1b743dc06.bin"),
            paths.TabFile(id));
    }

    [Fact]
    public void StateRecordFiles_ReturnsBothSequences()
    {
        var paths = new TabPaths(@"X:\LocalState");
        var id = Guid.Empty;

        var files = paths.StateRecordFiles(id).Select(p => Path.GetFileName(p)).ToArray();

        Assert.Equal(new[] { $"{id}.0.bin", $"{id}.1.bin" }, files);
    }

    [Theory]
    [InlineData("abc.0.bin", true)]
    [InlineData("abc.1.bin", true)]
    [InlineData("abc.bin", false)]
    [InlineData("ABC.0.BIN", true)]
    public void IsStateRecord_RecognisesJournalFiles(string name, bool expected)
        => Assert.Equal(expected, TabPaths.IsStateRecord(@"X:\TabState\" + name));

    [Fact]
    public void IsRealNotepadState_IsFalseForSandbox()
        => Assert.False(new TabPaths(@"C:\un\bac\a\sable").IsRealNotepadState);

    /// <summary>
    /// Le point sensible : --path ne doit jamais servir de contournement des
    /// protections. Si le chemin désigne le vrai dossier de Notepad — quelles
    /// que soient la casse ou la présence d'un séparateur final — les gardes
    /// liées au processus doivent rester actives.
    /// </summary>
    [Fact]
    public void IsRealNotepadState_IsTrueForRealPath()
        => Assert.True(TabPaths.Default().IsRealNotepadState);

    [Fact]
    public void IsRealNotepadState_IgnoresCaseAndTrailingSeparator()
    {
        var real = TabPaths.Default().LocalState;

        Assert.True(new TabPaths(real.ToUpperInvariant()).IsRealNotepadState);
        Assert.True(new TabPaths(real + Path.DirectorySeparatorChar).IsRealNotepadState);
    }

    [Fact]
    public void IsRealNotepadState_ResolvesRelativeTraversal()
    {
        // Un chemin détourné qui retombe sur le vrai dossier ne doit pas
        // passer pour un bac à sable.
        var real = TabPaths.Default().LocalState;
        var detoured = Path.Combine(real, "TabState", "..");

        Assert.True(new TabPaths(detoured).IsRealNotepadState);
    }
}
