using System.Text;
using NotepadTidy.Core;

Console.OutputEncoding = Encoding.UTF8;

var store = new TabStore();

if (!store.Exists)
{
    Console.Error.WriteLine($"TabState introuvable : {store.TabStateDir}");
    return 2;
}

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "stats";

switch (command)
{
    case "stats": return Stats(store);
    case "list": return List(store);
    case "dump": return Dump(store, args);
    case "backup": return Backup(store, args);
    case "merge": return Merge(store, args);
    default:
        Console.WriteLine("""
            nptidy — outil de rangement des onglets Notepad

              stats                       état de santé du TabState
              list                        liste les onglets avec un aperçu
              dump <guid>                 contenu et en-tête d'un onglet
              backup <dossier>            copie intégrale de LocalState
              merge <cible> <src...>      fusionne des notes (ajouter --apply pour écrire)

            Sans --apply, merge ne fait qu'afficher ce qu'il ferait.
            """);
        return 1;
}

static int Stats(TabStore store)
{
    var records = store.ReadAll().ToList();
    var byStatus = records.GroupBy(r => r.Status)
                          .ToDictionary(g => g.Key, g => g.Count());

    Console.WriteLine($"TabState : {store.TabStateDir}");
    Console.WriteLine($"Notepad tourne : {(TabStore.IsNotepadRunning() ? "OUI (écriture interdite)" : "non")}");
    Console.WriteLine($"Onglets : {records.Count}");
    Console.WriteLine();

    foreach (var status in Enum.GetValues<TabStatus>())
    {
        if (!byStatus.TryGetValue(status, out int n)) continue;
        var note = status switch
        {
            TabStatus.Ok => "réécriture sûre",
            TabStatus.FileBacked => "vrais fichiers — ne pas toucher",
            TabStatus.LayoutMismatch => "variante de format non gérée — à ignorer",
            TabStatus.BadCrc => "corrompus ou format modifié",
            _ => "",
        };
        Console.WriteLine($"  {status,-15} {n,4}   {note}");
    }

    var safe = records.Where(r => r.IsSafeToRewrite).ToList();
    Console.WriteLine();
    Console.WriteLine($"Exploitables : {safe.Count} onglets, {safe.Sum(r => r.Text.Length):N0} caractères");
    return 0;
}

static int List(TabStore store)
{
    foreach (var r in store.ReadAll().OrderByDescending(r => r.Text.Length))
    {
        var preview = r.Text.ReplaceLineEndings(" ").Trim();
        if (preview.Length > 60) preview = preview[..60] + "…";
        var flag = r.IsSafeToRewrite ? " " : "!";
        Console.WriteLine($"{flag} {r.Id}  {r.Text.Length,6}c  {r.Status,-14}  {preview}");
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

    var path = store.PathFor(id);
    if (!File.Exists(path)) { Console.Error.WriteLine($"introuvable : {path}"); return 2; }

    var record = TabRecord.Parse(id, TabStore.ReadShared(path));
    Console.WriteLine($"GUID          {record.Id}");
    Console.WriteLine($"Statut        {record.Status}");
    Console.WriteLine($"Taille        {record.FileLength} octets");
    Console.WriteLine($"Longueur      {record.DeclaredLength} caractères");
    Console.WriteLine($"Texte @       offset {record.TextOffset}");
    Console.WriteLine($"Curseur       {record.CursorStart}/{record.CursorEnd}");
    Console.WriteLine(new string('-', 60));
    Console.WriteLine(record.Text);
    Console.WriteLine(new string('-', 60));
    return 0;
}

static int Backup(TabStore store, string[] args)
{
    if (args.Length < 2) { Console.Error.WriteLine("usage : nptidy backup <dossier>"); return 1; }
    int n = store.Backup(args[1]);
    Console.WriteLine($"{n} fichiers copiés vers {args[1]}");
    return 0;
}

static int Merge(TabStore store, string[] args)
{
    bool apply = args.Contains("--apply");
    var ids = args.Skip(1).Where(a => !a.StartsWith("--"))
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
        Console.WriteLine($"[simulation] conteneur {container}");
        var target = TabRecord.Parse(container, TabStore.ReadShared(store.PathFor(container)));
        Console.WriteLine($"  état actuel : {target.Status}, {target.Text.Length} caractères");
        int total = target.Text.Length;
        foreach (var id in sources)
        {
            var r = TabRecord.Parse(id, TabStore.ReadShared(store.PathFor(id)));
            Console.WriteLine($"  + {id}  {r.Text.Length,6}c  {r.Status}");
            total += r.Text.Length + separator.Length;
        }
        Console.WriteLine($"  résultat : {total} caractères, {sources.Count} notes absorbées");
        Console.WriteLine("Rien n'a été écrit. Ajouter --apply pour appliquer.");
        return 0;
    }

    var result = store.Merge(container, sources, separator);
    if (!result.Ok) { Console.Error.WriteLine($"refusé : {result.Message}"); return 3; }

    Console.WriteLine($"Fusionné : {result.Absorbed} notes absorbées, {result.Chars} caractères, {result.Bytes} octets");
    return 0;
}
