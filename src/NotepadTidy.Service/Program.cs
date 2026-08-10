using System.Text;
using NotepadTidy.Core;
using NotepadTidy.Core.IO;
using NotepadTidy.Service;

Console.OutputEncoding = Encoding.UTF8;

// --path lets the service run against an isolated copy, which is how it gets
// exercised without touching the real notes.
var pathOption = ReadOption(args, "--path");
var paths = pathOption is null ? TabPaths.Default() : new TabPaths(pathOption);
var store = new TabStore(paths, new WindowsTabFileSystem());

INotepadGuard guard = paths.IsRealNotepadState
    ? new NotepadProcessGuard()
    : new SandboxGuard();

if (!Directory.Exists(paths.TabStateDir))
{
    Console.Error.WriteLine($"TabState not found: {paths.TabStateDir}");
    return 2;
}

var logPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "notepad-tidy", "service.log");
Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

void Log(string message)
{
    var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}";
    Console.WriteLine(line);
    try { File.AppendAllText(logPath, line + Environment.NewLine); }
    catch (IOException) { /* logging must never break the service */ }
}

var runner = new TidyRunner(store, guard);

// --dry-run reports what a pass would do and exits. The safe way to see the
// service's judgement before letting it write anything.
if (args.Contains("--dry-run"))
{
    var plan = runner.Plan();
    Console.WriteLine($"{plan.UsableNotes} usable notes, {plan.ThemeCount} themes, {plan.Unfiled} unfiled");
    foreach (var group in plan.Groups)
        Console.WriteLine($"  {group.Theme,-20} absorbs {group.Sources.Count,3} → container {group.Container.ToString()[..8]}");
    Console.WriteLine($"Tabs {plan.UsableNotes} → {plan.TabsAfter}. Nothing was written.");
    return 0;
}

// --once runs a single pass and exits, for a scheduled task or a manual run.
if (args.Contains("--once"))
{
    var plan = runner.Plan();
    var outcome = runner.Run(plan);
    Log($"single pass: {outcome.Message}");
    return outcome.Refused > 0 ? 4 : 0;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };

Log($"service started, pid {Environment.ProcessId}");
new NotepadWatcher(runner, Log).Run(paths.TabStateDir, cancellation.Token);
return 0;

static string? ReadOption(string[] args, string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
