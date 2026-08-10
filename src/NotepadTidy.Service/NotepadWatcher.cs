using System.Diagnostics;
using NotepadTidy.Core;

namespace NotepadTidy.Service;

/// <summary>
/// Waits for Notepad to close, then tidies — without polling.
///
/// <para>Two waits, both free at rest. When Notepad runs we wait on its process
/// handle: the thread is descheduled and the kernel wakes it the millisecond
/// the process dies. When Notepad is absent we wait on a FileSystemWatcher over
/// TabState, which is a kernel notification, not a loop.</para>
///
/// <para>Neither burns CPU while idle, which is the whole point: this runs
/// permanently on the user's machine.</para>
/// </summary>
public sealed class NotepadWatcher(TidyRunner runner, Action<string> log)
{
    private const string ProcessName = "Notepad";

    /// <summary>Time for Notepad's file handles to be released after it exits.</summary>
    private static readonly TimeSpan HandleSettleDelay = TimeSpan.FromMilliseconds(700);

    /// <summary>
    /// Idle wake-up. Not polling — merely a ceiling so a missed file event
    /// cannot strand the service forever.
    /// </summary>
    private static readonly TimeSpan IdleCeiling = TimeSpan.FromMinutes(30);

    public void Run(string tabStateDir, CancellationToken cancel)
    {
        log($"watching {tabStateDir}");

        // Reconciliation on start. Work may be pending from an abrupt shutdown,
        // a crash, or a service stopped by hand — the state on disk says so, and
        // the cause does not matter.
        if (!AnyRunning())
            Tidy("startup reconciliation");
        else
            log("Notepad already running — waiting for it to close");

        while (!cancel.IsCancellationRequested)
        {
            if (AnyRunning())
            {
                WaitForAllToExit(cancel);
                if (cancel.IsCancellationRequested) break;

                Thread.Sleep(HandleSettleDelay);

                // Notepad may have been reopened during the settle delay.
                if (AnyRunning()) { log("Notepad came back — standing down"); continue; }

                Tidy("Notepad closed");
            }
            else
            {
                WaitForTabStateActivity(tabStateDir, cancel);
            }
        }

        log("stopped");
    }

    private static bool AnyRunning() => Process.GetProcessesByName(ProcessName).Length > 0;

    /// <summary>
    /// Waits for every Notepad process to exit. Notepad can have several, and
    /// tidying while one survives would lose the work.
    /// </summary>
    private static void WaitForAllToExit(CancellationToken cancel)
    {
        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            using (process)
            {
                try { process.WaitForExit(); }
                catch (SystemException) { /* already gone between listing and waiting */ }
            }
            if (cancel.IsCancellationRequested) return;
        }
    }

    /// <summary>
    /// Sleeps until something happens in TabState — which means Notepad started
    /// or wrote. The kernel does the waiting.
    /// </summary>
    private static void WaitForTabStateActivity(string tabStateDir, CancellationToken cancel)
    {
        using var signal = new ManualResetEventSlim(false);
        using var watcher = new FileSystemWatcher(tabStateDir)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };

        void Wake(object? _, FileSystemEventArgs __) => signal.Set();
        watcher.Changed += Wake;
        watcher.Created += Wake;
        watcher.Deleted += Wake;
        watcher.Renamed += (_, __) => signal.Set();

        try { signal.Wait(IdleCeiling, cancel); }
        catch (OperationCanceledException) { }
    }

    private void Tidy(string reason)
    {
        try
        {
            var plan = runner.Plan();
            if (!plan.HasWork)
            {
                log($"{reason}: nothing to merge ({plan.UsableNotes} notes, {plan.ThemeCount} themes)");
                return;
            }

            var outcome = runner.Run(plan);
            log($"{reason}: {outcome.Message}" +
                (outcome.BackupPath is null ? "" : $" backup: {outcome.BackupPath}"));
        }
        catch (Exception ex)
        {
            // A background service must never take the user's session down with
            // it. Log, stay alive, try again on the next close.
            log($"{reason}: FAILED — {ex.GetType().Name}: {ex.Message}");
        }
    }
}
