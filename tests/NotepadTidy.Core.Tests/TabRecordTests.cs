using System.Text;
using NotepadTidy.Core;

namespace NotepadTidy.Core.Tests;

public class TabRecordTests
{
    /// <summary>
    /// Un onglet réel de 40 octets, capturé le 2026-08-10, contenant
    /// exactement "test GUID ". C'est notre vecteur de référence : si ce test
    /// casse, c'est que le format a changé ou que le parseur a régressé.
    /// </summary>
    private static readonly byte[] RealTab40 =
    [
        0x4E, 0x50, 0x00, 0x00, 0x01,       // "NP", séquence, flag=note volante, inconnu
        0x0A, 0x0A,                          // curseur 10/10
        0x01, 0x00, 0x00, 0x03, 0x01, 0x01, 0x01, // 01 00 00, compteur 3, trois 01
        0x0A,                                // longueur = 10 caractères
        0x74, 0x00, 0x65, 0x00, 0x73, 0x00, 0x74, 0x00, 0x20, 0x00, // "test "
        0x47, 0x00, 0x55, 0x00, 0x49, 0x00, 0x44, 0x00, 0x20, 0x00, // "GUID "
        0x01,                                // marqueur
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
        const string text = "Bonjour, ceci est une note avec des accents : éàùç€.";

        var record = TabRecord.Parse(Guid.NewGuid(), TabRecord.Build(text));

        Assert.Equal(TabStatus.Ok, record.Status);
        Assert.Equal(text, record.Text);
    }

    /// <summary>
    /// La taille du varint de longueur change aux frontières de 7 bits, ce qui
    /// décale tout le bloc de texte. C'est le piège le plus coûteux du format,
    /// donc on le teste explicitement de part et d'autre de chaque seuil.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(126)]
    [InlineData(127)]     // dernier varint sur 1 octet
    [InlineData(128)]     // premier sur 2 octets
    [InlineData(16_383)]  // dernier sur 2 octets
    [InlineData(16_384)]  // premier sur 3 octets
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
        const string text = "ligne 1\r\nligne 2\r\n\r\n--- séparateur ---\r\né€漢字";

        var record = TabRecord.Parse(Guid.NewGuid(), TabRecord.Build(text));

        Assert.Equal(text, record.Text);
    }

    [Fact]
    public void Parse_CounterVariant02_IsSupported()
    {
        // 17 % du corpus réel porte le compteur 02 au lieu de 03, donc un bloc
        // de config plus court d'un octet. Une taille fixe décale tout.
        var text = "jeu";
        var body = new List<byte> { 0x4E, 0x50, 0x00, 0x00, 0x01, 0x03, 0x03 };
        body.AddRange([0x01, 0x00, 0x00, 0x02, 0x01, 0x01]); // compteur = 2
        body.Add(0x03);                                       // longueur = 3
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
        var record = TabRecord.Parse(Guid.NewGuid(), "pas du tout un onglet Notepad"u8.ToArray());

        Assert.Equal(TabStatus.BadMagic, record.Status);
        Assert.False(record.IsSafeToRewrite);
    }

    [Fact]
    public void Parse_RefusesFileBackedTab()
    {
        // Flag 01 : l'onglet reflète un vrai fichier de l'utilisateur.
        // L'outil ne doit jamais y toucher.
        var file = (byte[])RealTab40.Clone();
        file[3] = 0x01;

        var record = TabRecord.Parse(Guid.NewGuid(), file);

        Assert.Equal(TabStatus.FileBacked, record.Status);
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
        // Longueur déclarée modifiée : le layout ne tombe plus juste.
        // On veut un refus, pas une lecture approximative.
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
