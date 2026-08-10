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
