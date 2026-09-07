# notepad-tidy

Automatically tidies the tabs of Windows 11 Notepad.

Notepad keeps your notes without ever asking you to save them — its best
feature, and exactly why you end up with a hundred loose tabs. `notepad-tidy`
runs in the background, waits until you close Notepad, groups notes by theme
and merges them into a handful of tabs — **which remain unsaved tabs**. Reopen
Notepad and your notes are tidy, with the same comfort as before.

No categories to declare. You name a theme by writing it, once.

*Une version française de cette documentation est disponible :
[README.fr.md](README.fr.md).*

## Project status

Reverse engineering of the on-disk format is **complete and validated**
against a real corpus of 106 tabs. Reading, writing and merging work end to
end, and the background service tidies on close.

| Component | Status |
|---|---|
| TabState format parser | ✅ 100/100 tabs, no mismatch |
| CRC32 (read + generate) | ✅ validated on 106 files |
| Writer — Notepad accepts our files | ✅ verified in real conditions |
| Note merging | ✅ working CLI |
| Theme discovery and filing | ✅ 80/82 notes filed on the reference corpus |
| Event-driven watcher | ✅ background service, 0% CPU at rest |
| Installer | ✅ `install.ps1`, no administrator rights |

## Why this is not a plugin

Notepad 11 is a sandboxed Store application. No extension API, no hook. The
only workable route is an external tool that manipulates the state files while
Notepad is closed.

The whole format is documented in [`docs/FORMAT.md`](docs/FORMAT.md) — it is
the core of the project and its most reusable result.

## Installation

Requires Windows 11 and the .NET 10 SDK. There is no prebuilt release yet, so
build the two executables and let the installer place them:

```powershell
dotnet publish src/NotepadTidy.Cli/NotepadTidy.Cli.csproj -c Release -r win-x64 -p:PublishAot=true -o dist
dotnet publish src/NotepadTidy.Service/NotepadTidy.Service.csproj -c Release -r win-x64 -p:PublishAot=true -o dist
.\install.ps1 -Source .\dist
```

`install.ps1` copies the binaries to `%LOCALAPPDATA%\notepad-tidy\bin` and
registers a scheduled task that starts the service at logon. **No
administrator rights are needed** — the service only ever touches the current
user's own Notepad folder.

To remove it, `.\uninstall.ps1`. Your notes are left exactly as they are.

The full guide — NativeAOT prerequisites, checking on the service, reading its
log, restoring a backup — is in [`docs/INSTALL.md`](docs/INSTALL.md).

## Getting started

To work on the code rather than install it:

```bash
dotnet build -c Release
dotnet test
```

### Work on a copy, not on your real notes

This is the recommended mode for any experiment. `--path` makes the tool
operate on an isolated copy of the TabState folder:

```bash
nptidy backup C:\sandbox            # copies TabState and WindowState
nptidy stats  --path C:\sandbox
nptidy merge  <target> <src> --path C:\sandbox --apply
```

Notepad only knows its own folder: it cannot overwrite anything in the
sandbox, and nothing you do there reaches your real notes. You can therefore
work **while Notepad is open**.

`--path` is not a back door: if the given path denotes Notepad's real folder —
whatever the casing, a trailing separator or a `..` detour — the process
guards stay active. That is covered by tests.

### Commands

```bash
nptidy stats                        # health of the TabState folder
nptidy analyze                      # measure the signals in your corpus
nptidy themes                       # group notes by theme
nptidy list                         # list tabs with a preview
nptidy dump <guid>                  # content and header of one tab
nptidy backup <folder>
nptidy merge <target> <src...>      # dry run, writes nothing
nptidy merge <target> <src> --apply
```

## How filing works

### 1. Explicit heading

A note whose first line starts with `#` declares its theme.

```
# project beta
the link for the testers
```

→ theme `project-beta`, created on the fly.

This is the only mechanism that is both administration-free and **completely
language-independent**: the category is not guessed, it is declared, in the
user's own words. `# Rechnungen`, `# 仕事のメモ` and `# работа` behave
identically.

