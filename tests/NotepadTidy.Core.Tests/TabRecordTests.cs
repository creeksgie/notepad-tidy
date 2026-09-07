using System.Text;
using NotepadTidy.Core;

namespace NotepadTidy.Core.Tests;

public class TabRecordTests
{
    /// <summary>
    /// A real 40-byte tab captured from disk, containing exactly "test GUID ".
    /// This is our reference vector: if this test breaks, either the format
    /// changed or the parser regressed.
    /// </summary>
    private static readonly byte[] RealTab40 =
    [
        0x4E, 0x50, 0x00, 0x00, 0x01,       // "NP", sequence, unsaved flag, marker
        0x0A, 0x0A,                          // caret 10/10
        0x01, 0x00, 0x00, 0x03, 0x01, 0x01, 0x01, // 01 00 00, counter 3, three 01 bytes
        0x0A,                                // length = 10 characters
        0x74, 0x00, 0x65, 0x00, 0x73, 0x00, 0x74, 0x00, 0x20, 0x00, // "test "
        0x47, 0x00, 0x55, 0x00, 0x49, 0x00, 0x44, 0x00, 0x20, 0x00, // "GUID "
        0x01,                                // marker
        0x8B, 0x21, 0x0C, 0x88,              // CRC32 big-endian
    ];

    [Fact]
    public void Parse_RealTab_ReturnsExactText()
    {
        var record = TabRecord.Parse(Guid.NewGuid(), RealTab40);

        Assert.Equal(TabStatus.Ok, record.Status);
        Assert.Equal("test GUID ", record.Text);
        Assert.Equal(10, record.DeclaredLength);
        Assert.Equal(15, record.TextOffset);
        Assert.Equal(10, record.CursorStart);
    }

    [Fact]
    public void Crc_OfRealTab_MatchesStoredValue()
    {
        Assert.True(Crc32.Verify(RealTab40));
        Assert.Equal(0x8B210C88u, Crc32.ReadStored(RealTab40));
    }

    [Fact]
    public void Build_ThenParse_RoundTrips()
    {
        const string text = "Hello, this is a note with accents: éàùç€.";

        var record = TabRecord.Parse(Guid.NewGuid(), TabRecord.Build(text));

        Assert.Equal(TabStatus.Ok, record.Status);
        Assert.Equal(text, record.Text);
    }

    /// <summary>
    /// The length varint changes size at 7-bit boundaries, which shifts the
    /// whole text block. This is the costliest trap in the format, so both
    /// sides of every boundary are tested explicitly.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(126)]
    [InlineData(127)]     // last one-byte varint
    [InlineData(128)]     // first two-byte varint
    [InlineData(16_383)]  // last two-byte varint
    [InlineData(16_384)]  // first three-byte varint
    public void Build_AcrossVarintBoundaries_RoundTrips(int length)
    {
        var text = new string('a', length);

        var record = TabRecord.Parse(Guid.NewGuid(), TabRecord.Build(text));

        Assert.Equal(TabStatus.Ok, record.Status);
        Assert.Equal(length, record.Text.Length);
        Assert.Equal(text, record.Text);
    }

    [Fact]
    public void Build_PreservesCrlfAndUnicode()
    {
        const string text = "line 1\r\nline 2\r\n\r\n--- separator ---\r\né€漢字";

        var record = TabRecord.Parse(Guid.NewGuid(), TabRecord.Build(text));

        Assert.Equal(text, record.Text);
    }

    [Fact]
    public void Parse_CounterVariant02_IsSupported()
    {
        // 17% of the real corpus carries counter 02 instead of 03, hence a
        // config block one byte shorter. A fixed size shifts everything.
        var text = "abc";
        var body = new List<byte> { 0x4E, 0x50, 0x00, 0x00, 0x01, 0x03, 0x03 };
        body.AddRange([0x01, 0x00, 0x00, 0x02, 0x01, 0x01]); // counter = 2
        body.Add(0x03);                                       // length = 3
        body.AddRange(Encoding.Unicode.GetBytes(text));
        body.Add(0x01);
        var file = new byte[body.Count + 4];
        body.CopyTo(file);
        Crc32.WriteBigEndian(file.AsSpan(body.Count), Crc32.Compute(file.AsSpan(3, body.Count - 3)));

        var record = TabRecord.Parse(Guid.NewGuid(), file);

        Assert.Equal(TabStatus.Ok, record.Status);
        Assert.Equal(text, record.Text);
    }

    [Fact]
    public void Parse_RejectsForeignFile()
    {
        var record = TabRecord.Parse(Guid.NewGuid(), "not a Notepad tab at all"u8.ToArray());

        Assert.Equal(TabStatus.BadMagic, record.Status);
        Assert.False(record.IsSafeToRewrite);
    }

