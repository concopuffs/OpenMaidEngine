# Language packages

A language package presents a game's text in another language. It changes no script, and no asset outside the
language it gives versions for. The scripts keep running on their own text, and the port translates each string as it
is drawn. Saves, history and read flags therefore stay those of the original game, and a save made in one language
loads in any other.

Language packages run in the port only. Native AGE draws the text its scripts hold, so `omp export` leaves a
package's text and language assets out (with a note).

## Layout

A language package is an ordinary Open Maid Pack ([packages.md](packages.md)) with format version 2 and two more
folders:

```text
<mod>/
├── manifest.toml        omp = 2, with a [[language]] table
├── text/
│   └── <tag>/           catalogs (*.toml), in subfolders if you like
├── fonts/               font files the language uses (optional)
└── assets/
    └── <tag>/           images and sounds shown only in that language (optional)
```

`<tag>` is a lowercase BCP 47 language tag: `en`, `pt-br`, `zh-hant`. Catalog file names are free. Within a
language folder the files load in path order (`_global.toml` before `SC0100.toml`, `a/x.toml` before `b.toml`), and a
later file wins over an earlier one at the same scope.

A package may also hold patches, scripts or assets. It is then an ordinary content mod that happens to carry text,
and its save stamps say so (see "Saves" below).

## Making one

1. **Extract templates** from your install. This writes one catalog per script with its messages and the patterns
   for the strings it builds, plus `_global.toml` with every literal the scripts draw or store:

   ```text
   Ome.Cli omp text extract --language en --output my.lang <game options>
   ```

   Every entry it writes has a `source` and no `text`. An entry without `text` is a template: the port skips it. For
   a language the engine has no menus of its own for (it has Japanese and English), it also writes `_engine.toml`
   with a template for each of the engine's strings ("Engine text" below).
2. **Translate** by adding `text = "…"` to the entries you translate. Leave the rest as they are, or delete them.
3. **Write the manifest** (below), with `omp = 2` and a `[[language]]` table for the language.
4. **Check** the package without starting the game:

   ```text
   Ome.Cli omp text check --mod my.lang <game options>
   ```

5. **Run it** in the port with `--mod my.lang --language en`, or install it in `mods/` and pick the language in the
   settings panel's OME section, which lists every language the installed packages define by its `name` (or with
   `[ui] Language` in `OME.INI`). Add `--text-log` to collect every string that reached the screen without an entry,
   and every message narrowed or cut to fit its window ([cli.md](cli.md)).

A switch in the settings panel applies at once to the menus and to text drawn after it. Text already on screen stays
until the game draws it again; everything is in the new language after returning to the title.

Run `extract` again after a game update, or whenever you want the templates current: it keeps every entry the folder
holds, translated or not, adds templates for sources no file has yet, and marks `obsolete = true` each entry whose
source the scripts no longer draw or store (an obsolete entry is skipped, like a template). It rewrites only the
files whose entries changed, and it does not keep comments, so write notes for translators in `note`.

### From a translation made in place

Some translations replace the game's own scripts and images in an install. `import-scripts` turns such an install
into a package for the port. It compares each translated script with the original and writes the translated text as
catalog entries. With `--package` it also writes the images the translation replaced to `assets/<tag>/`, each AGF
as a PNG. When the folder has no manifest, it writes one with the language's `[[language]]` table:

```text
Ome.Cli omp text import-scripts --from TRANSLATED_ROOT --output my.lang --language en --package <game options>
```

- The game options name the original install. `--overlay-root` and `--allow-bmp-as-agf` apply to the translated
  install, as it is played without the port.
- `--verify` reads the catalogs back and checks every message against the translated scripts.
- The summary and `import-report.json` list what could not be carried.
- The manifest's `glyph_delay_scale` is the original's character count divided by the translation's. Its `id` and
  `name` are placeholders: edit them, since the importer leaves an existing manifest unchanged.
- Run the result as any package, with `--mod my.lang --language en`.

The text and images remain their translators' work. The package is for playing a translated install you already
have; share it only with the translators' permission.

## Manifest

