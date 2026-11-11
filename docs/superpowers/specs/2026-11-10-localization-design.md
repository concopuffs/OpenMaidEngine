# Localization — design

**Date:** 2026-11-10
**Status:** accepted 2026-11-10, after one design review. Nothing is implemented. §20 records the questions
settled at acceptance and the one still open.
**Home in the canonical map:** this file is the design. The package format it extends stays in
`docs/superpowers/specs/2026-11-03-mod-loader-design.md`. The shell's string tables stay in
`docs/superpowers/specs/2026-11-09-engine-shell-design.md` (§6). Native text and history findings stay in
`docs/engine-re.md`. What modders write goes to `docs/modding/` when it is built (milestone 4).

**Not verified** (each is marked where it matters):

- how many source strings the Kamidori fan translation renders in more than one way. The diff in §2 counted
  instruction changes only. Milestone 0 measures this;
- what Himegari's 17,401 `draw-string` calls with a variable operand draw. The design treats them like any other
  string;
- what Himegari's name editor names, and whether any script compares the result (§5.1);
- the frame position a save resumes from (§8). `docs/engine-re.md` says the scripts rebuild the page; the saved
  position itself was not traced for this design.

## 1. Purpose and scope

A translation today replaces whole scripts. The Kamidori fan translation ships 430 changed scripts to carry
changes that are only text and line breaks (§2). That makes it a different base: mods need separate patches for
it, and it is limited to CP932 characters and hand-made line breaks.

This spec defines how the port shows a game in another language **without changing the scripts or the strings the
VM holds**. It covers:

| Layer | What it is | Section |
|---|---|---|
| Game text | strings the scripts draw: dialogue, names, menus, tables | §5–§8 |
| Engine-owned text | strings the port draws itself: shell, dialogs, error boxes | §11 |
| Assets | images, voices and movies that differ by language | §12 |
| Delivery | how a mod ships all three, and how a player picks a language | §9, §10 |

**Not in version 1:** Lua filters (the seam is defined in §6.5), scripts that need shaping or right-to-left
layout (§7.6), generated text images (§12), subtitles for movies, and exporting a language to native AGE (§17).

## 2. Evidence

**What the Kamidori fan translation changed** (473 scripts compared against the clean install):

| Finding | Number |
|---|---|
| Scripts that differ | 430 |
| Scripts whose instruction stream changed in any way other than text | 0 |
| Added `show-text` + `end-text-line` pairs (hand-made line breaks), in 310 scripts | 18,960 |
| `display-furigana` calls with both operands emptied (in the 120 same-shape scripts) | 2,595 |
| String compare literals changed (`""` to `" "`) | 3 |

**How the scripts use strings** (clean corpora):

| Opcode | Kamidori (732 scripts) | Himegari (481 scripts) |
|---|---|---|
| `show-text` | 51,509 (313 with a variable) | 31,724 (1,666 with a variable) |
| `display-furigana` | 3,054 | 279 |
| `draw-string` | 237 (152 with a variable) | 17,449 (17,401 with a variable) |
| `set-string` | 14,994 | 5,338 |
| `concat` | 67 | 58 |
| `halve-strlen` / `strlen` | 348 / 45 | 17 / 23 |
| `string-equals` + `string-not-equals` | 26 | 31 |
| `cp932-character-length`, `cp932-substring`, `edit-fullwidth-string-dialog` | 0 | 1 each, all in `INPUTNAME.BIN` |

**Three facts that shape the design:**

- **Text reaches the screen at few places.** In the VM these are `ShowTextRun` (ops `0x6e` and `0x196`) and
  `draw-string` (op `0x204`), both in `VirtualMachine.AdvText.cs`. History and redraw reuse recorded text.
- **Strings are also used as identities.** Scripts compare them, and the value switch registers its cases by
  string (`VirtualMachine.ControlFlow.cs:139`). Saves store them as CP932 (`NativeTextHistoryCodec.cs:103`).
- **Lengths size graphics.** Kamidori's `SC0100@0x10e88` takes `halve-strlen` of the speaker's name into
  `G[0x4f8]`, multiplies it by 23 pixels, and uses the result as the width of the name plate's `draw-texture`.
  339 Kamidori scripts call `halve-strlen`, most of them once; SC0100's is the one that was read. Himegari's
  SC0100 has no `halve-strlen`.
- **Lengths reach few saved values.** In Kamidori, 336 of the 348 `halve-strlen` calls write `G[0x4f8]`, a
  scratch cell the scripts reuse everywhere. The other 12, and all 45 `strlen` calls, write frame locals. Every
  Himegari length call writes a frame local (destinations counted in `build/games/<profile>/disasm/*.asm`).
- **Scripts do not change colour inside a sentence.** No `set-text-color` sits next to a text instruction in
  either corpus (517 calls in Kamidori, 261 in Himegari).

## 3. Decisions

1. **Scripts and VM strings stay in the source language.** A translation is applied when a string is drawn. It
   never replaces a string in a cell, a compare, a switch or a save.
2. **The key is the source text,** optionally narrowed by where it is drawn. Keys by text survive game revisions
   and saves, and one entry covers every place a name appears.
3. **The length opcodes measure what will be drawn** (§5.3). This is the only way a translation changes a value
   the scripts see.
4. **The save file is the one the source-language run would have written,** apart from integers derived from
   decision 3. History in a save holds source text; what the player sees is recomputed from it (§8).
5. **Translations are data in a mod package.** A `text/` folder holds catalogs per language. The loader merges
   them in load order, like assets.