    [Fact]
    public void Parse_RefusesFileBackedTab()
    {
        // Flag 01: the tab mirrors a real user file. Never touch it.
        var file = (byte[])RealTab40.Clone();
        file[3] = 0x01;

        var record = TabRecord.Parse(Guid.NewGuid(), file);

        Assert.Equal(TabStatus.FileBacked, record.Status);
        Assert.False(record.IsSafeToRewrite);
    }

    [Fact]
    public void Parse_RefusesUnknownHeaderVariant()
    {
        // Byte 4 acts as a version sentinel against silent format drift.
        var file = (byte[])RealTab40.Clone();
        file[4] = 0x02;

        var record = TabRecord.Parse(Guid.NewGuid(), file);

        Assert.Equal(TabStatus.UnknownVariant, record.Status);
        Assert.False(record.IsSafeToRewrite);
    }

    [Fact]
    public void Parse_DetectsCorruptedCrc()
    {
        var file = (byte[])RealTab40.Clone();
        file[^1] ^= 0xFF;

        var record = TabRecord.Parse(Guid.NewGuid(), file);

        Assert.Equal(TabStatus.BadCrc, record.Status);
        Assert.False(record.IsSafeToRewrite);
    }

    [Fact]
    public void Parse_DetectsLengthTampering()
    {
        // Declared length altered: the layout no longer adds up. We want a
        // refusal, not an approximate read.
        var file = (byte[])RealTab40.Clone();
        file[14] = 0x0B;

        var record = TabRecord.Parse(Guid.NewGuid(), file);

        Assert.Equal(TabStatus.LayoutMismatch, record.Status);
        Assert.False(record.IsSafeToRewrite);
    }

    [Fact]
    public void Parse_RejectsTruncatedFile()
    {
        var record = TabRecord.Parse(Guid.NewGuid(), RealTab40.AsSpan(0, 8).ToArray());

        Assert.Equal(TabStatus.TooShort, record.Status);
    }

    /// <summary>
    /// Truncation past the 12-byte header. The fixed guard does not catch these:
    /// the varints and the config block are sized by the file itself, so the
    /// parser has to check every read. Each of these threw
    /// IndexOutOfRangeException before the bounds were added.
    /// </summary>
    [Theory]
    // Caret varint with the continuation bit always set: it never terminates
    // and runs off the end of a 12-byte file.
    [InlineData(new byte[] { 0x4E, 0x50, 0x00, 0x00, 0x01,
                             0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80 })]
    // Ends inside the config block, on the counter byte.
    [InlineData(new byte[] { 0x4E, 0x50, 0x00, 0x00, 0x01,
                             0x00, 0x00, 0x01, 0x00, 0x00, 0x03, 0x01 })]
    public void Parse_TruncatedPastHeader_RefusesWithoutThrowing(byte[] file)
    {
        var record = TabRecord.Parse(Guid.NewGuid(), file);

        Assert.False(record.IsSafeToRewrite);
    }

    /// <summary>
    /// Every truncation of a known-good tab must be refused. One byte short is
    /// still a corrupted file, and a corrupted file must never be rewritten.
    /// </summary>
    [Fact]
    public void Parse_EveryPrefixOfARealTab_IsRefused()
    {
        for (int length = 0; length < RealTab40.Length; length++)
        {
            var record = TabRecord.Parse(Guid.NewGuid(), RealTab40.AsSpan(0, length).ToArray());

            Assert.False(record.IsSafeToRewrite);
        }
    }

    /// <summary>
    /// A declared length big enough to overflow `declared * 2` must be refused
    /// on its own, not compared against a wrapped number.
    /// </summary>
    [Fact]
    public void Parse_AbsurdDeclaredLength_IsRefused()
    {
        var file = new List<byte> { 0x4E, 0x50, 0x00, 0x00, 0x01,
                                    0x00, 0x00, 0x01, 0x00, 0x00, 0x00 };
        file.AddRange([0x80, 0x80, 0x80, 0x80, 0x04]); // varint = 2^30
        file.AddRange(new byte[5]);

        var record = TabRecord.Parse(Guid.NewGuid(), file.ToArray());

        Assert.Equal(TabStatus.LayoutMismatch, record.Status);
    }

    [Fact]
    public void IsSafeToRewrite_IsTrueOnlyForOk()
    {
        foreach (var status in Enum.GetValues<TabStatus>())
        {
            var record = new TabRecord { Id = Guid.Empty, Status = status };
            Assert.Equal(status == TabStatus.Ok, record.IsSafeToRewrite);
        }
    }
}
