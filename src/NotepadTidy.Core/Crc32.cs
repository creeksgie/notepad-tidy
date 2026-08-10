namespace NotepadTidy.Core;

/// <summary>
/// CRC32 as Notepad uses it in TabState files.
///
/// Polynomial 0xEDB88320 (zlib, reflected), init and final XOR both
/// 0xFFFFFFFF. The costly subtlety: the result is stored BIG-ENDIAN in the
/// file, while everything else in the format is little-endian.
///
/// See docs/FORMAT.md §5. Validated against 106 real tabs.
/// </summary>
public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in data)
            c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    /// <summary>
    /// Range covered by the CRC in a TabState file: [3 .. L-5]. The "NP" magic
    /// and the sequence byte are skipped; the CRC itself is excluded.
    /// </summary>
    public static uint ComputeForRecord(ReadOnlySpan<byte> file)
        => Compute(file.Slice(3, file.Length - 7));

    /// <summary>CRC stored in the last four bytes, big-endian.</summary>
    public static uint ReadStored(ReadOnlySpan<byte> file)
    {
        int n = file.Length;
        return (uint)((file[n - 4] << 24) | (file[n - 3] << 16) | (file[n - 2] << 8) | file[n - 1]);
    }

    public static void WriteBigEndian(Span<byte> destination, uint value)
    {
        destination[0] = (byte)(value >> 24);
        destination[1] = (byte)(value >> 16);
        destination[2] = (byte)(value >> 8);
        destination[3] = (byte)value;
    }

    public static bool Verify(ReadOnlySpan<byte> file)
        => file.Length >= 12 && ComputeForRecord(file) == ReadStored(file);
}