6. **Layout is the engine's job.** The port wraps at word boundaries, so a translator writes a sentence, not
   lines.
7. **Structural changes stay patches.** A translation that needs a different page break ships an OMS patch through
   the existing loader. The catalog cannot change control flow.
8. **One language setting.** `ui:Language` selects the shell's table, the game catalogs and the language assets.

## 4. Terms

- **Source text:** a string as the scripts hold it. For these games it is Japanese.
- **Presented text:** what the port draws in its place.
- **Catalog:** the merged set of entries for one language.
- **Entry:** one source text (or pattern) and its presented text.
- **Boundary:** the point where the VM hands a string to the host for drawing.
- **Message:** a run of adjacent text instructions that forms one sentence (§6.2).
- **Presenter:** the component that turns source text into presented text. With no catalog for the selected
  language it is switched off, and the port behaves exactly as it does today.

## 5. Text model

Every string opcode falls into one of three classes. Presentation (§5.2) and measurement (§5.3) are closed
lists; everything else is identity.

The lists were checked against the disassembly of both games: 23 opcodes appear there with a string operand.
Six are the presentation and measurement opcodes. The other 17 are nine that §5.1 names, plus `lookup-array` and
`lookup-array-2d` (copy a string out of a table), `set-font`, `find-font-family-index` and
`get-font-family-name` (font face names), the `comment` and `coroutine-label-yield` markers, and the diagnostic
accumulator (`append-diagnostic-value`, `0x1b2`). None of them draws text.

### 5.1 Identity: always source

`set-string`, `concat`, `toString`, `string-equals`, `string-not-equals`, the value switch, `cp932-substring`,
`store-shared-profile-string`, `load-shared-profile-string`, `edit-fullwidth-string-dialog`, and everything that
saves or loads a string. These are untouched.

Consequences:

- a compare or switch cannot break, because both sides are what the untranslated game compares;
- every VM string stays CP932, so presented text may use any Unicode character without affecting saves;
- the dialogue oracle (`Emitted`), the page map and the F2/F3 locator keep reporting source text.

**Name entry is a limitation in version 1.** Himegari's `INPUTNAME.BIN` opens `edit-fullwidth-string-dialog`.
The editor accepts only double-byte CP932 characters (`Cp932Text.ValidateFullwidthName`) and opens filled with
the source-language default.

- A player who keeps the default sees it translated everywhere it is drawn, because the default is a known
  string with a catalog entry.
- A player who types a name must type full-width characters. The name has no entry, so it is drawn as typed.
- The dialog's own labels and messages are engine-owned text (§11).

Showing a presented default in the field is future work (§17). It needs two more rules to be safe, and one
script does not justify them yet.

### 5.2 Presentation: translated at the boundary

| Site | Opcode | What is resolved |
|---|---|---|
| `ShowTextRun` | `0x6e` `show-text`, `0x196` `display-furigana` | the message the run belongs to (§6.2) |
| `draw-string` | `0x204` | the string operand |
| History render and redraw | `0x1d1`, `0x82` | a recorded group, re-resolved from its source rows (§8) |

At each site the VM asks the presenter for the presented text and passes both forms to the host. The host draws
the presented form.

Not resolved in version 1: `draw-formatted-integer` and the digit atlases (numbers only), the diagnostic
accumulator and the developer jump picker (developer-facing).

### 5.3 Measurement: follows the presented text

`strlen` (`0x2c5`), `halve-strlen` (`0x1a6`) and `cp932-character-length` (`0x2c6`).

- **Rule.** If the operand resolves to an entry, the opcode returns the size of the presented text. If it does
  not, the result is the native one. A player's typed name therefore measures as it always did.
- **Unit: half cells, counted by character class.** A character whose Unicode East Asian Width is Wide or
  Fullwidth counts 2. Every other character counts 1, including the Ambiguous class (Cyrillic, Greek), which a
  presented font draws narrow.
  - `strlen` returns the half cells.
  - `halve-strlen` returns half of that, rounded up. Native truncates; rounding up keeps an odd-length Latin name
    inside its plate.
  - `cp932-character-length` returns the number of characters in the presented text.
- **No fonts are involved.** Measuring the advance width in the current font was considered and rejected. It
  would put a font-dependent value into VM state: the headless host and the Godot host, or two machines with
  different fallback fonts, would return different lengths and runs could diverge. Counting by class gives the
  same value everywhere. For ASCII, kana and kanji it is also what native `strlen` returns for the CP932 bytes,
  so English text measures as the fan translation's did on native AGE.
- **Override per entry.** A proportional font can make a plate a few pixels wide or narrow: `Illumina` and `WWWW`
  differ more in width than in count. An entry may set `cells = N` (half cells) to state its length outright. It
  is data, so it is as deterministic as the count.
- **Pixel-accurate fitting stays in the host,** through `fit` and `align` (§7.2).
- **Why.** Scripts use these results to size plates, centre labels and place one string after another. The fan
  translation ran the same scripts with English in every length call, which shows the scripts tolerate it.
- **Cost.** A scratch integer in a save can differ between languages: Kamidori's `G[0x4f8]` and frame locals,
  and frame locals only in Himegari (§2).
- **Opt out.** A language can set `measure = "source"` (§9.2) and an entry can override it. Gate 2 uses this to
  prove that everything else is identical.

## 6. Lookup

### 6.1 Keys and context

An entry matches by exact source text. It may be narrowed:

