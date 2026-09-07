# Contributing

Thanks for looking. This tool rewrites files that hold notes existing nowhere
else, so the bar for anything that writes to disk is deliberately high.

*Ce document est aussi disponible en français : [CONTRIBUTING.fr.md](CONTRIBUTING.fr.md).*

## Never commit real notes

Notepad tabs contain API keys, draft e-mails, private links and client names.
`.gitignore` blocks `*.bin`, `TabState/`, `LocalState/` and backup folders,
but that is a safety net, not a guarantee.

The same applies to **test fixtures, code comments and documentation
examples**: use neutral names (`project`, `alpha`, `beta`, `example.com`,
Contoso, Fabrikam). If a real name reaches a commit, it stays in the history
even after you delete it.

## Building

```bash
dotnet build -c Release
dotnet test
```

Requires the .NET 10 SDK. `NotepadTidy.Core` has no external dependency and no
Windows dependency — it builds and tests anywhere. Only the CLI and the
service touch Windows paths and processes.

## Working on the code safely

Never point the tool at your real notes while developing. `--path` runs it
against an isolated copy:

```bash
nptidy backup C:\sandbox
nptidy stats --path C:\sandbox
```

## What a pull request needs

- **Tests for every refusal path.** The most valuable tests in this repo are
  the ones asserting that nothing was written and nothing was deleted.
  `TabMerger` sits behind `ITabFileSystem` precisely so those tests run in
  memory.
- **A green CI.** Build and tests run on `windows-latest` for every PR.
- **A commit message that explains the why.** The existing history is the
  model: what was observed, what was tried, what the measurement showed. A
  message that only restates the diff is not enough.
- **No new external dependency in `Core`** without discussing it first.

## Changes to the binary format

`docs/FORMAT.md` is the most reusable result of this project, and every claim
in it is backed by an experiment recorded in `docs/FINDINGS.md`. If a Notepad
update shifts the format:

1. add the experiment to `FINDINGS.md` — the question, the method, the numbers;
2. update `FORMAT.md`;
3. make the parser **refuse** the unknown variant rather than guess at it.

Refusing is always correct. Guessing can destroy notes.

## Documentation is bilingual

English is the base language for code, comments and CLI output. Documentation
exists in both languages: `README.md` / `README.fr.md`, `docs/X.md` /
`docs/X.fr.md`. When you change one, change the other in the same commit —
they have drifted before.

## Reporting a bug

Open an issue with your Windows and Notepad versions, the command you ran, and
what `nptidy stats` prints. **Do not paste the content of your notes** — a
GUID and a byte count are enough.

For anything that could damage notes, see [`SECURITY.md`](SECURITY.md).
