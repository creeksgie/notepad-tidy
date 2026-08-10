using System.Text;

namespace NotepadTidy.Core;

public enum TabStatus
{
    /// <summary>Parsé, layout vérifié, CRC valide. Seul état où l'écriture est permise.</summary>
    Ok,
    /// <summary>Magic "NP" absent — ce n'est pas un fichier TabState.</summary>
    BadMagic,
    /// <summary>Onglet lié à un vrai fichier (flag=01). À ne jamais modifier.</summary>
    FileBacked,
    /// <summary>Taille calculée ≠ taille réelle : variante de format non gérée.</summary>
    LayoutMismatch,
    /// <summary>CRC stocké ≠ CRC calculé. Fichier corrompu ou format modifié.</summary>
    BadCrc,
    /// <summary>Fichier trop court pour contenir un en-tête.</summary>
    TooShort,
    /// <summary>
    /// En-tête d'une variante inconnue — probablement une mise à jour de
    /// Notepad. On refuse plutôt que de parser à l'aveugle.
    /// </summary>
    UnknownVariant,
}

/// <summary>
/// Un onglet Notepad. Voir docs/FORMAT.md §4 pour le layout.
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

    /// <summary>Vrai si on peut réécrire ce fichier sans risque de perte.</summary>
    public bool IsSafeToRewrite => Status == TabStatus.Ok;

    private const byte FlagUnsaved = 0x00;

    /// <summary>
    /// Octet 4 de l'en-tête. Constant sur les 106 onglets du corpus de
    /// référence. Sert de sentinelle de version : tout autre valeur fait
    /// basculer le fichier en <see cref="TabStatus.UnknownVariant"/>.
    /// </summary>
    private const byte KnownVariantMarker = 0x01;

    /// <summary>
    /// Compteur du bloc de config émis par <see cref="Build"/>. La valeur 03
    /// est celle que Notepad a acceptée en conditions réelles ; le parseur
    /// accepte aussi 02, présent sur 17 % du corpus.
    /// </summary>
    private const byte WrittenExtraCount = 0x03;

    public static TabRecord Parse(Guid id, ReadOnlySpan<byte> file)
    {
        if (file.Length < 12)
            return new TabRecord { Id = id, Status = TabStatus.TooShort, FileLength = file.Length };

        if (file[0] != 0x4E || file[1] != 0x50)
            return new TabRecord { Id = id, Status = TabStatus.BadMagic, FileLength = file.Length };

        // Octet 3 : 00 = note volante, 01 = onglet adossé à un fichier réel.
        if (file[3] != FlagUnsaved)
            return new TabRecord { Id = id, Status = TabStatus.FileBacked, FileLength = file.Length };

        // Garde-fou anti-dérive de format. L'octet 4 vaut 01 sur la totalité du
        // corpus de référence et sa signification reste inconnue. Si une mise à
        // jour de Notepad le change, on veut un refus franc plutôt qu'un
        // parsing silencieusement décalé qui détruirait des notes.
        if (file[4] != KnownVariantMarker)
            return new TabRecord { Id = id, Status = TabStatus.UnknownVariant, FileLength = file.Length };

        int i = 5;
        int cursorStart = ReadVarint(file, ref i);
        int cursorEnd = ReadVarint(file, ref i);

        // Bloc de config : "01 00 00" puis un COMPTEUR, puis autant d'octets.
        // Ce n'est pas un bloc de taille fixe — le supposer décale d'un octet
        // sur les variantes à compteur 02, qui représentent 17 % du corpus.
        i += 3;                                 // 01 00 00
        int extraCount = file[i++];
        i += extraCount;

        int declared = ReadVarint(file, ref i);
        int textOffset = i;

        // Le layout doit tomber juste à l'octet près : en-tête + texte + marqueur + CRC.
        // 17 des 100 notes du corpus de référence échouent ici (variante inconnue).
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
    /// Fabrique un fichier d'onglet complet. Notepad ne fait aucune différence
    /// avec un fichier qu'il aurait produit lui-même (vérifié le 2026-08-10).
    /// </summary>
    public static byte[] Build(string text)
    {
        // Longueur en unités de code UTF-16, ce qui est exactement string.Length.
        int n = text.Length;

        var body = new List<byte>(18 + n * 2 + 1)
        {
            0x4E, 0x50,             // "NP"
            0x00,                   // séquence
            FlagUnsaved,            // note volante
            KnownVariantMarker,
        };

        WriteVarint(body, n);       // curseur début — placé en fin de texte
        WriteVarint(body, n);       // curseur fin

        // Bloc à compteur. On émet systématiquement la variante 03, celle que
        // Notepad a acceptée en conditions réelles. Le parseur, lui, lit le
        // compteur et accepte les deux.
        body.AddRange([0x01, 0x00, 0x00, WrittenExtraCount]);
        for (int k = 0; k < WrittenExtraCount; k++) body.Add(0x01);

        WriteVarint(body, n);       // longueur du contenu
        body.AddRange(Encoding.Unicode.GetBytes(text));
        body.Add(0x01);         // marqueur

        var result = new byte[body.Count + 4];
        body.CopyTo(result);
        // Le CRC couvre [3 .. fin du marqueur], soit tout le corps sauf les 3 premiers octets.
        Crc32.WriteBigEndian(result.AsSpan(body.Count), Crc32.Compute(result.AsSpan(3, body.Count - 3)));
        return result;
    }

    // LEB128 non signé. Attention : la taille varie avec la valeur, donc
    // l'en-tête n'a pas une taille fixe et l'offset du texte peut être impair.
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