| Scope | Written as | Matches |
|---|---|---|
| Global | no scope | the text, wherever it is drawn |
| Script | `script = "STATUS"` | only when that script's instruction draws it |
| Site | `script = "SC0100"`, `at = 0x2a43` | only that instruction |

- The site form uses the coordinate the F3 locator already prints (`text SC0100@0x2a43`). It is a base offset, so
  the catalog file must name the script's fingerprint, as a patch does.
- A site entry depends on mapping base offsets to the linked script the VM runs. The linker does not keep that
  map today. The patch applier computes each instruction's base index (`OmsPatchProvenance.BaseIndex`,
  `engine/Ome.Script/Patch/OmsPatchApplier.cs:28`), but nothing in `engine/Ome.Mods/` stores it. Keeping it is
  cheap: one offset table per patched script in the link cache. An unpatched script needs none, because its
  offsets are the base offsets. Runtime hooks on a script site would need the same table (the Lua design,
  `docs/backlog.md`, "Runtime scripting (Lua)").
- Whether `at` ships in version 1 is decided by milestone 0's count (§18.1).
- Comparison is by exact code points. There is no normalisation, since the extraction tool writes the keys.

### 6.2 Messages

A Japanese sentence is often several instructions: text, a furigana word, more text. The translation needs the
whole sentence.

- **Definition.** A message is a maximal run of `show-text` and `display-furigana` instructions on one layout,
  with only `end-text-line` or marker instructions between them, and no branch target inside.
- **Found statically.** When a script is loaded the presenter segments it once and records, for each text
  instruction, its message and its position in it.
- **Key.** The message's source text is its runs joined in order, without the ruby and with nothing between
  them. The key does not mark the script's `end-text-line`s, because history cannot reproduce them: it stores the
  same `LineBreak` record (flag `0x8`) for an `end-text-line` and for every soft wrap (`AdvTextHistory.cs`), so a
  group rebuilt after a load does not show where the authored breaks were. Japanese reads correctly when joined
  directly, so the key loses nothing. The extraction tool writes the authored line breaks into the entry's `note`
  for the translator.
- **Style.** A style instruction between two runs ends the message, so a message has one style. Sentences are
  not split this way in practice (§2).
- **Variables.** A run may take its text from a variable. Nothing executes between the runs of a message, so the
  presenter can read every operand when the first run executes.
- **Runtime.**
  1. At the first run, the presenter resolves the whole message.
  2. On a hit, the first run draws the presented sentence. The later runs and the `end-text-line`s inside the
     message draw nothing.
  3. On a miss, each run is resolved on its own, and unresolved runs draw as source.
- **The VM still executes every instruction.** Suppression is only in what is drawn. History, read flags and
  voice are recorded per run as before.

Example: `show-text "あの"`, `display-furigana "黒石" "くろいし"`, `show-text "を拾った。"` has the key
`あの黒石を拾った。` and one entry: `I picked up that black stone.`

### 6.3 Patterns

A string built at runtime has no fixed key. An entry whose source contains a placeholder is a pattern.

Having the VM remember which literals a string was built from was considered and set aside. It fails on a save:
string cells come back from `SAVEnn.DAT` as plain text. Matching on the finished text has to work anyway, so
patterns are the one mechanism and the VM's string model is unchanged.

```toml
[[text]]
source = "武器ＬＶが{level:num}に上がった"
text   = "Weapon level rose to {level}"

[[text]]
source = "{item}が所持限界数を超えています"
text   = "You cannot carry any more {item}"
```

- `{name}` matches any non-empty text, as little as possible. `{name:num}` matches digits, half or full width,
  with an optional minus sign in front. `{{` and `}}` are literal braces.
- A pattern must match the whole string.
- **Captures are resolved too.** `{item}` captures `黒石`, which is looked up and becomes `Black Stone`.
- **Digits.** Scripts produce both forms. A number operand read as a string comes out full width (`１２`,
  `VirtualMachine.ReadStr` through `AsciiToFullwidth`); `toString` writes ASCII. By default a `num` capture is
  copied as it is, so English text would show `Lv５`. A language may set `digits = "ascii"` (§9.2). It then
  converts full-width digits and the full-width minus sign in two places:
  - every `num` capture;
  - a string that is nothing but a number and has no entry, such as a count drawn on its own.

  Measurement (§5.3) follows the converted text.
- **No regular expressions.** The matcher is linear in the text length, so a package cannot make it slow.
- A pattern with no literal text at its start or end must have a script scope, to bound both cost and false
  matches.

### 6.4 Resolution order

For a source text and the site that draws it:

1. exact entry, site scope;
2. exact entry, script scope;
3. exact entry, global;
4. patterns in the same scope order, the one with the most literal characters first;
5. filters (§6.5);
6. no hit: the source text is drawn.

- **Between packages,** the later one in load order wins at equal scope. The link report lists each override.
- **Between languages,** `pt-BR` falls back to `pt`, then to source. A language may declare a different chain.
- **Ambiguity is an error at build.** Two patterns of equal rank that can match one string fail `omp text check`.
- **Cache.** Results are cached by source text and scope, because menus redraw the same strings every frame.
  Misses are cached too. The cache has a fixed capacity and evicts the least recently used result
  (`BoundedLruCache`, which the glyph rasterizer already uses). Himegari has about 17,400 `draw-string` sites with
  a variable operand, and a counter that changes every frame would otherwise grow the cache without limit.

### 6.5 Filters, and where Lua fits

