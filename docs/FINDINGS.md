# Log of empirical findings

Everything asserted in `FORMAT.md` comes from an experiment listed here, run
against a real corpus of ~100 tabs accumulated over two years.

*Version française : [FINDINGS.fr.md](FINDINGS.fr.md).*

## File locking

**Question**: can tabs be read while Notepad is running?

`File.ReadAllBytes()` fails with `IOException` — Notepad holds an exclusive
handle on every open tab, permanently.

**Conclusion**: read through `FileStream` with `FileShare.ReadWrite`. Writing
requires Notepad to be closed.

**Corollary**: Notepad does not write "on close", it writes continuously while
typing. Closing only flushes and releases locks.

## Finding the CRC

**Question**: which checksum protects the files?

Standard CRC32 and CRC32C over the obvious ranges: no match. Brute force over
{reflected, normal polynomial} × {init 0, 0xFFFFFFFF} × {final XOR 0,
0xFFFFFFFF} × {start offset 0..8} × {trailing bytes excluded 0..4} × {little
endian, big endian}, requiring a simultaneous match on three files of
different sizes.

**Exactly one combination survives**:

```
poly=0xEDB88320 reflected, init=0xFFFFFFFF, xor=0xFFFFFFFF,
range=[3 .. L-5], BIG-ENDIAN storage
```

Broader validation: **106 files out of 106**.

What defeated the first attempts: big-endian storage, while everything else in
the format is little-endian.

## Orphaned GUID in WindowState

**Question**: what does Notepad do when a `.bin` referenced by WindowState no
longer exists? This decided whether the WindowState format had to be reverse
engineered at all.

**Protocol**: full backup, create a test note, close Notepad, delete only its
`.bin`, leave the GUID in WindowState, restart.

**Result**:

| Observation | Result |
|---|---|
| Notepad starts | yes, no crash, no warning |
| Phantom tab in the bar | **none** — confirmed visually |
| The other 105 tabs | intact |
| `.bin` recreated | no |
| WindowState rewritten | no |

**Major consequence**: WindowState never needs to be touched. Recycling
existing tab GUIDs as theme containers keeps their GUID indexed, and deleting
absorbed notes has no side effect.

The WindowState checksum remains unsolved — and irrelevant.

## The `.0.bin` / `.1.bin` files

**Question**: backups, or something else?

Something else. 22-byte records replaying the metadata:

```
4E 50 00 0E 00 D5 05 DF 02 DF 02 01 00 00 03 01 01 01 | E7 71 0F 2A
      ^^                ^^^^^ ^^^^^                      ^^^^^^^^^^^
      sequence          351   351                        CRC
```

`351` is exactly the main content length. The two files alternate (byte 2 =
sequence) as a double buffer.

**Impact**: rewriting a tab without handling them leaves records announcing
the old length. Deleting them works — Notepad recreates them on the next edit.

## Writing end to end

**Question**: does Notepad accept a file it did not produce?

**Protocol**: merge a 12-character note into a 351-character one. Generate a
complete 821-byte `.bin`, write it to the container, delete the source and the
state records, restart.

**Result**: Notepad starts, displays the merged content, and **rewrites the
file unchanged** — 821 bytes, CRC still valid, content intact. No distinction
between a file it produced and a forged one.

A self-check runs before writing: the writer re-parses its own output and
compares it to the expected text. A faulty writer is caught on an in-memory
copy, never on real data.

## The counter variant

**Symptom**: 83 files out of 100 verified
`text_offset + 2×length + 5 == file_size`. Seventeen did not, with gaps from
-161 to +14412 bytes — so not corruption, but a parsing shift.

**Cause**: the block treated as a constant 7 bytes (`01 00 00 03 01 01 01`) is
really `01 00 00`, then a **counter**, then N bytes. Those 17 files carry
counter `02` and are therefore 6 bytes long.

Manual verification before fixing:

| File | Counter | Length | Computed | Actual |
|---|---|---|---|---|
| A | 02 | 404 c | 17 + 808 + 5 = 830 | 830 ✓ |
| B | 02 | 372 c | 17 + 744 + 5 = 766 | 766 ✓ |
| C | 02 | 1593 c | 17 + 3186 + 5 = 3208 | 3208 ✓ |

**After the fix: 100/100, no mismatch, no invalid CRC.**

## The keystroke journal

**Symptom**: after the user edited every note to add a `#` heading, 91 files
out of 93 turned into `LayoutMismatch`, having been clean before.

**Cause**: while Notepad runs, an edited tab is not rewritten. Notepad appends
a journal of 9-byte records, one per typed character, each with its own CRC.

```
00 00 01 23 00 | 79 49 EA 4B     '#'
01 00 01 44 00 | 6E 95 3E 9B     'D'
```

Decoding the journal reconstructs the pending keystrokes exactly.

**The tool refused all 91 files** instead of rewriting them from an incomplete
base text — which would have erased the user's edits. Refuse-by-default paid
off on real data.

**Closing Notepad consolidates everything**: 93/93 clean, journals gone.

## Guessing headings does not work

**Question**: can the theme be inferred from the shape of the first line,
without requiring a marker?

**Measured**: 11% of notes have a heading-shaped first line — short, no
terminal punctuation. **All of them are false positives**: colour codes, port
numbers, schedules, and two passwords.

**Conclusion**: require an explicit `#`. One character removes the ambiguity
that no heuristic resolves.

## Lone carriage returns

**Symptom**: discovered themes contained line breaks.

**Cause**: note text is split on `'\n'` only, while Notepad also writes lone
`'\r'` characters. The whole note was then treated as one line and the
extracted theme swallowed the content.

**Fix**: split on both. Themes became clean and one more note was filed.

## Reference corpus

~100 loose notes plus 6 file-backed tabs, spanning two years, ~171 000
characters. Median length 705 characters, maximum 25 181.

The corpus is not in the repository and must never enter it.
