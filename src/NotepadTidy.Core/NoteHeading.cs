namespace NotepadTidy.Core;

/// <summary>
/// Recognises a heading on the first line: "this note belongs to theme X",
/// written by the user themselves.
///
/// <para>This is the only filing mechanism in the project that is both
/// administration-free and <b>completely language-independent</b>: the category
/// is not guessed, it is declared. An English, German or Japanese user gets
/// exactly the same behaviour.</para>
///
/// <para>Recognition criteria are structural — length, word count, trailing
/// punctuation — never lexical. No dictionary.</para>
/// </summary>
public static class NoteHeading
{
    public const int MaxLength = 60;
    public const int MaxWords = 8;

    /// <summary>Explicit heading marker, markdown style.</summary>
    public const char Marker = '#';

    /// <summary>Punctuation that betrays a sentence rather than a heading.</summary>
    private static readonly char[] SentenceEnders = ['.', '!', '?', ',', ';'];

    /// <summary>
    /// An <b>explicitly</b> declared heading: first line prefixed by
    /// <see cref="Marker"/>.
    ///
    /// <para>The prefix is not decoration. Measured on a real corpus of 99
    /// notes, guessing the heading from the shape of the first line yields 11%
    /// detections of which every single one is a false positive: colour codes,
    /// port numbers, schedules, passwords. An explicit marker removes the
    /// ambiguity entirely for the cost of one character.</para>
    /// </summary>
    /// <summary>
    /// How many non-empty lines are scanned when looking for the marker.
    ///
    /// Looking only at the very first line is not enough: real notes often
    /// start with residue — a pasted HTML entity, a stray character, a leftover
    /// fragment — with the heading a couple of lines below. Scanning a few
    /// lines costs nothing and rescues those notes; scanning the whole note
    /// would start matching '#' inside the content.
    /// </summary>
    public const int HeadingSearchDepth = 4;

    public static string? ExtractExplicit(string note)
    {
        foreach (var line in FirstNonEmptyLines(note, HeadingSearchDepth))
        {
            if (line.Length < 2 || line[0] != Marker) continue;

            var heading = line[1..].Trim();
            if (heading.Length > 0 && heading.Length <= MaxLength) return heading;
        }
        return null;
    }

    /// <summary>
    /// Removes the heading line from a note's text, leaving the body.
    ///
    /// Used on absorbed notes: once merged, the separator above them already
    /// carries the theme and the date, so repeating "#theme" on every chunk is
    /// noise. The container keeps its own heading — it is what names the tab.
    /// </summary>
    public static string StripHeading(string text)
    {
        // Index arithmetic rather than Split/Join, for two reasons. Splitting on
        // '\n' alone is wrong — Notepad writes lone '\r' too, and a note using
        // them would count as a single line, so "drop the heading line" would
        // drop the whole note. And rejoining would rewrite every line ending in
        // the note. Slicing touches nothing it was not asked to touch.
        int i = 0;
        while (i < text.Length && IsLineBreakOrSpace(text[i])) i++;
        if (i >= text.Length || text[i] != Marker) return text;

        int endOfLine = i;
        while (endOfLine < text.Length && text[endOfLine] is not ('\r' or '\n')) endOfLine++;

        int bodyStart = endOfLine;
        while (bodyStart < text.Length && IsLineBreakOrSpace(text[bodyStart])) bodyStart++;

        // A heading is one short line. If we are about to remove more than that,
        // the parse went wrong and the safe move is to change nothing — losing a
        // note is far worse than keeping a redundant title.
        if (bodyStart > MaxLength + MaxStrippedSlack) return text;

        return text[bodyStart..];
    }

    /// <summary>
    /// Blank lines and indentation allowed around a heading before
    /// <see cref="StripHeading"/> considers the removal suspicious.
    /// </summary>
    private const int MaxStrippedSlack = 16;

    private static bool IsLineBreakOrSpace(char c) => c is '\r' or '\n' or ' ' or '\t';

    /// <summary>The first non-empty, trimmed lines of a note.</summary>
    public static IEnumerable<string> FirstNonEmptyLines(string note, int depth)
    {
        int seen = 0;
        // Lone '\r' included: Notepad does not always write CRLF pairs.
        foreach (var line in note.Split('\r', '\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            yield return trimmed;
            if (++seen >= depth) yield break;
        }
    }

    /// <summary>
    /// A heading <b>guessed</b> from the shape of the first line. Kept for
    /// measurement only — do not file with it, see the false-positive rate
    /// documented on <see cref="ExtractExplicit"/>.
    /// </summary>
    public static string? GuessImplicit(string note)
    {
        var line = FirstNonEmptyLine(note);
        return line is not null && LooksLikeHeading(line) ? line.Trim() : null;
    }

    public static bool LooksLikeHeading(string line)
    {
        line = line.Trim();
        if (line.Length == 0 || line.Length > MaxLength) return false;

        // A sentence ends with punctuation; a heading does not.
        if (SentenceEnders.Contains(line[^1])) return false;

        // A heading is short. The word count holds for space-separated
        // languages; for the others the length limit is enough on its own.
        int words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        if (words > MaxWords) return false;

        // A bare URL is not a heading, it is the content of the note.
        if (line.Contains("://", StringComparison.Ordinal)) return false;

        return true;
    }

    private static string? FirstNonEmptyLine(string note)
    {
        // Lone '\r' included: Notepad does not always write CRLF pairs.
        foreach (var line in note.Split('\r', '\n'))
            if (line.Trim().Length > 0) return line;
        return null;
    }

    /// <summary>
    /// Canonical form of a heading, so that "Project — Beta" and "project beta"
    /// denote the same theme. Letters and digits of any script are preserved.
    /// </summary>
    public static string Normalize(string heading)
        => string.Join('-', CorpusProfile.Tokenize(heading));
}