The presenter runs a chain of filters after the catalog. Version 1 has only the catalog; the interface exists so
Lua can join later without touching the VM.

- A filter receives the source text, the catalog's result (if any), the language, the site and any captures. It
  may return a different presented text.
- A filter must be a pure function of its input. It cannot write VM state. Its result is cached like any other.
- When the Lua runtime exists (roadmap §3.8), a mod registers one in its manifest, in the roadmap's hook form:

```toml
[[hooks]]
point   = "ome.text.present"
handler = "lua/plural.lua:on_present"
```

Plurals, grammatical case and computed strings are what this is for. A translation that needs none of them needs
no code.

## 7. Layout and rendering

### 7.1 Line breaking

Today the layout engine wraps where a glyph crosses the right bound, with one rule: no break before `、`, `。` or
`」` (`RetainedGlyphLayoutEngine`, `PreventsHorizontalWrapBefore`). That stays for source text.

For presented text:

- the presenter marks where a break is allowed: after a space or hyphen in spaced scripts, between any two
  characters in CJK text, never before closing punctuation or after opening punctuation;
- the layout engine breaks at the last allowed place before the bound and drops the space there;
- a word longer than a line breaks at the bound, as source text does;
- `\n` in the presented text forces a break.

A language declares `line_break = "words"` or `"anywhere"` (§9.2).

### 7.2 Alignment and fit

Measurement (§5.3) handles layout the script computes. Two entry attributes handle layout the script hard-codes:

- `align = "center"` or `"end"`: place the presented text so its centre or its end is where the source text's
  would have been. The default is `"start"`.
- `fit = "shrink"`: if the presented text is wider than the source text was, narrow the glyphs down to a floor
  the language sets. This is for labels in fixed boxes. Glyph requests already carry a requested width, so
  narrowing needs no new raster path.

Both use the source text's extent, which the host computes for history anyway (§8).

### 7.3 Overflow

Presented text can need more rows than the text window has.

- The host reports it: script, offset, the message and how many rows were cut. The report goes to the miss log
  (§14).
- Version 1 does not page automatically. The translator shortens the line, or ships a patch that adds a page
  break (decision 7).
- Automatic paging would need the host to insert a wait the VM does not know about, which touches skip, auto mode
  and saving mid-page. It is future work.

### 7.4 Fonts

- **A language maps fonts.** The portable rasterizer already resolves an authored face through a substitution
  list (`godot/config/japanese-text-rendering.json`). A language adds entries in front of that list, and may
  bundle font files in the package (§9).
- **Presented text is rasterized as Unicode,** by the Godot text server rasterizer on every platform. The Windows
  GDI rasterizer cannot draw it: it accepts only CP932 requests and needs the system code page 932
  (`WindowsGdiGlyphMaskRasterizer.cs:57-84`). Most players outside Japan therefore already run on the text server
  rasterizer.
- **One rasterizer while a language is active.** On a machine where GDI is available, an untranslated string
  would otherwise be drawn by GDI beside translated strings drawn by the text server, with different faces and
  antialiasing. While a language other than the source is selected, every run uses the text server rasterizer.
  With no language active nothing changes (gate 1).
- **Proportional fonts work.** The layout engine already advances by each glyph's own width outside Kamidori's
  aspect mode. In aspect mode, presented runs use the glyph's own advance instead of the fixed MS Gothic cell.
- **Glyph coverage is checked at build** for bundled fonts (§13).

### 7.5 Reveal speed

Text appears one glyph per `message:MessageSpeed` milliseconds. English has roughly twice the glyphs of Japanese
for one sentence, so it would appear half as fast. A language sets `glyph_delay_scale` (for example `0.5`).

Auto mode needs no change. Its wait does not depend on the text length: an unvoiced page waits
`AutoMessageTime1`, and a voiced page waits for the voice (`engine/Ome.Engine/Hosting/AdvAutoAdvanceTimer.cs`).

### 7.6 Scripts not supported in version 1

The pipeline draws one glyph per code point, with no shaping. That covers Latin, Cyrillic, Greek, CJK and Hangul.
Arabic, Hebrew, Thai and the Indic scripts need shaping or bidirectional layout and are out of scope.

## 8. History and saves

**Rule:** the native save holds what the source-language run would have recorded. What the player sees is
computed from it.

- **Two layouts per run.** The host lays out the presented text and draws it. It also lays out the source text
  without drawing, to get the rows native AGE would record. History stores the source rows, as it does today.
- **Two cursors per layout.** The existing cursor follows the source layout, and the recorded rows are built from
  it. A second, unsaved cursor follows the presented layout and is used only for drawing. `set-adv-text-cursor`
  sets both; `end-text-line` advances both. The opcode registry names no opcode that reads the text cursor or a
  text extent into a cell (`0x79` and `0x7a` set the cursor), so scripts never see either one.
- **The click-wait indicator follows the presented layout.** Op `0x1b1` can anchor the indicator to the layout's
  last glyph. That must be the last glyph of the presented layout, or the indicator sits where the Japanese text
  would have ended.
- **History view.** When the script renders a history group (`0x1d1`) or redraws one (`0x82`), the group is
  presented again and wrapped in the target layout:
  - **Groups recorded in this session** use an unsaved table kept beside the history: for each group, the
    messages that were resolved and their entries. Nothing is matched again.
  - **Groups that came from a save** have only their source rows. The presenter joins the text records, skipping
    ruby records (flag `0x4`) and line-break records (flag `0x8`), as the key does. It then splits the result
    into consecutive spans that match entries, taking the split that leaves the least text unmatched. A message
    always starts at a record boundary, so only those positions are tried.
  - A span with no match shows its source text.
