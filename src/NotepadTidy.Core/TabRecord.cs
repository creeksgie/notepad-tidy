using System.Text;

namespace NotepadTidy.Core;

public enum TabStatus
{
    /// <summary>Parsed, layout verified, CRC valid. The only state where writing is allowed.</summary>
    Ok,
    /// <summary>No "NP" magic — this is not a TabState file.</summary>
    BadMagic,
    /// <summary>Tab backed by a real file (flag=01). Must never be modified.</summary>
    FileBacked,
    /// <summary>Computed size ≠ actual size: unhandled format variant.</summary>
    LayoutMismatch,
    /// <summary>Stored CRC ≠ computed CRC. Corrupted file, or changed format.</summary>
    BadCrc,
    /// <summary>File too short to hold a header.</summary>
    TooShort,
    /// <summary>
    /// Header of an unknown variant — most likely a Notepad update. We refuse
    /// rather than parse blindly.
    /// </summary>
    UnknownVariant,
}

/// <summary>
/// A Notepad tab. See docs/FORMAT.md §4 for the layout.
/// </summary>
public sealed class TabRecord
{
    public required Guid Id { get; init; }
    public required TabStatus Status { get; init; }
    public string Text { get; init; } = "";
    public int CursorStart { get; init; }
    public int CursorEnd { get; init; }
    public int DeclaredLength { get; init; }
    public int TextOffset { get; init; }
    public int FileLength { get; init; }

    /// <summary>When the tab was created. Merges are ordered by this, never by size.</summary>
    public DateTime Created { get; init; }

    /// <summary>True when this file can be rewritten without risking data loss.</summary>
    public bool IsSafeToRewrite => Status == TabStatus.Ok;

    private const byte FlagUnsaved = 0x00;

    /// <summary>
    /// Header byte 4. Constant across the 106 tabs of the reference corpus. Used
    /// as a version sentinel: any other value sends the file to
    /// <see cref="TabStatus.UnknownVariant"/>.
    /// </summary>
    private const byte KnownVariantMarker = 0x01;

    /// <summary>
    /// Config-block counter emitted by <see cref="Build"/>. The value 03 is the
    /// one Notepad accepted in real conditions; the parser also accepts 02,
    /// which occurs in 17% of the reference corpus.
    /// </summary>
    private const byte WrittenExtraCount = 0x03;

    public static TabRecord Parse(Guid id, ReadOnlySpan<byte> file)
    {
        if (file.Length < 12)
            return new TabRecord { Id = id, Status = TabStatus.TooShort, FileLength = file.Length };

        if (file[0] != 0x4E || file[1] != 0x50)
            return new TabRecord { Id = id, Status = TabStatus.BadMagic, FileLength = file.Length };

        // Byte 3: 00 = unsaved note, 01 = tab backed by a real file.
        if (file[3] != FlagUnsaved)
            return new TabRecord { Id = id, Status = TabStatus.FileBacked, FileLength = file.Length };

        // Guard against format drift. Byte 4 is 01 across the whole reference
        // corpus and its meaning is unknown. Should a Notepad update change it,
        // we want a clean refusal rather than a silently shifted parse that
        // would destroy notes.
        if (file[4] != KnownVariantMarker)
            return new TabRecord { Id = id, Status = TabStatus.UnknownVariant, FileLength = file.Length };

        int i = 5;
        int cursorStart = ReadVarint(file, ref i);
        int cursorEnd = ReadVarint(file, ref i);

        // Config block: "01 00 00", then a COUNTER, then that many bytes.
        // It is not a fixed-size block — assuming so shifts everything by one
        // byte on counter-02 variants, which are 17% of the corpus.
        i += 3;
        int extraCount = file[i++];
        i += extraCount;

        int declared = ReadVarint(file, ref i);
        int textOffset = i;

        // The layout must add up to the byte: header + text + marker + CRC.
        int expected = textOffset + declared * 2 + 5;
        if (expected != file.Length)
        {
            return new TabRecord
            {
                Id = id, Status = TabStatus.LayoutMismatch,
                DeclaredLength = declared, TextOffset = textOffset, FileLength = file.Length,
            };
        }

        if (!Crc32.Verify(file))
        {
            return new TabRecord
            {
                Id = id, Status = TabStatus.BadCrc,
                DeclaredLength = declared, TextOffset = textOffset, FileLength = file.Length,
            };
        }

        return new TabRecord
        {
            Id = id,
            Status = TabStatus.Ok,
            Text = Encoding.Unicode.GetString(file.Slice(textOffset, declared * 2)),
            CursorStart = cursorStart,
            CursorEnd = cursorEnd,
            DeclaredLength = declared,
            TextOffset = textOffset,
            FileLength = file.Length,
        };
    }

    /// <summary>
    /// Builds a complete tab file. Notepad makes no distinction between this and
    /// a file it produced itself (verified in real conditions).
    /// </summary>
    public static byte[] Build(string text)
    {
        // Length in UTF-16 code units, which is exactly string.Length.
        int n = text.Length;

        var body = new List<byte>(18 + n * 2 + 1)
        {
            0x4E, 0x50,             // "NP"
            0x00,                   // sequence
            FlagUnsaved,            // unsaved note
            KnownVariantMarker,
        };

        WriteVarint(body, n);       // caret start — placed at the end of the text
        WriteVarint(body, n);       // caret end

        // Counter block. We always emit the 03 variant, the one Notepad accepted
        // in real conditions. The parser reads the counter and accepts both.
        body.AddRange([0x01, 0x00, 0x00, WrittenExtraCount]);
        for (int k = 0; k < WrittenExtraCount; k++) body.Add(0x01);

        WriteVarint(body, n);       // content length
        body.AddRange(Encoding.Unicode.GetBytes(text));
        body.Add(0x01);             // marker

        var result = new byte[body.Count + 4];
        body.CopyTo(result);
        // The CRC covers [3 .. end of marker], i.e. the whole body minus the
        // first three bytes.
        Crc32.WriteBigEndian(result.AsSpan(body.Count), Crc32.Compute(result.AsSpan(3, body.Count - 3)));
        return result;
    }

    // Unsigned LEB128. Note that the size varies with the value, so the header
    // has no fixed length and the text offset can land on an odd byte.
    private static int ReadVarint(ReadOnlySpan<byte> b, ref int i)
    {
        int value = 0, shift = 0;
        while (true)
        {
            byte c = b[i++];
            value |= (c & 0x7F) << shift;
            if ((c & 0x80) == 0) return value;
            shift += 7;
        }
    }

    private static void WriteVarint(List<byte> output, int value)
    {
        while (value >= 0x80)
        {
            output.Add((byte)(value | 0x80));
            value >>= 7;
        }
        output.Add((byte)value);
    }
}