```toml
omp = 2
id = "fan.himegari-en"
name = "English text"
version = "1.0.0"

[game]
profile = "<profile>"
target = "native"

[[language]]
tag = "en"
name = "English"                     # the language's own name
fallback = []                        # optional: the languages to try next; default is the tag's parents
line_break = "words"                 # "words" (default) or "anywhere"
measure = "presented"                # "presented" (default) or "source"
digits = "ascii"                     # "keep" (default) or "ascii"
glyph_delay_scale = 0.5              # reveal speed factor, above 0 and at most 4; default 1
min_fit_scale = 0.6                  # the narrowest fit and overflow make glyphs; default 0.75
overflow = "shrink"                  # "shrink" (default) or "cut"

[[language.font]]
requested = ["*"]                    # the game's faces this applies to; "*" for all
use = ["fonts/NotoSans-Regular.ttf", "Georgia"]
```

`[[language]]` needs `omp = 2`; a version 1 manifest with one is an error. Unknown keys are errors, as everywhere in
a manifest.

| Key | Meaning |
|---|---|
| `tag` | the language. Not `ja` (the source) or `x-pseudo` (the port's test language) |
| `name` | the language's own name |
| `fallback` | the chain tried after this language, before the source text. Without it, a tag's parents: `pt-br` then `pt` |
| `line_break` | where presented text may wrap: `words` breaks after spaces and hyphens and between CJK characters; `anywhere` between any two characters |
| `measure` | what the scripts' length opcodes measure: the `presented` text, so a name plate sized from a name fits the translated name, or the `source` text, so the game's state never depends on the language |
| `digits` | `ascii` presents full-width digits (`１２`) in numbers the scripts build as ASCII |
| `glyph_delay_scale` | scales the reveal's delay per glyph for translated text. English has about twice the glyphs of Japanese per sentence, so `0.5` keeps the pace |
| `min_fit_scale` | the narrowest an entry's `fit = "shrink"`, or `overflow`, makes its glyphs, as a share of their width: above 0 and at most 1; default `0.75` |
| `overflow` | what a message that needs more rows than its window has does: `shrink` (default) narrows its glyphs just enough to fit; `cut` cuts it at the window's bottom. Below |
| `[[language.font]]` | font mappings, below |

**Several packages, one language.** A package may add text to a language another package defines: give it
`text/<tag>/` and no `[[language]]` table. Their catalogs merge in load order, and the later package wins at equal
scope. When two packages both declare the language, each key the later one sets wins, and the link report names it.
If no package in the stack defines a language that one adds text to, the link stops.

## Catalogs

```toml
script = "SC0100"                    # optional: the default scope of every entry in this file
fingerprint = "sha256:…"             # required only when an entry uses `at`

[[text]]
source = "やっと匠士になる事が出来た。"
text = "I'm finally a Tradesman."

[[text]]
source = "はい"
at = 0x2a43
text = "Right."

[[text]]
script = ""                          # global, whatever the file's scope
source = "黒石"
text = "Black Stone"
note = "item name"
```

| Key | Meaning |
|---|---|
| `source` | the text as the game has it: a message, a literal, or a pattern |
| `text` | the translation. Without it the entry is a template and is skipped. `\n` forces a line break |
| `script`, `at` | the scope, below |
| `obsolete` | `true` skips the entry; `omp text extract` sets it when the source is gone |
| `measure` | this string's measurement, over the language's |
| `cells` | this string's length in half cells, for the length opcodes |
| `row_gap` | pixels between the rows of a `draw-string` whose text holds `\n`, beyond the font's height; default 2 |
| `note` | for translators; the port ignores it |
| `align`, `fit` | where the translation sits and how narrow it may get, against the original text: below |

A file that does not read stops the link (and the port) with every problem, its file and its line.

### Scope

An entry matches the exact source text. A scope narrows where:

| Scope | Written as | Matches the text |
|---|---|---|
| global | `script = ""`, or no scope in a file without one | wherever it is drawn |
| script | `script = "STATUS"` | where that script draws it, or where it was stored by that script |
| site | `script` and `at = 0x…` | at that one instruction |

The narrowest entry wins, then a later file or package. `at` is the instruction's dword offset in the original
script, as the F3 locator prints it (`text SC0100@0x2a43`) when no mod patches that script. A file with site entries
gives the script's `fingerprint` (`Ome.Cli oms-patch --fingerprint SC0100.BIN`). Site entries bind only to that
revision of the script:

- in a script another mod patches, they move with their instructions; an entry on an instruction a patch replaced is
  dropped;
- in a script a mod replaces, or one whose fingerprint differs, they are all dropped.

The link report and the port's `[text]` lines say what was dropped and why. With a patching mod loaded, the
locator prints the patched script's offsets: take `at` from a run without it, or from `extract`.

A string stored in a table and drawn later (an item name, a quest line) is looked up at the instruction that stored
it too, so `text/en/QUINIT.toml` with `script = "QUINIT"` translates what QUINIT stores.

### Messages

A sentence the script draws in pieces (text, a word with furigana, more text) is one entry: its pieces joined,
without the ruby and without the script's line breaks. `extract` writes the authored line breaks into `note`. The
translation wraps on its own; keep `\n` only for breaks that are structure, such as a header line.

### Patterns

A string the scripts build at runtime matches a pattern:

```toml
[[text]]
script = "SHOP"
source = "{item}を{count:num}個手に入れた"
text = "Got {count} {item}"
```

- `{name}` matches any non-empty text; `{name:num}` matches a number (half- or full-width digits, an optional minus).
  `{{` and `}}` are literal braces.
- A pattern matches the whole string. Each capture is itself looked up, so `{item}` comes out translated when it has
  an entry.
- A pattern that starts or ends with a placeholder needs a script scope.
- `extract` writes a message with variables as `{v1}`, `{v2}`, … and a string the scripts compose as a pattern scoped
  to the composing script.
- Two patterns at one scope that can match the same string are an error in `omp text check`.

### Alignment and fit

Some text sits where the script put the original: a title centred by its cursor, a name plate, an item name in a
fixed box. Two keys place a translation against the room the original text took, from its first character to its
last (spaces at either end do not count):

```toml
[[text]]
script = "FELLOW"
source = "鉄槌"
text = "Iron Hammer"
fit = "shrink"                       # narrow the glyphs to the original's room
align = "end"                        # "start" (default), "center" or "end"
```

- `align = "center"` puts each line's centre where the original's centre was; `"end"` puts its end where the
  original's end was. A line stays inside its window or surface: it moves no further left than the window's left
  margin (or where the text starts, when that is further left), nor past the right edge.
- `fit = "shrink"` narrows every glyph of the translation by one factor, never below the language's
  `min_fit_scale`. An original on one row gives the width: the translation's widest line is made no wider than it.
  An original that took several rows in the text window gives the rows: the translation is narrowed only as far as
  it needs to wrap into no more rows. A message that still does not fit its window is narrowed further (below).
- Both apply to text the game draws in its windows and to strings it draws onto pictures. History shows the text as
  written.
- A name plate the game sizes from the name's length is sized from the translation (`measure`, above); with
  `measure = "source"` it keeps the original's size, and `align = "center"` centres the name on it.

### Text too long for its window

A message that needs more rows than its window has would be cut at the window's bottom, as the game cuts its own
text. The port draws a translation at its letters' own widths, so one written to the line lengths of an in-place
translation can need a row more than it had there. By default (`overflow = "shrink"`) the port narrows such a
message's glyphs, all by one factor and only as far as it needs to fit, never below `min_fit_scale`. The window is
whatever the game's script set up; nothing about it is per game.

- A message that fits, or one whose entry's `fit` already made it fit, is drawn as before.
- A message that does not fit even at `min_fit_scale`, or whose own `\n` breaks make more rows than the window has,
  is drawn at full width and cut. The port does not add a page.
- `overflow = "cut"` always cuts, as the game does.
- History shows the text as written.

`--text-log` lists each narrowed message with the width it got (`width_percent`) and each cut one with the rows it
lost (`rows_cut`; [cli.md](cli.md)). To fit a message without narrowing it, shorten it or patch the script to break
the page.

## Fonts

The port draws translated text with the system's fonts by default. A `[[language.font]]` table maps the game's
faces to others: `requested` lists faces (`"*"` for all), and `use` lists files under the package's `fonts/` and
font family names, tried in order. The first table whose `requested` names a face wins. A character the listed fonts
lack falls back to the next font, then to the system's.

- `fonts/` holds `.ttf`, `.otf`, `.ttc`, `.woff` and `.woff2` files, at most 64 MiB each, without subfolders. Each
  bundled file a `use` names must be in the package.
- A bundled font draws at its own weight; the port does not embolden it.
- `omp text check` lists the characters of the translations that no bundled font of a table covers.

## Images and other assets

Text drawn into a picture (a title logo, a menu button, a map label) is not text the port can translate. Give the
picture a language version instead: put the file in `assets/<tag>/` under the name the game asks for, and it replaces
that asset while the language, or one that falls back to it, is selected.

```text
<mod>/assets/en/SO001.png      an image: PNG stands for SO001.AGF
<mod>/assets/en/VC0001.OGG     any other asset under its own name
```

- A `.png` file stands for the `.AGF` of the same name. Use PNG for edited images: any colour type and depth, with
  transparency. Any other file keeps its name and is read as the game reads that asset.
- The name must be an asset of the base game, or one your package adds in `[assets].names`. A script cannot have a
  language version: its text goes in `text/`.
- The language must be one some package in the stack defines with `[[language]]`.
- Load order decides between packages. Inside one package, its language version wins over its own plain asset in
  `assets/`. A later package's plain asset wins over an earlier package's language version, and `omp check` lists
  that: ship a version for the language in the later package, or in one that loads after it.
- A language switch shows a picture the game draws from its file at once. A picture the game copied into another
  one changes when the game next loads it, which a restart always does.
- `omp explain NAME` shows which language versions an asset has and which one wins.

## Engine text

The port's own text has keys: the menu bar and its strip, the settings panel, the quit dialog, the folder picker,
the name entry, and the error box AGE shows when a script fails. The engine has it in Japanese and English. A
catalog sets it for its language with `[[engine]]` tables:

```toml
[[engine]]
key = "command.skip"
text = "Ü&berspringen"
```

- `key` names the string, not its text. `&` marks a menu item's or button's access key; `&&` is a literal ampersand.
- A string that takes values has `{0}`, `{1}`, … where its Japanese has them. The text may reorder or leave them out
  but not add one. Write a literal brace as `{{` or `}}`.
- An entry without `text` is a template: `omp text extract` writes one per key into `_engine.toml` for a language
  the engine has no table for, with the Japanese and English in `note`.
- The shell looks a key up in the selected language's entries from every package (a later package wins), then in the
  engine's own table for that language, then the same for each language of its fallback chain, then in the engine's
  Japanese. A German package with `fallback = ["en"]` shows the engine's English for every key it leaves out.
- An entry the engine cannot use (a key it does not have, or a value it does not take) is left out, with a `[text]`
  line in the log. `omp text check` warns about unknown keys and fails on the rest.
- Game text and engine text never mix: `[[text]]` entries match the game's text, `[[engine]]` entries the engine's
  keys. One catalog file may hold both.
- The game driver reads the menus and boxes in Japanese, whatever is shown.

`extract` lists every key. The engine's own tables are `engine/Ome.Engine.Frontend/Shell/Strings/ja.json` and
`en.json` in the source, with the error box's strings (`error.*`) in `engine/Ome.Engine/Model/AgeBoxText.cs`.

### A shell language for every game

A version 2 package may leave out `[game]`. It then targets every profile:

```toml
omp = 2
id = "fan.shell-de"
name = "Deutsche Oberfläche"
version = "1.0.0"

[[language]]
tag = "de"
name = "Deutsch"
fallback = ["en"]
```

Such a package holds only `[[language]]` tables, catalogs with `[[engine]]` entries alone, and `fonts/`. A `[[text]]`
entry, `patches/`, `scripts/`, `replace/` or `assets/`, or a `[scripts]`, `[assets]` or `[replace]` table is an error
in it. Install it in each game's `mods/` folder or pass it with `--mod`. With its language selected, the shell speaks
that language and the game's text stays as the game has it.

## Tools

| Command | What it does |
|---|---|
| `omp text extract --language TAG --output DIR` | writes or updates the templates in `DIR/text/TAG/`, with `_engine.toml` for a language the engine has no table for |
| `omp text check [--mod PATH …] [--data-root DIR] [--text-root DIR] [--language TAG] [--no-scripts]` | links the packages and validates their catalogs; exits non-zero on an error |
| `omp text stats [--mod PATH …] [--data-root DIR] [--text-root DIR] [--language TAG] [--scripts]` | counts the translated messages, strings and composed strings, per script with `--scripts` |
| `omp text po-export --language TAG (--package DIR \| --text-root DIR) --output FILE.po` | writes the language's catalogs as one gettext PO file |
| `omp text po-import --language TAG (--package DIR \| --text-root DIR) --input FILE.po` | puts the PO file's translations back into the catalogs |
| `omp text import-scripts --from GAME_ROOT --output DIR [--language TAG] [--package] [--verify]` | builds catalogs from an install translated in place, and with `--package` its images and a manifest ("From a translation made in place") |

`check` reports errors (a file that does not read, two patterns that can match one string, too many entries, a stack
that does not link, an engine entry with a value its key does not take) and warnings (entries whose source no
installed script draws or stores where their scope reaches, site entries that do not bind, characters no bundled font
covers, engine keys the engine does not have). Full options are in [cli.md](cli.md).

### Working in PO

Translation tools such as Poedit, Weblate and Lokalize read gettext PO. `po-export` writes one PO file of a
language's catalogs, and `po-import` puts what the translators wrote back into them. The catalogs stay the source:
import changes only each entry's `text`, keeps every other key and the files' header comments, and rewrites only the
files whose entries changed.

- Each catalog entry is one message. Its `msgctxt` is its scope: none at global scope, the script name at script
  scope (`SC0100`), and the script and offset at a site (`SC0100@0x2a43`). An engine entry's context is `engine:` and
  its key, and its `msgid` is the engine's Japanese.
- A comment on each message gives the entry's `note` and the file and line it came from. A template exports with an
  empty `msgstr`, an obsolete entry as an obsolete message (`#~`).
- Import skips empty, fuzzy and obsolete messages and warns about messages no entry has. If a translation does not
  fit its entry (a placeholder the source does not have, or a value an engine key does not take), it writes nothing
  and lists each problem.
- PO cannot tell an empty translation from a missing one. An entry translated as empty text (`text = ""`) exports
  with an empty `msgstr` and a comment saying so; import leaves it as it is.
- gettext's `msgfmt` refuses a message whose source and translation do not both start, or both end, with a line
  break. The port does not mind; a catalog imported from a translated install can hold such messages.

## Saves

A package that holds only `text/`, `fonts/`, `assets/<tag>/` and `[[language]]` changes no script, table or asset a
save depends on. Its save stamp marks it, and adding, removing or updating it is reported as "(presentation only)".
A save made with any language loads with any other, or with none.

What the scripts compute from a measured length is saved as computed: with `measure = "presented"`, a name plate
sized from a translated name is saved at that size. `measure = "source"` keeps a save the same as the original
game's.

## Testing without a package

`--text-root DIR` mounts a loose folder laid out as a package's `text/` (`DIR/<tag>/*.toml`), with its languages in
`DIR/languages.toml` and its fonts in `DIR/fonts/`. It loads after the packages and wins over them. Restart the port
to see an edit. `--language x-pseudo` presents every string bracketed and a third longer, with no catalog, to find
text that bypasses translation and layouts that cannot take longer text.

## Not yet

- shaping and right-to-left scripts (Arabic, Hebrew, Thai, the Indic scripts).

The design behind this page is `../superpowers/specs/2026-11-10-localization-design.md`; the open items are in
`../backlog.md`, "Localization".
