using System.Text;
using NotepadTidy.Core;
using NotepadTidy.Core.IO;

Console.OutputEncoding = Encoding.UTF8;

// --path permet de travailler sur une copie isolée plutôt que sur le vrai
// dossier de Notepad. C'est le mode recommandé pour tout essai.
var pathOption = ReadOption(args, "--path");
var paths = pathOption is null ? TabPaths.Default() : new TabPaths(pathOption);
var store = new TabStore(paths, new WindowsTabFileSystem());

// Sur un bac à sable, Notepad ne peut rien écraser : attendre sa fermeture
// n'aurait aucun sens. Sur le vrai dossier, la protection est obligatoire.
INotepadGuard guard = paths.IsRealNotepadState
    ? new NotepadProcessGuard()
    : new SandboxGuard();

if (!Directory.Exists(store.Paths.TabStateDir))
{
    Console.Error.WriteLine($"TabState introuvable : {store.Paths.TabStateDir}");
    return 2;
}

if (!paths.IsRealNotepadState)
    Console.WriteLine($"[bac à sable] {paths.LocalState}{Environment.NewLine}");

var command = args.Length > 0 && !args[0].StartsWith("--") ? args[0].ToLowerInvariant() : "stats";

switch (command)
{
    case "stats": return Stats(store, guard);
    case "analyze": return Analyze(store);
    case "themes": return Themes(store);
    case "list": return List(store);
    case "dump": return Dump(store, args);
    case "backup": return Backup(store, args);
    case "merge": return Merge(store, guard, args);
    default:
        Console.WriteLine("""
            nptidy — outil de rangement des onglets Notepad

              stats                       état de santé du TabState
              list                        liste les onglets avec un aperçu
              dump <guid>                 contenu et en-tête d'un onglet
              backup <dossier>            copie de TabState et WindowState
              merge <cible> <src...>      fusionne des notes

            Options :
              --path <dossier>            travailler sur une copie isolée
                                          plutôt que sur le vrai Notepad
              --apply                     appliquer réellement la fusion

            Sans --apply, merge n'écrit rien.

            Pour essayer sans risque :
              nptidy backup C:\bac-a-sable
              nptidy list --path C:\bac-a-sable
            """);
        return 1;
}

static string? ReadOption(string[] args, string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static int Stats(TabStore store, INotepadGuard guard)
{
    var records = store.ReadAll().ToList();
    var byStatus = records.GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.Count());

    Console.WriteLine($"TabState : {store.Paths.TabStateDir}");
    Console.WriteLine($"Notepad tourne : {(guard.IsRunning ? "OUI (écriture interdite)" : "non")}");
    Console.WriteLine($"Onglets : {records.Count}");
    Console.WriteLine();

    foreach (var status in Enum.GetValues<TabStatus>())
    {
        if (!byStatus.TryGetValue(status, out int n)) continue;
        var note = status switch
        {
            TabStatus.Ok => "réécriture sûre",
            TabStatus.FileBacked => "vrais fichiers — ne pas toucher",
            TabStatus.LayoutMismatch => "layout non vérifié — ignorés",
            TabStatus.UnknownVariant => "variante inconnue — Notepad a peut-être changé",
            TabStatus.BadCrc => "corrompus",
            _ => "",
        };
        Console.WriteLine($"  {status,-16} {n,4}   {note}");
    }

    var safe = records.Where(r => r.IsSafeToRewrite).ToList();
    Console.WriteLine();
    Console.WriteLine($"Exploitables : {safe.Count} onglets, {safe.Sum(r => r.Text.Length):N0} caractères");
    return 0;
}

