# Architecture

*Version française : [ARCHITECTURE.fr.md](ARCHITECTURE.fr.md).*

## The guiding constraint

The tool runs permanently on the machine. **It must cost nothing at rest.**
Everything else follows from that.

## No polling

A loop checking every N seconds whether Notepad is running is waste. Windows
can wake a process on an event.

```
        ┌─────────────────────────────────────────┐
        │  Notepad not running                    │
        │  → wait on a FileSystemWatcher          │
        │    over TabState\                       │
        │  → 0% CPU, the kernel wakes us          │
        └──────────────┬──────────────────────────┘
                       │ Notepad starts / writes
                       ▼
        ┌─────────────────────────────────────────┐
        │  Notepad running                        │
        │  → OpenProcess(SYNCHRONIZE)             │
        │  → WaitForSingleObject(handle, INFINITE)│
        │  → 0% CPU, the thread sleeps            │
        └──────────────┬──────────────────────────┘
                       │ Notepad closes
                       ▼
        ┌─────────────────────────────────────────┐
        │  Burst of work (1–2 s)                  │
        │  → wait for the locks to drop           │
        │  → back up, parse, file, write          │
        └──────────────┬──────────────────────────┘
                       │
                       └──> back to waiting
```

`WaitForSingleObject` on a process handle is exactly the "callback on close"
one would want: the thread is descheduled, burns no cycles, and is woken the
millisecond the process dies.

In .NET: `Process.WaitForExit()` / `Process.Exited`, which build on it. Note
that Notepad may have several processes — wait for the **last** one.

## Sequence on close

```
1. Notepad is gone
2. Wait ~500 ms for handles to drop
3. CHECK that no Notepad process came back        ← otherwise abort
4. Back up LocalState\ (rotating the last N)
5. Parse TabState\
      - skip flag=01 tabs (backed by real files)
      - skip tabs whose layout does not verify
6. Diff against the state file: which notes are new or modified?
7. File only the new ones
8. For each touched theme:
      - read the container's .bin
      - append the new content
      - rewrite the .bin (varints + CRC recomputed)
      - delete the container's .0.bin / .1.bin
9. Delete the absorbed notes' .bin
10. Update the state file
```

Step 3 is not cosmetic: if Notepad restarts while we work, it takes the files
back and everything is lost.

## Abrupt shutdown, crash, Windows update

If the machine shuts down without Notepad being closed, Windows kills Notepad
— and usually our service too, before it can act. Notes are left unfiled and
nobody noticed.

**No special case is needed.** The state file makes pending work declaratively
detectable: anything in `TabState` that does not match the recorded state is
pending, whatever the cause.

So the service runs a **reconciliation pass at every start**, before waiting:

```
service starts
  → is Notepad already running?
        yes → do nothing, go straight to waiting for it to close
        no  → reconciliation pass, then wait
```

The first branch is not theoretical: Windows can relaunch Notepad on sign-in
through "Restore apps after restart". Reconciliation therefore obeys the same
guards as everything else.

Two pleasant consequences: the service is **restartable at any time** without
losing work, and the startup path and the nominal path are the **same code**,
so the same tests cover both.

An abrupt kill can also leave the `.0/.1` state records out of sync with the
`.bin`. That is already covered: any file whose layout does not add up is
refused rather than rewritten.

## Idempotence

Without state, cycle 2 re-merges what cycle 1 merged, and content grows on
every open.

State file, next to the configuration:

```json
{
  "containers": {
    "28e8c91b-7b1e-4155-a174-f6b1b743dc06": {
      "theme": "project-alpha",
      "contentHash": "sha256:…",
      "lastMerged": "2026-08-10T18:56:56Z"
    }
  },
  "absorbed": ["b9bd4408-d11d-4eab-a4e2-edad2380814e"]
}
```

Rules:

- A note whose GUID is in `absorbed` and which reappears is ignored.
- A container whose `contentHash` no longer matches was **edited by hand**: we
  still append, we never regenerate from scratch. Manual edits are sacred.

## Data safety

These notes exist nowhere else. The rules:

1. **Back up before any write.** Not negotiable, not disableable.
2. **Refuse by default.** Unverified layout, unexpected magic, invalid CRC on
   read → the file is left alone and the event is logged.
3. **First-class dry run.** It must display everything without opening a
   single file for writing.
4. **Never touch flag=01 tabs.** They are real user files.
5. **Abort if Notepad comes back.** Checked before every write.

## Layout

```
NotepadTidy.Core/
    Crc32              the checksum, isolated and stateless
    TabRecord          parse + build a tab — pure, no I/O
    TabPaths           path resolution — pure, no I/O
    TabStore           reading and enumerating TabState
    TabMerger          merging: the only code that destroys data
    NoteHeading        explicit "#" heading
    ThemeVocabulary    theme discovery by shared prefix
    ThemeMention       fallback filing by mention in the body
    CorpusProfile      corpus statistics — diagnostics
    NoteSignals        optional, language-specific signals — diagnostics
    IO/                ITabFileSystem, INotepadGuard + Windows implementations
NotepadTidy.Service/    event-driven watcher, orchestration
NotepadTidy.Cli/        stats, analyze, themes, list, dump, backup, merge
```

`Core` has no external dependency. Most of it is pure functions, testable
without a disk and without Windows.

### Why the `IO/` interfaces

They do not exist to satisfy a principle, but for a precise reason:
`TabMerger` deletes unrecoverable files. Behind `ITabFileSystem` and
`INotepadGuard`, its tests run in memory and can assert that a refusal wrote
and deleted **nothing** — impossible with static calls to `File` and
`Process`.

It is the only abstraction in the project. Everything else is concrete by
default.

### Resistance to Notepad updates

Header byte 4 is `01` across the whole reference corpus and its meaning is
unknown. It serves as a version sentinel: any other value sends the file to
`TabStatus.UnknownVariant`, and anything that is not `TabStatus.Ok` is refused
for writing.

If a Windows update changes the format, the default behaviour is
**abstention**, never a shifted parse that would destroy notes. Supporting a
new variant will mean extending the detection, not rewriting the parser.

## Publishing

```
dotnet publish -c Release -r win-x64 /p:PublishAot=true
```

NativeAOT: a self-contained `.exe`, no .NET runtime to install, near-instant
startup and a small memory footprint.
