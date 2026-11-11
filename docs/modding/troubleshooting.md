# Troubleshooting

Start with `omp check`. It runs the same link the port runs at startup and prints the whole report:

```text
Ome.Cli omp check --mod path\to\my.mod [--mod other …]
```

In the port, the last report is `diagnostics/mod-link.txt` in the profile root ([cli.md](cli.md)).

## Reading the link report

Each line starts with its class and the mods involved:

```text
load order: example.sc0000-merge -> example.sc0000-page3
conflict: [example.sc0000-merge, example.sc0000-page3] SC0000.BIN: conflict: example.sc0000-page3 (patches/page3) inserts before 0x877, inside the region example.sc0000-merge (patches/merge) replaces at 0x874
note: [example.sc0000-page3] SC0000.BIN: compiled patches/page3.oms at load time (no stored plan)
omp check: does not link: 2 mods; 0 errors, 1 conflicts, 0 interactions, 0 overrides, 6 notes
```

| Class | Meaning | Stops the load? |
|---|---|---|
| `error` | Something in one mod is wrong: syntax, a missing file, a bad manifest, a fingerprint mismatch. | yes |
| `conflict` | Two mods cannot load together: overlapping edits, two replacements of one script, a declared `[[conflicts]]`, a missing dependency. | yes |
| `interaction` | Several mods insert at the same site. They all apply, in the order shown. | no |
| `override` | Several mods supply the same asset or script. The winner is named. | no |
| `note` | Information. Examples: a fragment compiled at load time, a stray file in a package, a table entry that now points at replacement code, or a record 0 that would hang native AGE at boot (`warning: record 0 runs at boot …`). | no |

In the port, a report with errors or conflicts stops startup with exit code 2. `--skip-failed-mods` drops the mods
involved and starts with the rest, naming what it dropped.

## Compile errors

Compile errors give `file:line:col`.

**Fingerprint mismatch**

```text
patches/page3.oms:3:1: this patch targets sha256:0297…f6, but SC0000.BIN is sha256:0297…f6
```

The installed script is not the one the fragment was written for. The second fingerprint is the installed one.
Common causes:

- an install with versus without a translation layer (`--overlay-root`): each is a different base;
- a different game version;
- another mod that replaces the script wholesale. The report names that mod.

Re-check the site against the installed script before you update the fingerprint. A different base usually means
different offsets too.

**Not an instruction start**

```text
patches/page3.oms:7:13: 0x878 is not an instruction start in SC0000.BIN
```

The offset points into the middle of an instruction. Take offsets from `locate_page.py`, the decompiler or
`oms-decompile --level raw`, which print instruction starts only.

**Names that do not resolve** usually mean a missing `use` line. Registry names need `use <profile>.globals`, and
dialogue lines and speakers need `use <profile>.adv`. If the registries were never generated, build them as described
in [first-mod.md](first-mod.md), step 0.

**A character outside CP932** cannot be shown by the game. Replace it with an equivalent character: curly quotes,
most emoji and many accented letters are not in CP932, while straight quotes and full-width punctuation are.

## Conflicts

- **Overlapping regions, or an insertion inside another mod's replaced region:** the two mods edit the same code.
  Either move your edit outside the other mod's region, or declare the other mod in `[[conflicts]]` with a reason,
  so players get a clear message instead.
- **Two replacements of one script:** if superseding the other mod is intended, declare it in `[replace]`
  ([packages.md](packages.md)). Otherwise turn your replacement into patches.
- **No free selector:** every selector up to `0x7f` is allocated. Release removed mods with `--forget-mod ID`.

## In-game problems

**Native AGE hangs at boot, before the title.** Your record 0, the first `[scripts].names` entry, shows text or waits
for input. Record 0 runs during boot. Put a short boot script that ends with `exit` first ([new-scenes.md](new-scenes.md)).
`omp check` warns about this when record 0 itself shows text or waits; a `run` from record 0 into a scene hangs the
same way without the warning.

**"エラーが発生しました" with "Depth が不正です SCRIPT != OWNER".** This is the engine's "invalid depth" error. A click
reached a handler that belongs to the scene `OWNER` while the script `SCRIPT`, which the scene called with `run`, was
waiting for input. Typically a right-click opens the ADV menu. Pages inside a script called with `run` cannot use the
scene's menu. Insert the pages into the scene with a patch, or make the script a scene of its own
([new-scenes.md](new-scenes.md)). The port shows the same box as native AGE, with 中止 (abort), 再試行 (retry) and 無視
(ignore); with an English shell it reads "An error occurred" and "Invalid depth SCRIPT != OWNER".

