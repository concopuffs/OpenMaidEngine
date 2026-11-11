# Language packages

A language package presents a game's text in another language. It changes no script and no asset. The scripts keep
running on their own text, and the port translates each string as it is drawn. Saves, history and read flags
therefore stay those of the original game, and a save made in one language loads in any other.

Language packages run in the port only. Native AGE draws the text its scripts hold, so `omp export` leaves a
package's text out (with a note).

## Layout

A language package is an ordinary Open Maid Pack ([packages.md](packages.md)) with format version 2 and two more
folders:

```text
<mod>/
├── manifest.toml        omp = 2, with a [[language]] table
├── text/
│   └── <tag>/           catalogs (*.toml), in subfolders if you like
└── fonts/               font files the language uses (optional)
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

   Every entry it writes has a `source` and no `text`. An entry without `text` is a template: the port skips it.
2. **Translate** by adding `text = "…"` to the entries you translate. Leave the rest as they are, or delete them.
3. **Write the manifest** (below), with `omp = 2` and a `[[language]]` table for the language.
4. **Check** the package without starting the game:

   ```text
   Ome.Cli omp text check --mod my.lang <game options>
   ```

5. **Run it** in the port with `--mod my.lang --language en` (or install it in `mods/` and pick the language with
   `[ui] Language` in `OME.INI`). Add `--text-log` to collect every string that reached the screen without an entry
   ([cli.md](cli.md)).

Run `extract` again after a game update, or whenever you want the templates current: it keeps every entry the folder
holds, translated or not, adds templates for sources no file has yet, and marks `obsolete = true` each entry whose
source the scripts no longer draw or store (an obsolete entry is skipped, like a template). It rewrites only the
files whose entries changed, and it does not keep comments, so write notes for translators in `note`.

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
min_fit_scale = 0.6                  # floor for fit = "shrink" (read, not used yet)

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
| `align`, `fit` | read and kept, but not applied yet (`../backlog.md`) |

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

## Fonts

The port draws translated text with the system's fonts by default. A `[[language.font]]` table maps the game's
faces to others: `requested` lists faces (`"*"` for all), and `use` lists files under the package's `fonts/` and
font family names, tried in order. The first table whose `requested` names a face wins. A character the listed fonts
lack falls back to the next font, then to the system's.

- `fonts/` holds `.ttf`, `.otf`, `.ttc`, `.woff` and `.woff2` files, at most 64 MiB each, without subfolders. Each
  bundled file a `use` names must be in the package.
- A bundled font draws at its own weight; the port does not embolden it.
- `omp text check` lists the characters of the translations that no bundled font of a table covers.

## Tools

| Command | What it does |
|---|---|
| `omp text extract --language TAG --output DIR` | writes or updates the templates in `DIR/text/TAG/` |
| `omp text check [--mod PATH …] [--data-root DIR] [--text-root DIR] [--language TAG] [--no-scripts]` | links the packages and validates their catalogs; exits non-zero on an error |
| `omp text stats [--mod PATH …] [--data-root DIR] [--text-root DIR] [--language TAG] [--scripts]` | counts the translated messages, strings and composed strings, per script with `--scripts` |

`check` reports errors (a file that does not read, two patterns that can match one string, too many entries, a stack
that does not link) and warnings (entries whose source no installed script draws or stores where their scope
reaches, site entries that do not bind, characters no bundled font covers). Full options are in [cli.md](cli.md).

## Saves

A package that holds only `text/`, `fonts/` and `[[language]]` changes nothing a save records. Its save stamp marks
it, and adding, removing or updating it is reported as "(presentation only)". A save made with any language loads
with any other, or with none.

## Testing without a package

`--text-root DIR` mounts a loose folder laid out as a package's `text/` (`DIR/<tag>/*.toml`), with its languages in
`DIR/languages.toml` and its fonts in `DIR/fonts/`. It loads after the packages and wins over them. Restart the port
to see an edit. `--language x-pseudo` presents every string bracketed and a third longer, with no catalog, to find
text that bypasses translation and layouts that cannot take longer text.

## Not yet

- `align`, `fit` and overflow reports;
- the engine's own menus and dialogs in a package's language (the shell has Japanese and English);
- language-specific assets (`assets/<tag>/`) and PO import and export;
- shaping and right-to-left scripts (Arabic, Hebrew, Thai, the Indic scripts).

The design behind this page is `../superpowers/specs/2026-11-10-localization-design.md`; the open items are in
`../backlog.md`, "Localization".
