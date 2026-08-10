# Notepad TabState binary format (Windows 11)

Complete reverse engineering, validated empirically against 106 real tabs.
Everything marked ✅ was verified on real data, not inferred.

*Version française : [FORMAT.fr.md](FORMAT.fr.md).*

## 1. Location

```
%LOCALAPPDATA%\Packages\Microsoft.WindowsNotepad_8wekyb3d8bbwe\LocalState\
├── TabState\
│   ├── {guid}.bin          ← one tab (actual content)
│   ├── {guid}.0.bin        ← state record, sequence 0
│   ├── {guid}.1.bin        ← state record, sequence 1
│   └── {guid}.bin.bak      ← occasional Notepad backup
└── WindowState\
    ├── {guid}.0.bin        ← tab index, sequence 0
    └── {guid}.1.bin        ← tab index, sequence 1
```

Notepad 11 is a sandboxed Store application
(`Microsoft.WindowsNotepad_8wekyb3d8bbwe`). **No extension API, no hook
point.** A plugin in the proper sense is impossible: the only route is an
external tool manipulating these files while Notepad is closed.

## 2. Nothing is encrypted or obfuscated ✅

Content is **raw UTF-16LE**, preceded by a thin binary header and followed by
a CRC. No compression, no encryption, no obfuscation.

## 3. Locking ✅

While Notepad runs it holds an **exclusive handle** on every open tab file.

- To **read** while Notepad runs: open with `FileShare.ReadWrite`. A plain
  `File.ReadAllBytes()` fails with `IOException`.
- To **write**: Notepad must be closed. It keeps state in memory and rewrites
  files at will; any concurrent write is lost.

Notepad does not write "on close": it writes **continuously** as you type.
Closing only flushes and releases the locks.

## 4. Structure of a `{guid}.bin` tab

| Offset | Size | Contents |
|--------|------|----------|
| 0 | 2 | Magic `4E 50` = `"NP"` |
| 2 | 1 | Sequence number (`00` on main files) |
| 3 | 1 | **Flag**: `00` = unsaved note, `01` = tab backed by a file |
| 4 | 1 | Unknown, `01` on every sample |
| 5 | varint | Caret position — selection start |
| … | varint | Caret position — selection end |
| … | 3 | `01 00 00` |
| … | 1 | **Counter N** — observed as `02` or `03` |
| … | N | N bytes of value `01` |
| … | varint | **Content length, in UTF-16 characters** |
| … | 2×n | The text, UTF-16LE, no BOM |
| L-5 | 1 | Marker, value `01` |
| L-4 | 4 | **CRC32, big-endian** |

### The flag at byte 3

- `00` → loose note, never saved. **This is what the tool handles.**
- `01` → the tab mirrors a real file on disk. **Never touch it.** The header
  then contains a path and the layout above does not apply.

Reference corpus: 100 loose notes, 6 file-backed.

### Varints

Unsigned LEB128, 7 useful bits per byte, high bit meaning "more follows".

```
0x0A            → 10
0xDF 0x02       → 0x5F | (0x02 << 7) = 95 + 256 = 351
```

**Major trap**: varint size varies with the value, so **the header has no
fixed length**. Reference corpus:

| Length-varint size | Files |
|---|---|
| 1 byte | 33 |
| 2 bytes | 64 |
| 3 bytes | 3 |

Consequences:

1. Text starts at offset **15, 18, or more** depending on the file.
2. **That offset can be odd.** Decoding the whole buffer as UTF-16 from offset
   0 to search for text produces garbage on those files — the header must be
   parsed properly.
3. Pushing a note past 127 or 16383 characters grows the header by one byte
   and **shifts the entire text block**. There is no in-place patch: the whole
   file gets rewritten.

## 5. The CRC32 ✅

This is what decides whether Notepad accepts or discards the file.

```
Algorithm : standard CRC32 (zlib)
Polynomial: 0xEDB88320  (reflected)
Init      : 0xFFFFFFFF
Final XOR : 0xFFFFFFFF
Range     : bytes [3 .. L-5]  — magic and sequence byte skipped
Storage   : BIG-ENDIAN in the last four bytes
```

**Validated on 106 files out of 106, zero failures.**

The trap that costs an evening: the CRC is stored big-endian while everything
else in the format (varints, UTF-16) is little-endian.

Found by brute force over {reflected, normal polynomial} × {init 0,
0xFFFFFFFF} × {final XOR 0, 0xFFFFFFFF} × {start offset 0..8} × {LE, BE},
requiring a simultaneous match on three files of different sizes. Exactly one
combination survives.

