using NotepadTidy.Core;

namespace NotepadTidy.Core.Tests;

/// <summary>
/// The explicit heading is the only filing mechanism that is both
/// administration-free and language-independent: the category is declared by
/// the user rather than guessed by the code.
/// </summary>
public class NoteHeadingTests
{
    [Theory]
    [InlineData("# project beta\r\nlink for the testers", "project beta")]
    [InlineData("#Alex\r\nreply about the quote", "Alex")]
    [InlineData("  # server deploy \r\nsystemctl restart", "server deploy")]
    public void ExtractExplicit_ReadsMarkedHeading(string note, string expected)
        => Assert.Equal(expected, NoteHeading.ExtractExplicit(note));

    [Theory]
    [InlineData("project beta\r\nthe link")]   // no marker
    [InlineData("#\r\nbody")]                  // marker alone
    [InlineData("#   \r\nbody")]               // empty heading
    [InlineData("")]
    public void ExtractExplicit_ReturnsNullWithoutUsableMarker(string note)
        => Assert.Null(NoteHeading.ExtractExplicit(note));

    /// <summary>
    /// Non-Latin scripts must behave identically: nothing in the mechanism
    /// depends on the writing system.
    /// </summary>
    [Theory]
    [InlineData("# Rechnungen\r\nnoch offen", "Rechnungen")]
    [InlineData("# 仕事のメモ\r\n本文", "仕事のメモ")]
    [InlineData("# работа\r\nтекст", "работа")]
    public void ExtractExplicit_IsScriptAgnostic(string note, string expected)
        => Assert.Equal(expected, NoteHeading.ExtractExplicit(note));

    /// <summary>
    /// These shapes all come from a real corpus. Guessing a heading from the
    /// look of the first line would file them as themes, when they are colour
    /// codes, port numbers and schedules. This is why the marker is required.
    /// </summary>
    [Theory]
    [InlineData("2e343b 4a5568 1c1f26 f7fafc\r\nsite palette")]
    [InlineData("port 5432\r\ndatabase config")]
    [InlineData("wednesday 10h 14h\r\navailability")]
    public void GuessImplicit_ProducesFalsePositives_HenceTheMarker(string note)
    {
        // The flaw is documented rather than pretended away.
        Assert.NotNull(NoteHeading.GuessImplicit(note));
        Assert.Null(NoteHeading.ExtractExplicit(note));
    }

    [Theory]
    [InlineData("I think the site is fine but the contact page needs work.", false)]
    [InlineData("Hi Alex,", false)]
    [InlineData("https://example.com/some/very/long/link", false)]
    [InlineData("project beta", true)]
    public void LooksLikeHeading_UsesStructureNotVocabulary(string line, bool expected)
        => Assert.Equal(expected, NoteHeading.LooksLikeHeading(line));

    [Fact]
    public void Normalize_CollapsesVariantsOfTheSameTheme()
    {
        Assert.Equal(
            NoteHeading.Normalize("Projet — Bêta"),
            NoteHeading.Normalize("projet   beta"));
    }
}
