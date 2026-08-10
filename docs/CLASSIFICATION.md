# Filing notes — no admin, no cost, no language dependency

Written after measuring against a real corpus. The figures come from
`nptidy analyze`, not from intuition.

*Version française : [CLASSIFICATION.fr.md](CLASSIFICATION.fr.md).*

## The constraint that decided everything

A tool that only works on one person's notes, in one language, is worth
nothing to anybody else. The first attempt — lists of greetings, stop words
and weekday names — was bespoke for a single user. Worse, the heuristic "a
capitalised word mid-sentence is a proper noun" collapses in German, where
every common noun is capitalised, and is meaningless in Japanese.

So everything below is judged against one criterion: **does it work for
someone else, in another language?**

## Level 1 — the explicit heading (primary mechanism)

A note whose first line starts with `#` declares its theme.

```
# project beta
the link for the testers
```

→ theme `project-beta`, created on the fly.

The category is not guessed, it is declared, in the user's own words.
`# Rechnungen`, `# 仕事のメモ` and `# работа` behave identically — covered by
tests.

### Why an explicit marker and not a guess

It is tempting to treat any short first line as a heading. Measured on the
real corpus: **11% of notes have a heading-shaped first line, and every single
one is a false positive** — colour codes (`2e343b 4a5568 1c1f26`), port
numbers (`port 5432`), schedules, and two passwords.

Each would have created a junk theme. The marker removes the ambiguity for the
cost of one character. `NoteHeading.GuessImplicit` keeps the guess, but only
to measure it — never to file.

### Put a separator after the theme

The theme should be followed by a space or a newline. Without one, the theme
and the content run together — `#projectHi there!` — and nothing marks where
the theme ends.

This matters most for **multi-word themes**, written with a hyphen
(`#project-mail`): glued to content they become undecidable, because
`project-mailsomething` is a perfectly plausible single token. A single space
makes them exact.

`ThemeVocabulary` recovers glued themes statistically (see level 3), but a
separator is exact and costs nothing.

## Level 2 — filing by mention (fallback)

A note without a heading still has to go somewhere. We look for the name of a
theme the user **already declared elsewhere**. A new theme is never invented,
so the vocabulary stays theirs, in their language.

Deliberately conservative, because a wrong filing costs more than an unfiled
note — it teaches the user to distrust the tool:

- **whole-token comparison**, so `play` does not match inside `display`;
- a multi-word theme only counts when all of its tokens appear in sequence;
- themes shorter than four characters are ignored as mentions, since short
  names collide with ordinary words constantly;
- **outright refusal** when two themes are cited comparably, with a required
  margin of 2×.

Measured: this recovered 5 of the 7 previously unfiled notes, including three
whose marker had been mistyped.

Its weakness is honest: a single mention is a weak signal. Raising the
threshold to two mentions trades recall for precision.

## Level 3 — statistics derived from the corpus

`CorpusProfile` derives from the data what other tools hardcode:

| Derived | How | Verified |
|---|---|---|
| Stop words | any token in more than 25% of notes | French corpus yields `le, de, la, et…`; English yields `the, and, to` |
| Opening words | recurring first words | finds `salut` in French and `hi` in English, **same code** |
| Term rarity | IDF | surfaces project names, buries filler |

None of these lists is written anywhere. Tests verify the behaviour on French,
English and German corpora.

Two settings learned by measuring:

- **Sublinear TF** (`1 + log(tf)`). Without it, a note repeating one word
  forty times crushes the ranking — exactly what happened on the first run.
- **Reject purely numeric tokens.** Times, dates and amounts are everywhere in
  personal notes and identify no subject.

## What was deliberately dropped

**Automatic theme creation.** Once the user names themes with `#`, clustering
only invents names they did not choose — which was the original problem.
`CorpusProfile` and `NoteSignals` remain as diagnostics behind
`nptidy analyze`, not as the filing backbone.

## What stays language-specific, and optional

`NoteSignals` detects message drafts by greeting, politeness formulas and
proper nouns by capitalisation. **These are complements, never the backbone**,
and the code says so.

Only URLs and domains are genuinely universal there — and they are
surprisingly informative: the domain of a platform, a host or a vendor
identifies a project unambiguously.

## Cost

No model, no API, no connection. TF-IDF over a hundred notes costs
milliseconds. Filing is **free and offline by construction**, not by
configuration.

A local embedding model could improve the level 2 fallback later, but it is
not required — and it would cost ~150 MB of RAM in bursts for an uncertain
gain on short notes.

## Order of work

1. `nptidy analyze` to look at your own corpus.
2. Level 1: explicit headings. Simple, exact, risk-free.
3. Level 2: mention fallback, tuned with `--dry-run` until the filing looks
   right.
4. Only then wire up writing.
