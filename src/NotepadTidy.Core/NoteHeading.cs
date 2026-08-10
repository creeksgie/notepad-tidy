namespace NotepadTidy.Core;

/// <summary>
/// Reconnaissance d'un titre en première ligne : « cette note appartient au
/// thème X », écrit par l'utilisateur lui-même.
///
/// <para>C'est le seul mécanisme de classement du projet qui soit à la fois
/// sans administration et <b>totalement indépendant de la langue</b> : la
/// catégorie n'est pas devinée, elle est déclarée. Un utilisateur anglophone,
/// allemand ou japonais obtient exactement le même comportement.</para>
///
/// <para>Les critères de reconnaissance sont structurels — longueur, nombre de
/// mots, ponctuation finale — et non lexicaux. Aucun dictionnaire.</para>
/// </summary>
public static class NoteHeading
{
    public const int MaxLength = 60;
    public const int MaxWords = 8;

    /// <summary>Ponctuation qui trahit une phrase plutôt qu'un titre.</summary>
    private static readonly char[] SentenceEnders = ['.', '!', '?', ',', ';'];

    /// <summary>Marqueur explicite de titre, à la markdown.</summary>
    public const char Marker = '#';

    /// <summary>
    /// Titre <b>explicitement</b> déclaré : première ligne préfixée par
    /// <see cref="Marker"/>.
    ///
    /// <para>Le préfixe n'est pas une coquetterie. Mesuré sur un corpus réel de
    /// 99 notes, deviner le titre à la seule forme de la première ligne donne
    /// 11 % de détections dont la totalité sont des faux positifs : codes
    /// couleur, numéros de port, horaires, mots de passe. Un marqueur explicite
    /// ramène l'ambiguïté à zéro pour le coût d'un caractère.</para>
    /// </summary>
    public static string? ExtractExplicit(string note)
    {
        var line = FirstNonEmptyLine(note)?.Trim();
        if (line is null || line.Length < 2 || line[0] != Marker) return null;

        var heading = line[1..].Trim();
        return heading.Length > 0 && heading.Length <= MaxLength ? heading : null;
    }

    /// <summary>
    /// Titre <b>deviné</b> à la forme de la première ligne. Conservé pour
    /// mesurer, mais à ne pas utiliser pour classer : voir le taux de faux
    /// positifs documenté sur <see cref="ExtractExplicit"/>.
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

        // Une phrase se termine par une ponctuation ; un titre, non.
        if (SentenceEnders.Contains(line[^1])) return false;

        // Un titre est court. Le compte de mots reste valable pour les langues
        // à espaces ; pour les autres, la limite de longueur suffit.
        int words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        if (words > MaxWords) return false;

        // Une URL seule n'est pas un titre : c'est le contenu de la note.
        if (line.Contains("://", StringComparison.Ordinal)) return false;

        return true;
    }

    private static string? FirstNonEmptyLine(string note)
    {
        foreach (var line in note.Split('\n'))
            if (line.Trim().Length > 0) return line;
        return null;
    }

    /// <summary>
    /// Forme canonique d'un titre, pour que « Gamma — beta » et
    /// « gamma beta » désignent le même thème. On garde les lettres et les
    /// chiffres de n'importe quel alphabet.
    /// </summary>
    public static string Normalize(string heading)
        => string.Join('-', CorpusProfile.Tokenize(heading));
}
