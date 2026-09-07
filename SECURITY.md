# Security policy

## What counts as a vulnerability here

This tool has no network surface, no server and no privileges: it runs as the
logged-in user and touches one folder. So the interesting failures are not
about remote attackers — they are about **losing notes**.

Please report anything that lets `notepad-tidy`:

- destroy, truncate or corrupt a tab that it should have refused to touch;
- write while Notepad is running, or otherwise race with it;
- escape the folder it was pointed at, in particular through `--path`;
- crash a whole tidying pass on one malformed file, since the service would
  then stop tidying silently;
- restore a corrupted backup over live notes.

Parser crashes on hostile input matter for the same reason: a tab file is
untrusted input, and the correct response to an unknown layout is a clean
refusal, never a guess.

## Reporting

Use GitHub's **private vulnerability reporting** on this repository
(Security → Report a vulnerability). If that is unavailable, open a regular
issue describing only the *shape* of the problem, and say you have details to
share privately.

Please include the Windows and Notepad versions, the command or situation, and
a **synthetic** reproduction file if you have one.

**Never attach your real notes or their content.** A hex dump of a
purpose-built file, a GUID, or a byte count is what is useful.

Expect an acknowledgement within a few days. This is a personal project with
no on-call rotation, so please be patient — but data-loss reports go first.

## Scope

Supported: the latest commit on `main`. There are no released versions to
backport to yet.

Out of scope: anything requiring an attacker who already runs code as your
user. At that point they can read `%LOCALAPPDATA%` directly and this tool
changes nothing.

## Two things to know as a user

- Backups under `%LOCALAPPDATA%\notepad-tidy\backups` contain the **full text
  of your notes**, unencrypted, and survive uninstallation unless you pass
  `-RemoveBackups`. That is deliberate — they are the only way back from an
  unwanted merge — but it is worth knowing.
- The service log at `%LOCALAPPDATA%\notepad-tidy\service.log` records theme
  names and counts, not note content.
