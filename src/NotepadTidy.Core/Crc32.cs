namespace NotepadTidy.Core;

/// <summary>
/// CRC32 tel que Notepad l'utilise dans les fichiers TabState.
///
/// Polynôme 0xEDB88320 (zlib, réfléchi), init et XOR final à 0xFFFFFFFF.
/// La subtilité qui coûte cher : le résultat est stocké en BIG-ENDIAN dans
/// le fichier, alors que tout le reste du format est little-endian.
///
/// Voir docs/FORMAT.md §5. Validé sur 106 onglets réels.
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
    /// Plage couverte par le CRC dans un fichier TabState : [3 .. L-5].
    /// On saute le magic "NP" et l'octet de séquence, on exclut le CRC.
    /// </summary>
    public static uint ComputeForRecord(ReadOnlySpan<byte> file)
        => Compute(file.Slice(3, file.Length - 7));

    /// <summary>CRC stocké dans les 4 derniers octets, en big-endian.</summary>
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