/// <summary>
/// Mesure les signaux exploitables du corpus, sans afficher aucun contenu.
/// C'est l'outil de décision : il dit quels axes de classement ont de la
/// matière avant d'écrire la moindre ligne de classifieur.
/// </summary>
static int Analyze(TabStore store)
{
    var notes = store.ReadAll().Where(r => r.IsSafeToRewrite).Select(r => r.Text).ToList();
    if (notes.Count == 0) { Console.Error.WriteLine("aucune note exploitable"); return 2; }

    int drafts = notes.Count(NoteSignals.LooksLikeMessageDraft);
    int epistolary = notes.Count(NoteSignals.HasEpistolaryMarker);
    int technical = notes.Count(NoteSignals.HasTechnicalMarker);
    int withUrl = notes.Count(n => NoteSignals.UrlCount(n) > 0);
    int linkDumps = notes.Count(NoteSignals.IsMostlyLinks);

    Console.WriteLine($"Notes analysées : {notes.Count}");
    Console.WriteLine();
    Console.WriteLine("— Type détectable sans modèle —");
    Console.WriteLine($"  brouillon de message (salutation en tête)  {drafts,4}  {Pct(drafts, notes.Count)}");
    Console.WriteLine($"  registre épistolaire (politesse, @)        {epistolary,4}  {Pct(epistolary, notes.Count)}");
    Console.WriteLine($"  marqueurs techniques                       {technical,4}  {Pct(technical, notes.Count)}");
    Console.WriteLine($"  contient au moins une URL                  {withUrl,4}  {Pct(withUrl, notes.Count)}");
    Console.WriteLine($"  presque uniquement des liens               {linkDumps,4}  {Pct(linkDumps, notes.Count)}");

    // Combien de notes déclarent déjà leur thème en première ligne ? C'est le
    // seul mécanisme de classement indépendant de la langue.
    var explicitly = notes.Select(NoteHeading.ExtractExplicit).Where(h => h is not null).ToList();
    var guessed = notes.Select(NoteHeading.GuessImplicit).Where(h => h is not null).ToList();
    Console.WriteLine();
    Console.WriteLine("— Titre en première ligne —");
    Console.WriteLine($"  titre explicite (# en tête)                {explicitly.Count,4}  {Pct(explicitly.Count, notes.Count)}");
    Console.WriteLine($"  première ligne DEVINÉE comme titre         {guessed.Count,4}  {Pct(guessed.Count, notes.Count)}");
    if (guessed.Count > 0)
    {
        Console.WriteLine("  ce que la devinette proposerait comme thèmes :");
        foreach (var h in guessed.Select(h => NoteHeading.Normalize(h!))
                                 .Where(h => h.Length > 0)
                                 .Distinct(StringComparer.OrdinalIgnoreCase).Take(8))
            Console.WriteLine($"    {h}");
        Console.WriteLine("  (à inspecter : la devinette produit surtout des faux positifs)");
    }

    var lengths = notes.Select(n => n.Length).OrderBy(x => x).ToList();
    Console.WriteLine();
    Console.WriteLine("— Longueurs —");
    Console.WriteLine($"  médiane {lengths[lengths.Count / 2]}  moyenne {lengths.Average():N0}  max {lengths[^1]}");
    Console.WriteLine($"  notes de moins de 80 caractères : {lengths.Count(l => l < 80)}  (peu de signal sémantique)");

    // Rien de ce qui suit n'utilise de liste codée en dur : tout est dérivé du
    // corpus, donc transposable à un autre utilisateur et à une autre langue.
    var profile = new CorpusProfile(notes);

    Console.WriteLine();
    Console.WriteLine($"— Mots vides DÉDUITS du corpus ({profile.StopWords.Count}) —");
    Console.WriteLine("  " + string.Join(", ", profile.StopWords
        .OrderByDescending(profile.DocumentFrequency).Take(14)));

    Console.WriteLine();
    Console.WriteLine($"— Mots d'ouverture DÉDUITS ({profile.OpeningWords.Count}) —");
    Console.WriteLine("  " + (profile.OpeningWords.Count > 0
        ? string.Join(", ", profile.OpeningWords.OrderByDescending(profile.DocumentFrequency))
        : "aucun"));
    Console.WriteLine($"  notes s'ouvrant ainsi : {notes.Count(profile.OpensLikeCorrespondence)}");

    Console.WriteLine();
    Console.WriteLine("— Mots les plus discriminants, par TF-IDF —");
    var best = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    foreach (var note in notes)
        foreach (var (token, score) in profile.DistinctiveTokens(note, 6))
            if (score > best.GetValueOrDefault(token)) best[token] = score;
    foreach (var (token, score) in best.OrderByDescending(k => k.Value).Take(20))
        Console.WriteLine($"  {token,-24} {score,6:N1}   ({profile.DocumentFrequency(token)} notes)");

    // Noms propres : un mot capitalisé en milieu de phrase n'est pas un nom
    // commun français. C'est le filtre qui sépare les projets du vocabulaire.
    var proper = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    foreach (var note in notes)
        foreach (var p in NoteSignals.ProperNouns(note).Distinct(StringComparer.OrdinalIgnoreCase))
            proper[p] = proper.GetValueOrDefault(p) + 1;

    Console.WriteLine();
    Console.WriteLine("— Noms propres récurrents (candidats projet / interlocuteur) —");
    foreach (var (token, count) in proper.Where(k => k.Value >= 2)
                                         .OrderByDescending(k => k.Value).Take(25))
        Console.WriteLine($"  {token,-24} {count,3} notes");
    Console.WriteLine($"  ... {proper.Count(k => k.Value == 1)} noms propres n'apparaissent que dans 1 note");

    var domains = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    foreach (var note in notes)
        foreach (var d in NoteSignals.Domains(note).Distinct(StringComparer.OrdinalIgnoreCase))
            domains[d] = domains.GetValueOrDefault(d) + 1;

    if (domains.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("— Domaines cités —");
        foreach (var (d, c) in domains.OrderByDescending(k => k.Value).Take(12))
            Console.WriteLine($"  {d,-32} {c,3} notes");
    }

    return 0;

    static string Pct(int n, int total) => $"({100.0 * n / total,5:N1} %)";
}

