using NotepadTidy.Core;

namespace NotepadTidy.Core.Tests;

/// <summary>
/// These tests exist because this function once deleted the entire content of
/// every note using lone carriage returns. It split on '\n' only, so such a
/// note counted as a single line, and "remove the heading line" removed
/// everything. Two thirds of a real corpus was lost in one run.
/// </summary>
public class StripHeadingTests
{
    [Fact]
    public void StripHeading_RemovesTheHeadingAndKeepsTheBody()
    {
        Assert.Equal("the body", NoteHeading.StripHeading("#project\r\n\r\nthe body"));
        Assert.Equal("the body", NoteHeading.StripHeading("#project\nthe body"));
    }

    /// <summary>The regression that caused real data loss.</summary>
    [Fact]
    public void StripHeading_HandlesLoneCarriageReturns()
    {
        const string note = "#project\rline one\rline two\rline three";

        var stripped = NoteHeading.StripHeading(note);

        Assert.Equal("line one\rline two\rline three", stripped);
        Assert.NotEmpty(stripped);
    }

    [Fact]
    public void StripHeading_LeavesNotesWithoutAHeadingUntouched()
    {
        const string note = "just content\rwith no heading at all";

        Assert.Equal(note, NoteHeading.StripHeading(note));
    }

    [Fact]
    public void StripHeading_PreservesTheOriginalLineEndings()
    {
        // Rejoining split lines would rewrite every line ending in the note.
        Assert.Equal("a\rb\r\nc", NoteHeading.StripHeading("#t\r\na\rb\r\nc"));
    }

    /// <summary>
    /// The last-resort guard: if the removal looks bigger than a heading, the
    /// text is returned untouched. Keeping a redundant title costs nothing;
    /// losing a note cannot be undone.
    /// </summary>
    [Fact]
    public void StripHeading_RefusesToRemoveMoreThanAHeading()
    {
        var note = "#" + new string('x', 300) + "\r\nbody";

        Assert.Equal(note, NoteHeading.StripHeading(note));
    }

    [Fact]
    public void StripHeading_HandlesAHeadingOnlyNote()
    {
        Assert.Equal("", NoteHeading.StripHeading("#project"));
        Assert.Equal("", NoteHeading.StripHeading("#project\r\n\r\n"));
    }

    [Fact]
    public void StripHeading_HandlesEmptyInput()
        => Assert.Equal("", NoteHeading.StripHeading(""));
}