**My edit is gone after loading a save.** The cells you changed are part of the save, so loading restores the saved
values. Make the edit somewhere the save does not store, or repeat it at a point that runs after loading
([new-scenes.md](new-scenes.md), "Data edits at boot").

**Save-stamp warnings when loading.** The save was made with different mods, versions or selectors. The port warns
and loads anyway. A save made on a page that a removed mod added, or inside a removed mod's scripts, may not resume
correctly. Reinstall the mod to recover such a save, since its selector is kept.

**My new page does not show.** Check that the site is on the path the game actually takes. The page locator records
the route of the run you made. Branches and earlier choices can skip your site.

## Translations

**"text/fr/ adds entries to language 'fr', which no package in the stack defines".** Some package in the stack must
declare the language with a `[[language]]` table. A package that only adds text to a language another one defines
needs that other package enabled ([languages.md](languages.md)).

**"[[language]] needs omp = 2" or "text/ needs omp = 2".** Language tables and the `text/` and `fonts/` folders are
format version 2. Set `omp = 2` in the manifest. So are language assets: "language assets (assets/<language>/NAME)
need omp = 2".

**"assets/fr/ holds language assets for 'fr', which no package in the stack defines".** As for `text/`: enable the
package with the `[[language]]` table, or add one.

**"a PNG stands for an image only in assets/<language>/".** Plain `assets/` is what native AGE reads, so it takes the
game's own formats. Put the PNG in a language folder, or convert it to AGF.

**"neither the base nor this package's [assets].names has an asset NAME".** A language asset replaces an asset the
game has, or one the same package adds. Check the name; for a PNG, the game's name with `.AGF` replaced by `.png`.

**"NAME's asset hides OTHER's en asset".** A package later in load order replaces the asset for every language, so
the earlier package's language version never shows. Ship an `en` version in the later package or in one that loads
after it, or change the load order.

**A translated picture does not change.** Check the log: the port prints an `[assets]` line with the file the
first time it reads each language asset. With no line, the name or the language is not the one the game uses;
`omp explain NAME` shows which version wins. After a language switch, a picture the game copied into another one
changes only when the game loads it again: go back to the title, or restart.

**`po-import` writes nothing.** A translation that names a placeholder its source does not define, or an engine
value its key does not take, stops the import; each problem is listed with its file and line. Fix them in the PO
file and import again.

**A line stays Japanese.** Run with `--text-log`: if the line is in `diagnostics/text-misses.jsonl`, its `source` is
the exact key the catalog needs, and `script` and `at` the site that drew it. If the catalog has that source:

- an entry without `text`, or with `obsolete = true`, is skipped;
- a site entry (`at`) does not bind when the script's fingerprint differs from the file's, or a mod replaces the
  script; the link report and the port's `[text]` lines say so;
- a sentence the script draws in pieces matches only as the whole sentence, joined without its line breaks.

`omp text check` warns about entries whose source no installed script draws or stores where their scope reaches.

**Text is cut off at the bottom of the window.** The translation needs more rows than the window has. Run with
`--text-log`: each cut message is in `diagnostics/text-misses.jsonl` with `rows_cut`. Shorten the translation, give
the entry `fit = "shrink"`, or patch the script to break the page ([languages.md](languages.md), "Alignment and
fit").

**A label runs out of its box, or is no longer centred.** Give the entry `fit = "shrink"` to narrow it to the
original's width, and `align = "center"` or `"end"` to place it as the original was. The language's `min_fit_scale`
limits how narrow it gets.

**Squares or the wrong font.** The fonts of the language's `[[language.font]]` table lack those characters, and so
do the system's. `omp text check` lists the characters each table's bundled fonts lack; add a font that has them to
`use`.

**A menu or dialog keeps the engine's wording.** The log has a `[text]` line for each `[[engine]]` entry the engine
left out, ending "the engine's own text is shown": the key is not one the engine has, or the text names a value
(`{1}`) its Japanese does not take. `omp text check` reports both. An entry without `text` is a template and is
skipped.

**"a package without [game] holds only text/ and fonts/" or "holds only [[engine]] entries".** A package for every
game carries the engine's own text only. Game text, patches, scripts and assets need a `[game]` table naming the
game ([languages.md](languages.md), "A shell language for every game").

## Game-specific problems

Each game guide lists its quirks and current limits, such as installs with several bases, missing patch points, or
recipes not yet tested on that game ([games/README.md](games/README.md)).

## Outside the repository

An exported port build cannot compile new scripts, or fragments without a stored plan, because the OMS modules and
registries live in the repository ([cli.md](cli.md), "Outside the repository"). Build packages with `omp build` and
ship the `.omp`.