/// <summary>
/// Groups notes by explicit heading. Grouping is insensitive to
/// case: "#Project" and "#project" denote the same theme.
/// </summary>
static int Themes(TabStore store)
{
    var records = store.ReadAll().Where(r => r.IsSafeToRewrite).ToList();

    // The theme is often glued to the content ("#themeHi!"), so it cannot be
    // delimited note by note. It is inferred from shared prefixes instead.
    var vocabulary = ThemeVocabulary.Discover(records.Select(r => r.Text));

    var groups = new Dictionary<string, List<Guid>>(StringComparer.OrdinalIgnoreCase);
    var untitled = new List<Guid>();

    var byMention = new List<(Guid Id, string Theme, string Why)>();

    foreach (var record in records)
    {
        var theme = ThemeVocabulary.Match(record.Text, vocabulary);

        // Pas de titre : on cherche un thème DÉJÀ déclaré dans le corps. On
        // n'en invente jamais un nouveau — le vocabulaire reste celui de
        // l'utilisateur.
        if (theme is null)
        {
            var mention = ThemeMention.FindInBody(record.Text, vocabulary);
            if (!mention.Found) { untitled.Add(record.Id); continue; }
            theme = mention.Theme!;
            byMention.Add((record.Id, theme, $"{mention.Hits} mention(s)"));
        }

        if (!groups.TryGetValue(theme, out var list)) groups[theme] = list = [];
        list.Add(record.Id);
    }

    int titled = records.Count - untitled.Count - byMention.Count;
    Console.WriteLine($"Notes exploitables : {records.Count}");
    Console.WriteLine($"  titre « # » explicite     : {titled}");
    Console.WriteLine($"  rattachées par mention    : {byMention.Count}");
    Console.WriteLine($"  non classées              : {untitled.Count}");
    if (byMention.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("— Rattachements par mention dans le corps —");
        foreach (var (id, theme, why) in byMention)
            Console.WriteLine($"  {id.ToString()[..8]}  → {theme,-16} ({why})");
    }
    Console.WriteLine();
    Console.WriteLine($"— {groups.Count} thèmes —");
    foreach (var (theme, ids) in groups.OrderByDescending(g => g.Value.Count).ThenBy(g => g.Key))
        Console.WriteLine($"  {theme,-28} {ids.Count,3} note{(ids.Count > 1 ? "s" : "")}");

    if (untitled.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("— Notes sans titre exploitable (aperçu de la 1re ligne) —");
        foreach (var id in untitled.Take(20))
        {
            var text = records.First(r => r.Id == id).Text;
            var first = text.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
            if (first.Length > 52) first = first[..52] + "…";
            Console.WriteLine($"  {id.ToString()[..8]}  {first}");
        }
    }
    return 0;
}

