namespace NotepadTidy.Core;

/// <summary>
/// Découvre le vocabulaire des thèmes par <b>préfixes partagés</b>.
///
/// <para>Motivation, tirée d'un corpus réel : en ajoutant « #thème » au début
/// de la première ligne, on colle très souvent le thème au contenu existant —
/// « #themeSalut ! », « #projectpour les colonnes ». Aucun séparateur ne
/// permet de savoir où finit le thème.</para>
///
/// <para>Mais un thème sert plusieurs fois. Treize notes commençant par
/// « #project… » partagent ce préfixe, et lui seul. On n'a donc pas besoin
/// de deviner : il suffit de mesurer.</para>
///
/// <para>Aucun dictionnaire, aucune langue, aucun modèle — uniquement des
/// préfixes et des comptes.</para>
/// </summary>
public static class ThemeVocabulary
{
    public const int MinThemeLength = 2;
    public const int MaxThemeLength = 30;
    public const int MinNotesPerTheme = 2;

    /// <summary>
    /// Longueur au-delà de laquelle un préfixe ne gagne plus de points.
    ///
    /// Sans ce plafond, un préfixe long couvrant peu de notes l'emporte sur le
    /// vrai thème : « theme// ===== configuration » (2 notes × 27) battait
    /// « theme » (8 notes × 5) et fragmentait le thème en trois.
    /// </summary>
    public const int LengthCredit = 8;

    /// <summary>
    /// Texte de la première ligne situé après le marqueur, ou <c>null</c> si la
    /// note ne commence pas par un marqueur.
    /// </summary>
    public static string? RawHeadingLine(string note)
    {
        foreach (var line in note.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            return trimmed.Length >= 2 && trimmed[0] == NoteHeading.Marker
                ? trimmed[1..].TrimStart()
                : null;
        }
        return null;
    }

    /// <summary>
    /// Thèmes déduits d'un corpus, du plus utilisé au moins utilisé.
    ///
    /// Glouton : on retient à chaque tour le préfixe qui explique le plus de
    /// notes — à égalité, le plus long — puis on retire les notes couvertes et
    /// on recommence.
    /// </summary>
    public static IReadOnlyList<string> Discover(IEnumerable<string> notes)
    {
        var heads = notes.Select(RawHeadingLine)
                         .Where(h => !string.IsNullOrWhiteSpace(h))
                         .Select(h => h!.ToLowerInvariant())
                         .ToList();

        var themes = new List<string>();
        var remaining = new List<string>(heads);

        while (remaining.Count > 0)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var head in remaining)
            {
                int limit = Math.Min(MaxThemeLength, head.Length);
                for (int length = MinThemeLength; length <= limit; length++)
                {
                    var prefix = head[..length];
                    counts[prefix] = counts.GetValueOrDefault(prefix) + 1;
                }
            }

            // Score = nombre de notes × longueur du préfixe.
            //
            // Trier d'abord par nombre ne marche pas : un préfixe court est
            // toujours au moins aussi fréquent que ses extensions, donc « pl »
            // l'emporterait sur « plexo » et fusionnerait plexo avec play.
            // Trier d'abord par longueur ne marche pas non plus : deux notes
            // ouvrant sur « themesalut ! » donneraient ce texte pour thème.
            //
            // Le produit arbitre : « project » (13×10) bat « fl » (18×2),
            // et « theme » (8×5) bat « themesalut » (2×10).
            var best = counts
                .Where(kv => kv.Value >= MinNotesPerTheme)
                .OrderByDescending(kv => kv.Value * Math.Min(kv.Key.Length, LengthCredit))
                .ThenByDescending(kv => kv.Value)
                .ThenByDescending(kv => kv.Key.Length)
                .Select(kv => kv.Key)
                .FirstOrDefault();

            if (best is null) break;

            themes.Add(best);
            remaining.RemoveAll(h => h.StartsWith(best, StringComparison.Ordinal));
        }

        return themes;
    }

    /// <summary>
    /// Thème d'une note, parmi un vocabulaire connu. On retient le plus long
    /// préfixe qui correspond, pour que « plexopro » l'emporte sur « plexo ».
    /// </summary>
    public static string? Match(string note, IEnumerable<string> vocabulary)
    {
        var head = RawHeadingLine(note)?.ToLowerInvariant();
        if (head is null) return null;

        return vocabulary
            .Where(theme => head.StartsWith(theme, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(theme => theme.Length)
            .FirstOrDefault();
    }
}