The marker is required rather than inferred. Measured on a real corpus:
guessing a heading from the shape of the first line yields 11% detections of
which **every single one is a false positive** — colour codes, port numbers,
schedules, passwords. One character removes the ambiguity entirely.

### 2. Filing by mention

A note without a heading is filed into a theme the user **already declared
elsewhere**. A new theme is never invented.

It is deliberately conservative, because a wrong filing costs more than an
unfiled note:

- whole-token comparison, so `play` does not match inside `display`;
- a multi-word theme only counts when all of its tokens appear in sequence;
- themes shorter than four characters are ignored as mentions;
- outright refusal when two themes are cited comparably, with a required
  margin of 2×.

### 3. No automatic theme creation

Deliberately dropped. It invented names the user had not chosen, which was
precisely the problem. `CorpusProfile` and `NoteSignals` remain as diagnostics
behind `nptidy analyze`, not as the filing backbone.

## Data safety

These notes exist nowhere else — that is the whole point of unsaved tabs. So:

- **Backup before any write**, not optional.
- **Refuse by default**: unexpected magic, a layout that does not add up to
  the byte, or an invalid CRC → the file is left alone.
- **First-class dry run**: `merge` without `--apply` opens no file for writing.
- **Tabs backed by a real file are never modified.**
- **Abort if Notepad is running**, checked before and during the operation.

Copy
`%LOCALAPPDATA%\Packages\Microsoft.WindowsNotepad_8wekyb3d8bbwe\LocalState`
before your first experiments, and develop against that copy.

> ⚠️ The backups under `%LOCALAPPDATA%\notepad-tidy\backups` hold the **full
> text of your notes**, and `uninstall.ps1` keeps them on purpose — they are
> the only way back from an unwanted merge. Pass `-RemoveBackups` when you
> want them gone.

> ⚠️ Never commit real notes to this repository. They easily contain API keys,
> draft e-mails and private links. The `.gitignore` blocks `*.bin`, but
> vigilance stays manual.

## Tests

124 tests. About a third cover `TabMerger`, the only code that deletes files.
It sits behind an `ITabFileSystem`, so its tests run entirely in memory and
never approach a real TabState folder.

What is pinned down, in order of importance:

- **The refusals.** Notepad running, Notepad returning mid-operation, a
  file-backed container, a source with a bad CRC, an unknown format variant.
  In each case the tests assert that **nothing** was written or deleted.
- **All or nothing.** A single doubtful source cancels the whole operation.
- **Idempotence.** A second pass does not grow the container.
- **Malformed input.** Every truncated prefix of a valid tab, a varint that
  never terminates, an absurd declared length — each is refused cleanly rather
  than crashing the pass.
- **Varint boundaries** (127/128, 16383/16384), where the header changes size
  and shifts the whole text block.
- **Language independence**: stop words and opening words are derived from
  French, English and German corpora with the same code.

## Documentation

| File | Contents |
|---|---|
| [`docs/FORMAT.md`](docs/FORMAT.md) | The binary format, in full |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Event-driven design, idempotence, data safety |
| [`docs/CLASSIFICATION.md`](docs/CLASSIFICATION.md) | Filing, and why it does not depend on a language |
| [`docs/FINDINGS.md`](docs/FINDINGS.md) | Log of the empirical experiments |
| [`docs/INSTALL.md`](docs/INSTALL.md) | Installing, running and removing the service |

French versions are kept alongside as `*.fr.md`.

## Performance

The tool is meant to run permanently, so it must cost nothing at rest. **No
polling**: it waits on a kernel handle (`WaitForSingleObject` on the Notepad
process), the thread sleeps at 0% CPU and the kernel wakes it on exit.

| Phase | RAM | CPU |
|---|---|---|
| At rest | ~15 MB | 0% |
| Burst on close | ~150 MB, 1–2 s | one core, briefly |

## Contributing

Bug reports and pull requests are welcome — see
[`CONTRIBUTING.md`](CONTRIBUTING.md). If you find a way to make the tool
damage notes, please read [`SECURITY.md`](SECURITY.md) first.

## Licence

MIT — see [`LICENSE`](LICENSE).