static int List(TabStore store)
{
    foreach (var r in store.ReadAll().OrderByDescending(r => r.Text.Length))
    {
        var preview = r.Text.ReplaceLineEndings(" ").Trim();
        if (preview.Length > 60) preview = preview[..60] + "…";
        Console.WriteLine($"{(r.IsSafeToRewrite ? " " : "!")} {r.Id}  {r.Text.Length,6}c  {r.Status,-16}  {preview}");
    }
    return 0;
}

static int Dump(TabStore store, string[] args)
{
    if (args.Length < 2 || !Guid.TryParse(args[1], out var id))
    {
        Console.Error.WriteLine("usage : nptidy dump <guid>");
        return 1;
    }
    if (!store.FileSystem.FileExists(store.Paths.TabFile(id)))
    {
        Console.Error.WriteLine($"introuvable : {store.Paths.TabFile(id)}");
        return 2;
    }

    var record = store.Read(id);
    Console.WriteLine($"GUID       {record.Id}");
    Console.WriteLine($"Statut     {record.Status}");
    Console.WriteLine($"Taille     {record.FileLength} octets");
    Console.WriteLine($"Longueur   {record.DeclaredLength} caractères");
    Console.WriteLine($"Texte @    offset {record.TextOffset}");
    Console.WriteLine($"Curseur    {record.CursorStart}/{record.CursorEnd}");
    Console.WriteLine(new string('-', 60));
    Console.WriteLine(record.Text);
    Console.WriteLine(new string('-', 60));
    return 0;
}

static int Backup(TabStore store, string[] args)
{
    if (args.Length < 2) { Console.Error.WriteLine("usage : nptidy backup <dossier>"); return 1; }
    Console.WriteLine($"{store.Backup(args[1])} fichiers copiés vers {args[1]}");
    return 0;
}

static int Merge(TabStore store, INotepadGuard guard, string[] args)
{
    bool apply = args.Contains("--apply");
    // On ne garde que les GUID : les options et leurs valeurs sont écartées.
    var ids = args.Skip(1)
                  .Select(a => Guid.TryParse(a, out var g) ? g : Guid.Empty)
                  .Where(g => g != Guid.Empty).ToList();

    if (ids.Count < 2)
    {
        Console.Error.WriteLine("usage : nptidy merge <cible> <source...> [--apply]");
        return 1;
    }

    var container = ids[0];
    var sources = ids.Skip(1).ToList();
    var separator = $"{Environment.NewLine}{Environment.NewLine}--- fusionné le {DateTime.Now:yyyy-MM-dd} ---{Environment.NewLine}";

    if (!apply)
    {
        var target = store.Read(container);
        Console.WriteLine($"[simulation] conteneur {container} — {target.Status}, {target.Text.Length} caractères");
        int total = target.Text.Length;
        foreach (var id in sources)
        {
            var r = store.Read(id);
            Console.WriteLine($"  + {id}  {r.Text.Length,6}c  {r.Status}");
            total += r.Text.Length + separator.Length;
        }
        Console.WriteLine($"  résultat : {total} caractères, {sources.Count} notes absorbées");
        Console.WriteLine("Rien n'a été écrit. Ajouter --apply pour appliquer.");
        return 0;
    }

    var result = new TabMerger(store, guard).Merge(container, sources, separator);
    if (!result.Ok)
    {
        Console.Error.WriteLine($"refusé ({result.Refusal}) : {result.Message}");
        return 3;
    }

    Console.WriteLine($"Fusionné : {result.Absorbed} notes, {result.Chars} caractères, {result.Bytes} octets");
    return 0;
}