## 6. State records `.0.bin` / `.1.bin` ⚠️

These are **not backups**. They are 22-byte records replaying the tab's
metadata:

```
4E 50 00 0E 00 D5 05 DF 02 DF 02 01 00 00 03 01 01 01 | E7 71 0F 2A
      ^^          ^^^^^ ^^^^^ ^^^^^ ^^^^^^^^^^^^^^^^^   ^^^^^^^^^^^
      seq         ?     351   351   same config block    CRC
```

`DF 02` = 351 = **exactly the main content length**.

The two files alternate through byte 2 (sequence `00` / `01`), double
buffering so an interrupted write never corrupts the state.

**Critical impact**: rewriting the `.bin` with a different length while
leaving these records in place makes them announce the old length and
contradict the content.

Chosen and validated handling: **delete them**. Notepad did not object and
recreates them on the next edit.

## 7. Keystroke journal ⚠️

Discovered while testing on a corpus the user had just edited.

While Notepad runs, an edited tab is **not rewritten**. Notepad appends a
journal to the end of the `.bin`: one 9-byte record per typed character, each
with its own CRC.

```
00 00 01 23 00 | 79 49 EA 4B     '#'
01 00 01 44 00 | 6E 95 3E 9B     'D'
02 00 01 61 00 | C1 C6 94 AC     'a'
```

So the `.bin` holds the text as of the last consolidation, followed by pending
keystrokes. In one measurement, 91 notes out of 93 were in that state.

The parser detects this as `LayoutMismatch` — the declared length no longer
matches the file size — and **refuses the file**. That is the correct
behaviour: rewriting from the base text would have erased the pending edits.

**Closing Notepad consolidates everything**: journals disappear and every file
parses cleanly again. This is why the tool only ever writes with Notepad
closed.

## 8. `WindowState` — leave it alone ✅

`WindowState\{guid}.{0,1}.bin` holds the **ordered list of tab GUIDs**, 16 raw
bytes each (`Guid.ToByteArray()`), after a header of about a hundred bytes.

Verified: all 106 tab GUIDs present on disk were referenced there.

Its checksum does **not** follow the scheme in section 5 — no start offset
from 0 to 6 matches. It remains unsolved.

**That does not matter, because it is not needed.** Decisive test:

> Delete a tab `.bin` while leaving its GUID orphaned in WindowState, then
> restart Notepad.
>
> Result: Notepad starts normally, **shows no phantom tab**, does not crash,
> does not recreate the file and raises no complaint. The other 105 tabs are
> intact. Confirmed visually in the tab bar.

Hence the tool's strategy: **recycle existing tab GUIDs** as theme containers.
Their GUID is already indexed, so WindowState is never touched.

## 9. Writing — validated end to end ✅

Test: merging a 12-character note into a 351-character note.

1. Parse both tabs
2. Concatenate with a separator → 399 characters
3. Generate a complete 821-byte `.bin` (header + varints + text + marker +
   recomputed CRC)
4. Write it to the container, delete the source, delete the container's state
   records
5. Restart Notepad

**Result: Notepad accepted the file, re-read it, and rewrote it unchanged —
821 bytes, CRC still valid, content intact.** It draws no distinction between
a file it produced and a forged one.

## 10. The counter variant — solved ✅

An early parser treated `01 00 00 03 01 01 01` as a constant 7-byte block.
Result: **83 files out of 100 verified, 17 failed**.

The cause: the block is not fixed size. The fourth byte is a **counter**,
followed by exactly N bytes.

```
Counter 03 :  01 00 00 03 01 01 01     (7 bytes)
Counter 02 :  01 00 00 02 01 01        (6 bytes)
```

Assuming 7 bytes shifts everything by one on counter-`02` files, and the rest
of the parse goes astray.

After the fix: **100 files out of 100, no mismatch, no invalid CRC.**

The counter's meaning is still unknown, but reading it is enough.

## 11. Line endings ⚠️

Notepad does not always write CRLF pairs — **lone `\r` characters occur**.
Splitting note text on `'\n'` alone treats the whole note as a single line.

## 12. Fragility over time

Microsoft updates Notepad often and may change this format without notice or
documentation. The tool therefore:

- checks the `NP` magic and the version byte on every run;
- treats byte 4 as a version sentinel — any other value yields
  `UnknownVariant` and the file is refused rather than parsed blindly;
- backs up `LocalState` before any write.

The default behaviour on drift is **abstention**, never a silently shifted
parse.