- **Limit: scope is unknown after a load.** A saved record holds no script or site (the port's `SourceOffset`
  field is not written to the save). A loaded group is therefore matched against entries of every scope, and a
  global entry is preferred. If one source text has different scoped translations and no global one, the loaded
  backlog can show the wrong variant. The live page is not affected.
- **After a load, nothing special happens.** A save holds no pixels or glyph records for the ADV text layouts.
  The scripts rebuild the current page themselves (`docs/engine-re.md`: "created ADV layout surfaces have no
  serialized pixels … the current page is rebuilt and republished by script control flow"). Its text
  instructions pass through the presenter like any others, which also rebuilds the presented cursor. The saved
  frame position was not traced for this design. If a script instead redraws the page with `0x82`, that is the
  history path above.
- **Switching language** re-presents the same records, so the backlog changes language with everything else.

What this gives:

| Property | Holds? |
|---|---|
| A save made in one language loads in any other, and without the package | yes |
| A save made with a language active loads in native AGE | yes, it holds source text only |
| The decoded `.DAT` payload equals the source-language run's | yes, except integers from §5.3 |
| The `.STH` thumbnail equals the source-language run's | no, it is a picture of the screen and shows presented text |
| Read flags (`RT.DAT`) are shared between languages | yes, they are kept per script id and message, and the scripts do not change |

**Save stamps.** The loader records the stack beside each save and warns when it changed. A package that ships
only `text/`, `fonts/` and language assets is marked presentation-only, and adding or removing it is reported as
such instead of as a content change.

## 9. Packages

### 9.1 Layout

Three additions to the Open Maid Pack layout (mod loader spec §3.2):

```text
<mod>/
├── manifest.toml
├── text/
│   └── <language>/        catalogs (*.toml); file names are free
├── fonts/                 font files a language uses
└── assets/
    └── <language>/        language-specific asset overrides (§12)
```

`<language>` is a BCP 47 tag in lower case: `en`, `pt-br`, `zh-hant`.

### 9.2 Manifest

```toml
omp = 2                              # version 1 rejects unknown keys, so these need a new format version

[[language]]
tag  = "en"
name = "English"                     # shown in the options panel
fallback = []                        # optional; default is the tag's parents, then source
line_break = "words"                 # "words" or "anywhere"
measure = "presented"                # "presented" or "source" (§5.3)
digits = "ascii"                     # "keep" (default) or "ascii" (§6.3)
glyph_delay_scale = 0.5
min_fit_scale = 0.6                  # floor for fit = "shrink"

[[language.font]]
requested = ["*"]                    # authored faces this applies to
use = ["fonts/NotoSans-Regular.ttf"] # bundled files first, then family names
```

- **Format version 2.** The version 1 reader rejects unknown keys and any other `omp` value
  (`engine/Ome.Mods/Manifest/ManifestReader.cs`), so the `[[language]]` table needs `omp = 2`. A version 2 reader
  still reads version 1 manifests. The Lua design also adds manifest tables; both belong to the same version 2,
  and whichever is built first introduces it.
- **No `[game]` table** makes an engine language pack (§11.1).
- A package may declare several languages.
- Several packages may provide one language. Their catalogs merge (§6.4). For settings, the later package wins
  per key, and the link report says so.
- A package with `text/<language>/` and no `[[language]]` entry for it is valid: it adds entries to a language
  another package defines. If no package defines it, the load stops with that message.

### 9.3 Catalog files

```toml
# text/en/SC0100.toml
script = "SC0100"                    # optional: default scope for every entry in this file
fingerprint = "sha256:…"             # required only if an entry uses `at`

[[text]]
source = "やっと匠士になる事が出来た。工匠にとって最も一般的な階級だ。"
text   = "I'm finally a Tradesman, the most common rank for alchemists."

[[text]]
source = "はい"
at     = 0x2a43
text   = "Right."

[[text]]
script = ""                          # override the file's scope: global
source = "黒石"
text   = "Black Stone"
note   = "item name"                 # for translators; the engine ignores it
```

| Key | Meaning |
|---|---|
| `source` | the source text, a message key (§6.2) or a pattern (§6.3) |
| `text` | the presented text; `\n` forces a line break |
| `script`, `at` | scope (§6.1) |
| `align`, `fit` | layout (§7.2) |
| `measure` | override the language's measurement mode for this string |
| `cells` | this string's length in half cells, in place of the count (§5.3) |
| `note` | translator comment |

Unknown keys are errors, as in the manifest.

**Format choice.** TOML matches the rest of the project and makes the attributes first-class. Translation tools
speak gettext PO, so §13 adds a PO export and import. PO as the shipped format was set aside (§20).

### 9.4 What the loader does

1. **Read** every `text/` file of the enabled packages; a syntax error, unknown key or bad placeholder stops the
   load with file and line.
2. **Merge** per language in load order and record every override.
3. **Bind** site entries to the linked scripts, and check their fingerprints.
4. **Report** in `diagnostics/mod-link.txt`: entries per language, overrides with the winner, and entries whose
   source matches nothing in the installed scripts.
5. **Mount** the catalogs as one immutable snapshot. Changing language swaps the snapshot.

`omp export` skips `text/`, `fonts/` and language assets with a note, because native AGE cannot load them (§17).

## 10. Language selection

- **Setting.** `ui:Language` in `OME.INI`, or `--language` for one run. Today `PortOptions` accepts only `ja` and
  `en`; it will accept any well-formed tag. The list to open up is `ShellStrings.Languages = ["ja", "en"]`
  (`engine/Ome.Engine.Frontend/Shell/ShellStrings.cs:17`).
- **Available languages.** The shell's own tables plus every language the mounted stack defines. The options
  panel lists them by their `name`.
- **Per game.** `OME.INI` is shared across games. If the selected language has no catalog for the running game,
  the game text stays source and the shell uses its own fallback. No per-game setting is added in version 1.
- **Switching.** The shell already switches live. Game text drawn after the switch uses the new language; text
  already on a surface stays until the script redraws it. A full switch is guaranteed after returning to the
  title, and the options panel says so in one line beside the setting.
- **Source language.** `ja` means "no presenter" for these games. A stack with no catalog for the selected
  language runs with the presenter off.

## 11. Engine-owned text

The shell already has keyed tables (`Shell/Strings/ja.json`, `en.json`; engine shell spec §6). This spec extends
that scheme instead of replacing it.

- **Remaining fixed strings move to keys.** The script error box (`VirtualMachine.DiagnosticCaption` and its
  buttons) and any other dialog the port draws itself.
- **A package can supply or override them.** A catalog file may hold `[[engine]]` entries:

```toml
[[engine]]
key  = "command.skip"
text = "Skip"
```

- **Lookup order:** package entries for the selected language (later package wins), the engine's embedded table
  for that language, then the embedded Japanese table. That is the shell's current fallback with one layer in
  front.
- **Per-game wording** falls out of this, since a package with game content targets one profile.
- **Game text and engine text never share a namespace.** Engine entries are keyed by name; game entries by
  source text.
- **The driver is unaffected.** It reports controls by their Japanese text whatever is shown (shell spec §8), and
  it reports game text as source.

### 11.1 Language packs for the engine alone

A package may target no profile, so one pack can add a shell language for every game.

- **Manifest.** A version 2 manifest may leave out the `[game]` table. Every profile's loader accepts such a
  package.
- **Contents.** `[[language]]` definitions, catalog files that hold only `[[engine]]` entries, and `fonts/`.
  Anything tied to a game is an error in it: `[[text]]` entries, assets, patches and scripts.
- **Installing.** It installs like any other package, in a profile's `mods/` folder or with `--mod`. A folder
  shared by every profile is left to the launcher (mod loader spec §12).
- **Save stamps.** It is presentation-only (§8).
- **Lua.** The Lua design asks for profile-less packages too, for shared library code. The rule that a package
  may omit `[game]` is the same one; what such a package may hold beyond the list above is for that design to
  say.

## 12. Images and other assets

The fan translation replaces 243 images. Replacement remains the mechanism; the addition is that it is keyed by
language.

- **`assets/<language>/NAME`** overrides the asset of that name while that language (or one that falls back to
  it) is selected. It applies to images, voices and movies alike, so a dub ships the same way.
- **Resolver order.** Load order decides between packages, as it does today. Within one package, its language
  asset for the selected language ranks just above its own plain asset of the same name. Below the stack come
  `--overlay-root` roots, loose files and archives, unchanged (mod loader spec §6.5).
- **Why not "all language assets first".** Ranking every package's language assets above every plain asset was
  considered. A translation early in the load order would then beat a later content mod that replaces the same
  image, and the mod's change would silently not appear. What a mod changed matters more than which language it
  is in. A translation of that mod's image ships in the mod, or in a package that loads after it.
- **Reported.** The link report lists each language asset that a later package's plain asset hides, so a
  translator can see which images need a new version.
- **Formats.** The resolver decodes AGF, and BMP under `--allow-bmp-as-agf`. It has no PNG path. A translator
  should not need an AGF encoder, so PNG input for image names is part of this work.
- **Switching** takes effect when an asset is next loaded.

**Later, not in version 1:**

- **Region patches.** Composite a small image onto a rectangle of an atlas, instead of shipping the whole sheet.
- **Text recipes.** For plates that are text on a plain background: a textless base image, a catalog key, a font
  and a position, rendered and cached by the port. A second language then needs no image editing.
- **Subtitles** for movies and voice-only lines.

## 13. Tools

New `Ome.Cli omp text` commands:

| Command | Effect |
|---|---|
| `omp text extract --language TAG --output DIR` | write catalog templates from the installed scripts: every message per script (with its authored line breaks in `note`), every string literal as a global candidate, and suggested patterns where literals feed `concat`. Run again, it keeps existing translations and marks entries whose source changed or disappeared |
| `omp text check [--mod PATH …]` | validate catalogs without starting the game: syntax, placeholders used on both sides, ambiguous patterns, sources that match nothing, glyph coverage for bundled fonts |
| `omp text stats` | translated and untranslated counts per script and language |
| `omp text po-export`, `po-import` | convert a language to and from gettext PO for translation tools (`msgctxt` carries the scope) |
| `omp text import-scripts --from GAME_ROOT [--overlay-root DIR] --output DIR` | build a catalog from an install whose scripts were translated in place, by aligning each script with the base and joining hand-broken lines back into messages. Writes the catalog and `import-report.json` (§18.1) |
| `omp text import-scripts … --verify` | replay: present every base message and literal through the catalog just written and compare with the translated install's text. Runs without the game |

`import-scripts` is what turns an existing translation into a package. The Kamidori diff in §2 shows the
alignment is mechanical: the instruction streams match once the added line breaks are removed. The translated
text belongs to its translators, so the project runs this only as a local test (§18.1) and ships none of the
output.

## 14. Diagnostics and driving

- **Miss log.** With `--text-log`, the port writes every string that reached a boundary without an entry to
  `diagnostics/text-misses.jsonl`: source text, site, count. Overflow reports (§7.3) go to the same file. Playing
  the game, or running the drive scenarios, finds the strings static extraction cannot.
- **Pseudo-language.** `--language x-pseudo` presents every string lengthened and bracketed, with no catalog. It
  shows at a glance which text bypasses the boundary and which layouts break with longer text.
- **Locator.** The F2 HUD and the F3 copy add the catalog entry that supplied the current message, if any.
- **Driver.** `state`, the page map and `expect` keep source text and positions. Scenarios therefore run
  unchanged under any language.
- **Page map.** With a language active, each page record adds a `presented` field beside the source dialogue.
  Gate 11 compares on it.

## 15. Engine changes

| Part | Change |
|---|---|
| `Ome.Engine` (new `Localization/`) | `TextCatalog` (immutable, per language), `TextPresenter` (resolution, patterns, message table, cache, filter chain), the message segmenter |
| `VirtualMachine` | the two presentation sites pass source and presented text; the three length opcodes consult the presenter. The VM holds one `TextPresenter` reference and no other new state |
| `AdvTextHistory` | a second, unsaved cursor per layout; an unsaved table of resolved messages per group; group re-presentation for `0x1d1` and `0x82` |
| `IAdvHost` | the live run carries the presented text, break positions, alignment and fit; the result returns source rows and both cursors. No text measurer: lengths are counted in the engine (§5.3) |
| `RetainedGlyphLayoutEngine` | break at allowed positions; a measure-only pass for the source rows |
| Godot host | text server rasterizer for every run while a language is active; per-language font substitution; the wait indicator follows the presented layout |
| `Ome.Mods` | `text/`, `fonts/`, `assets/<language>/`, `[[language]]`, merge and report, presentation-only stamps; a base-offset table per patched script in the link cache, if `at` ships (§6.1) |
| `Ome.Engine.Frontend` | `ui:Language` accepts any tag; language list from the stack; engine-string lookup with the package layer |
| `Ome.Cli` | `omp text` commands |

The presenter is a component the VM is given, not more fields on it. It fits a later split of `VirtualMachine`
into components and does not depend on one.

Catalogs are immutable once mounted and the presenter is called only on the VM thread, so no locking is needed
beyond swapping the snapshot reference.

## 16. Untrusted input

Catalogs are data from untrusted packages (mod loader spec §11):

- bound the entry count per language and the length of any one string;
- placeholders are the only matching syntax; there are no regular expressions;
- font files are parsed by the font library, which is an attack surface the engine does not control. Fonts load
  only from enabled packages, with a size bound.

## 17. Future work

- **Lua filters** (§6.5), when the runtime exists.
- **Automatic paging** on overflow (§7.3).
- **Ruby in presented text,** for languages that want it.
- **Shaping and right-to-left layout** (§7.6).
- **Region patches, text recipes and subtitles** (§12).
- **A presented default in the name editor** (§5.1). The field would show the catalog's text for the default
  name. Two rules make it safe: a name accepted unchanged leaves the cell's source value in place, and typed
  ASCII is widened to full width before it is stored, because the stored value must stay double-byte CP932.
- **Native export.** Baking a catalog into translated scripts for native AGE, with native's limits: CP932 only,
  hand line breaks, and compares against translated literals. This is what fan translators do by hand today.

## 18. Validation gates

| Gate | Passes when |
|---|---|
| 1. Off means off | with no catalog for the selected language, every existing gate and drive scenario passes unchanged |
| 2. Identity | a Kamidori scenario driven with a language active and `measure = "source"` writes a save whose decoded payload equals the source-language save's (`Ome.Cli save-diff`); with `measure = "presented"` the differences are confined to `G[0x4f8]` and frame locals (§2). The headless host and the Godot host produce the same payload |
| 3. Catalog | merge, override order, scope precedence and the language fallback chain behave as §6.4 says; malformed files stop the load with file and line |
| 4. Messages | a sentence split by furigana presents as one wrapped sentence; a branch target inside a run splits it; a miss falls back per run |
| 5. Patterns | the level-up line presents with its number moved; a captured item name is itself translated; a full-width number comes out in ASCII under `digits = "ascii"`; an ambiguous pair fails `omp text check` |
| 6. Measurement | the speaker's name plate is sized to the presented name on Kamidori; an entry's `cells` value overrides the count |
| 7. History | the backlog shows presented text before and after a save and load, and changes with the language; the click-wait indicator sits after the last presented glyph |
| 8. Assets | a language asset overrides its package's plain one only while its language is selected; the fallback chain applies; a later package's plain asset wins over an earlier package's language asset, and the link report lists it |
| 9. Pseudo-language | the committed drive scenarios run under `x-pseudo` with the same VM positions as the source run |
| 10. Import, local only | `import-scripts` converts the translated Kamidori install; `--verify` reproduces its text for every message and literal; every item the catalog could not carry is on a reviewed list (§18.1) |
| 11. Run comparison, local only | the port driven over the same scenarios on the translated install and on the clean install with the imported catalog shows the same text on every page (§18.1) |

Gates 1–9 use synthetic catalogs written for the tests and run in CI. Gates 10 and 11 need an install the
project cannot ship, so they run locally, as the native comparisons do.

### 18.1 The import test

An existing script-based translation is the best test this design has. It is tens of thousands of real strings
that someone already made work in this engine, and every choice the translators made is a case the catalog
either carries or does not. Synthetic catalogs only test the cases their author thought of.

The test has two halves. The first needs no engine changes, so it runs before any are made.

**Half 1: conversion (gate 10, milestone 0).** `import-scripts` aligns each translated script with its base
script and writes a catalog and a report. The report answers the questions the design rests on:

| Report line | What it tests |
|---|---|
| scripts that do not align once text instructions are set aside | whether alignment is mechanical. §2 found none in 473 |
| messages converted at global scope, script scope and site scope | decision 2 (the key is the source text). A source string translated two ways in one script needs site scope |
| messages that could not be joined into one entry | §6.2. For example, a translated sentence that spans a page wait where the source did not |
| changed literals outside messages (`set-string`, `draw-string` and `concat` operands) | whether table and UI strings convert as plain global entries |
| changed literals that feed compares or the value switch | §5.1. The three known ones should need no entry, because the base literal stays in place |
| emptied furigana runs | §6.2. The catalog drops ruby from the presented text, as the translators did |
| literals that feed `concat` | which patterns (§6.3) a real translation needs |
| changed files that are neither scripts nor images | what the design has no home for |
| replaced images | the list that becomes `assets/<language>/` (§12) |

`--verify` then presents every base message and literal through the catalog and compares the result with the
translated install's text, ignoring line breaks and runs of spaces. A mismatch means two different translations
ended up under one key.

The numbers decide the open question in §20. If almost nothing needs site scope, `at` can leave version 1. If a
large share of messages cannot be joined, §6.2 has to change before anything is built on it.

**Half 2: run comparison (gate 11, milestone 6).** The port already runs both installs
(`docs/modding/games/kamidori.md`). Drive the same scenarios with the same `rand-seed` on each:

- run A: the translated install, no catalog;
- run B: the clean install with the imported catalog.

Page N of run A must show the same text as page N of run B. Run A's page map `dialogue` is compared with run
B's `presented` field, ignoring line breaks and runs of spaces. Offsets are not compared, because the
translated scripts carry extra instructions.

Two more results come from the same runs:

- **Wrapping.** The translators fitted every line by hand. Each overflow report (§7.3) in run B is a page where
  the engine's wrapping did worse than they did. These are reviewed, not required to be empty, because fonts and
  glyph widths can differ between the runs.
- **Saves.** Run B's save must pass gate 2 against a plain Japanese run. A save from the translated install
  cannot, because it holds the strings its translated scripts wrote.

**What it does not cover.** The translated install's engine is 4.33F and the clean one is 4.33G
(`docs/engine-re.md`). Both runs here are the port, so native behaviour is not compared. Text the scenarios do
not reach is covered by half 1 only.

**Other translations.** The test is not specific to Kamidori. Any install whose scripts were translated in place
can go through both halves, and each one that does is another check on the design.

## 19. Milestones

0. **Import check.** The catalog format, the message segmenter, `import-scripts` with its report and `--verify`.
   No engine or host changes. Gate 10. Its numbers are reviewed before milestone 1 starts, and §6 is revised if
   they call for it. The imported catalog then serves as local test data for every later milestone.
1. **Core.** Catalog, presenter, exact lookup at the two sites, `ui:Language` wiring, word wrapping, Unicode
   raster path, miss log, pseudo-language. Gates 1, 3, 9.
2. **Messages, patterns, measurement.** Gates 4, 5, 6.
3. **History and saves.** Source layout pass, second cursor, history re-presentation. Gates 2, 7.
4. **Packages and tools.** `text/`, `fonts/`, `[[language]]` in the loader; `omp text extract`, `check`, `stats`;
   the modder guide under `docs/modding/`.
5. **Engine-owned text.** Remaining fixed strings to keys; the package layer; languages listed from the stack.
6. **Assets and run comparison.** Language assets, PNG input, PO conversion, the page map's `presented` field.
   Gates 8, 11.

## 20. Settled questions, and the one still open

Settled on 2026-11-10, by the design review and the user:

| Question | Decision | Alternative that was set aside |
|---|---|---|
| What the length opcodes return | the presented text's length by default, counted by character class (§5.3) | source lengths by default with a per-string opt-in: saves stay strictly identical, but plates and centred labels are wrong until a translator marks them |
| `halve-strlen` on an odd length | rounds up for presented text (§5.3) | truncating, as native does: an odd-length Latin name overhangs its plate |
| What history rows a save holds | source rows (§8) | presented rows: no second layout pass, but a save's backlog is tied to one language and to CP932 |
| Shipped catalog format | TOML, with PO conversion (§9.3, §13) | PO in the package: it has no natural place for `align`, `fit`, `at`, `measure` or `cells` |
| Packages that target no profile | allowed, for engine language packs (§11.1) | one copy of a shell language per game |
| Switching language while playing | fully switched after returning to the title, with a note in the options panel (§10) | requiring a restart |
| Asset precedence between packages | load order decides; a language asset ranks only above its own package's plain asset (§12) | every language asset above every plain asset, with the conflict reported |
| Name entry | a limitation of version 1 (§5.1) | presenting the default in the editor now (§17) |

Still open:

- **Site scope.** Whether `at` ships in version 1. It needs the offset table of §6.1. Milestone 0's count of
  messages that need it decides (§18.1).
