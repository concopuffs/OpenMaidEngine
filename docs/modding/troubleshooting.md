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
| `interaction` | Several mods insert at the same site, several Lua handlers are due at one hook, or two Lua mods list one global in `[lua] writes`. They all apply, in the order shown. | no |
| `override` | Several mods supply the same asset or script. The winner is named. | no |
| `note` | Information. Examples: a fragment compiled at load time, a stray file in a package, a table entry that now points at replacement code, or a record 0 that would hang native AGE at boot (`warning: record 0 runs at boot …`). | no |

In the port, a report with errors or conflicts stops startup with exit code 2. `--skip-failed-mods` drops the mods
involved and starts with the rest, naming what it dropped.

A stack with Lua ends with a `lua:` section: each mod's capabilities and writes, each hook's handlers in order, and
each event's subscribers ([lua.md](lua.md), "The link report and the trace").

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

**Names that do not resolve** usually mean a missing `use` line. Registry names need `use <game>.globals`, and
dialogue lines and speakers need `use <game>.adv`. If the registries were never generated, build them as described
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

## Startup refusals

A data root that holds a `profile.toml` is a player profile: the file names the game, the install and the mods
(`../tools-reference.md`, `--data-root`). The port stops at startup (exit code 2) when the profile and the command
line disagree, when the profile or its root is unsafe or wrong, and, for any data root, when another engine runs
on it:

**"`--game-root` is refused with a profile file".** The same goes for `--game`, `--profile`, `--overlay-root` and
`--allow-bmp-as-agf`. The profile's saves and selectors belong to its game and install, so a flag cannot swap them
for one run. Change the profile, or test with a data root that has no `profile.toml` (the driver's sandboxes have
none). `--mod`, `--no-mods`, `--skip-failed-mods` and the run options still work with a profile.

**"`--probe` is refused with a profile file".** Probing needs a game named on the command line. Probe with a data
root that has no `profile.toml`.

**"the install at … is …, not the game '…'".** The profile's `[install] root` holds another game's `SYS4INI.BIN`.
Point it at the game's own install.

**"`ome_profile`: format version N is not supported".** A newer engine wrote the profile. Use that engine, or a
profile this engine made.

**"… : unknown key".** The file holds a key this engine does not know. `profile.toml` is strict, as `manifest.toml`
is: a mistyped key would otherwise be dropped silently.

**"… is a symbolic link or junction; a profile root may hold none".** Something inside the profile root (a
folder or a file, hidden or not) is a link. The port writes into the root, so a link could send a write somewhere
else. Replace the link with the real folder, or move the profile root. A cloud folder's placeholders (OneDrive and
the like) are not links.

**"… names the network path `\\host\share\…`, which this machine's player has not approved".** The profile names a
folder on another computer. Opening it would contact that computer and offer it your Windows sign-in, so a profile
from someone else must not be able to pick the computer. Approve the location in the launcher. To approve it for one
run only, pass `--allow-network-paths`. A share mapped to a drive letter (`Z:\…`) needs no approval, and neither does
a network path inside the profile root.

**"… is in use: another engine is running this profile, or the launcher is changing its settings".** This one
holds for every data root, not only a profile: each run holds its root's `run.lock` while it runs. Two engines on
one root would overwrite each other's saves and settings, so the second stops having changed nothing. Close the
other game, or wait for the launcher to finish. A game that crashed leaves `run.lock` behind; that is harmless, and
the next start takes it.

**"profile.toml enables ID, which none of its mod folders holds".** The profile's `[mods] order` names a mod
that is in none of its `folders`. Put the mod in one of them, or take it out of `order`. With
`--skip-failed-mods` the port starts without it.

**"profile.toml enables ID, which is found 2 times".** Two folders, or one folder, hold two packages with that id,
such as two versions of one mod. Keep one.

**"requires ID, which is in … but not enabled in profile.toml".** An enabled mod needs another mod that is in a
folder but switched off. Enable that one too. The launcher does this when you enable a mod.

**"the profile's mode is native, which runs only what native AGE runs, and it …".** The profile is in
Compatibility mode (`mode = "native"` in its `profile.toml`), and the mod targets `extended`, ships Lua, or requires
such a mod, such as the game's base mod. Switch the profile to Extended mode in the launcher (`mode = "extended"`),
or leave the mod off.

**A note that a file "is left out".** A package in a shared folder could not be read, and the profile does not
enable it. Nothing else happens. If you meant to use it, replace the file.

**A warning that `install.fingerprint` differs.** The install's `SYS4INI.BIN` changed since the profile accepted it,
for example after an official update. The port continues; check that your mods still name the right revision.

## In-game problems

**Native AGE hangs at boot, before the title.** Your record 0, the first `[scripts].names` entry, shows text or waits
for input. Record 0 runs during boot. Put a short boot script that ends with `exit` first ([packages.md](packages.md),
"Record 0 runs at boot").
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

**Text is cut off at the bottom of the window.** The translation needs more rows than the window has, even with its
glyphs narrowed to the language's `min_fit_scale` (or the language sets `overflow = "cut"`). Run with `--text-log`:
each cut message is in `diagnostics/text-misses.jsonl` with `rows_cut`. Shorten the translation, remove `\n` breaks
it does not need, or patch the script to break the page ([languages.md](languages.md), "Text too long for its
window").

**A message looks narrower than the others.** It needed more rows than its window has, and the port narrowed it to
fit (`overflow = "shrink"`, the default). `--text-log` lists it with `width_percent`, the width it got. Shorten it to
draw it at full width.

**A label runs out of its box, or is no longer centred.** Give the entry `fit = "shrink"` to narrow it to the
original's width, and `align = "center"` or `"end"` to place it as the original was. The language's `min_fit_scale`
limits how narrow it gets.

**Squares or the wrong font.** The fonts of the language's `[[language.font]]` table lack those characters, and so
do the system's. `omp text check` lists the characters each table's bundled fonts lack; add a font that has them to
`use`.

**Letters squeezed to half their width, or running past their boxes.** The table's first font is proportional to the
game: Windows marks it neither monospaced nor fixed-pitch, whatever its name says. Outside aspect mode the game draws
it at half the height over its average width, about half for a Japanese font and near its own width for a Latin one.
`omp text check` warns about it and gives the width. Use a fixed-width font (languages.md, "Fonts").

**A menu or dialog keeps the engine's wording.** The log has a `[text]` line for each `[[engine]]` entry the engine
left out, ending "the engine's own text is shown": the key is not one the engine has, or the text names a value
(`{1}`) its Japanese does not take. `omp text check` reports both. An entry without `text` is a template and is
skipped.

**"a package without [game] holds only text/ and fonts/" or "holds only [[engine]] entries".** A package for every
game carries the engine's own text only. Game text, patches, scripts and assets need a `[game]` table naming the
game ([languages.md](languages.md), "A shell language for every game").

## Lua

**A value is not what you expect, and several mods touch it.** Run with `--lua-trace` and read
`diagnostics/lua-trace.jsonl`: each handler call's line shows the event's fields before and after it and every cell
it wrote, so the first line whose `after` differs from what you expect names the mod ([lua.md](lua.md), "The link
report and the trace").

**"lua/NAME.lua: line N: …".** A syntax error, found by the compiler that runs the mod; the line is the file's. The
check runs none of your code.

**"… declares no top-level function F; a handler is a global function of its file".** The hook's handler must be
declared as `function F(ctx)` at the top level of the named file, not as `local function`, `function M.F`, `F =
function`, or inside a block ([lua.md](lua.md), "Handlers and the session").

**"a point hook hands its handler the frame and operands, which needs the vm capability".** Add
`capabilities = ["vm"]` to `[lua]`.

**"'NAME' is not a named patch point of <game>".** Point names are in your game guide and its patch-point
reference ([games/README.md](games/README.md)).

**"X replaces SCRIPT, and a hook names the original's instruction" or "a patch of X replaced the instruction".**
Another mod replaced the script, or patched away the instruction your point names. Your hook cannot bind there;
the two mods conflict as written.

**"a site hook hands its handler the frame and operands, which needs the vm capability"** (or a script hook's
"the frame"). Add `capabilities = ["vm"]` to `[lua]`.

**"X replaces SCRIPT, and the hook names the original's entry and exit".** Another mod replaces the script your
script hook names; a replaced script is that mod's code, and the two mods conflict as written.

**"the hook was written for SCRIPT sha256:A, but the base catalog's SCRIPT is sha256:B".** The installed script is
another revision than the one your site hook names; the offset may not mean the same instruction in it. Take the
fingerprint and the offset from the script as installed (`Ome.Cli oms-patch --fingerprint SCRIPT.BIN`, and the
disassembly or the page locator).

**"0x… is not the start of an instruction of SCRIPT".** A site is a dword offset where an instruction starts, as
the disassembly lists it; an offset inside an instruction's operands names none.

**"… so the after hook fires only once it has finished" or "… so the after hook never fires".** Notes, not errors:
the instruction waits or calls a script (a save in between and a later load of it skip the hook), or it ends its
frame ([lua.md](lua.md), "Site hooks").

**"skip = true on the instruction at 0x… (op …), which …".** The instruction jumps, calls a script, ends its
script, waits, saves, loads or restores frames, and skipping it would break the game's flow; the message says which.
Skip a plain instruction near it, or change the flow with a patch ([lua.md](lua.md), "Skipping an instruction").

**"ctx:skip(): this hook did not declare skip = true".** Add `skip = true` to the hook's `[[hooks]]` entry. An after
hook cannot skip: its instruction has already run.

**"… this engine does not raise it yet".** The reserved engine events are part of a later version of Lua scripting
([lua.md](lua.md), the list at the top).

**"calling NAME failed: NAME.BIN@0x… (op 0x… …) waits (…), and a called script cannot wait"**, or **"… saves"**,
**"… loads a save"**, **"… continues the load's restore"**, **"… raised a script error: …"**, **"the call ran past its
budget"**. A script `maid.vm.call` ran reached something a call cannot do; the offset and the opcode say where, in
that script or in one it called. The cells keep what the script wrote before it. Run `omp check`: its `lua.calls`
notes list each instruction of the scripts you call that may wait or restore. Set the cells the script reads so it
takes a path without them, call a smaller script, or ship a script and a patch that calls it ([lua.md](lua.md),
"Calling a script").

**"lua.calls: NAME: NAME.BIN at 0x… (op 0x… …) waits / saves / loads a save, …" in `omp check`.** A script your
`[lua] calls` lists, or one it calls (the line names the chain: `A calls B, C:`), has an instruction the call
always fails at. Take it off the list. **"… may wait, and the call fails if it does"**, **"… continues a load's
restore …"**, **"… which the check cannot follow"** are notes: the call works on paths that avoid them.

**"NAME is not in M's [lua] calls"**: add the script to `[lua] calls`. **"lua.calls: neither the catalog nor the
mod's [scripts] has NAME"**: the name is misspelled, or neither the game nor your `[scripts]` has such a script. **"lua.calls: maid.vm.call is raw access, so a mod that lists
calls needs the vm capability"**: add `capabilities = ["vm"]` to `[lua]`, or use a base mod's helper, which needs
none. **"maid.vm.call is not available in the console"**, **"… cannot run while the session is built (init.lua,
ome.session.start)"**: call it from a hook, a watch or a game event's handler ([lua.md](lua.md), "Calling a script").

**"a watch on an address names a cell by number, which needs the vm capability".** Add `capabilities = ["vm"]` to
`[lua]`, or watch the cell's name through the base mod, which needs none. **"a watch on a name resolves through a
base mod, and the mod requires none"**: add `[[requires]] id = "<game>-base"` ([lua.md](lua.md), "Watches").

**"X is a table; watch "X[*]" for its cells", "X is no table", "X is a float global", "X's row count is not
known".** A table's name takes `[*]` and a scalar's does not; version 1 watches integer and string cells only; and a
table whose size the registry does not know has no end to watch, so watch its cells by address instead.

**"watch: '…' is not a global's name, name[*] for a whole table, or G[0x…] or GS[0x…] for a cell".** A watch names a
global, all of a table, or one cell; it cannot name one element by index (`levels[3]`): watch the table and test
`ctx.index`.

**"watch handlers' writes nest 32 deep".** A watch handler writes a cell that it, or another watch handler, watches,
and so runs again on its own write. Skip the write when `ctx.writer == "mod"` and `ctx.mod` is your mod, or keep the
value somewhere no watch covers.

**"no mod of the stack declares the event 'NAME' (it declares …)".** The event is misspelt, or the mod that declares
it is not enabled; the parenthesis lists the events the stack has. **"X declares NAME, and the mod does not
require it"**: add `[[requires]] id = "X"` ([lua.md](lua.md), "Game events").

**"field F of NAME is read-only", "NAME has no field F", "field F is bool, not number".** The handler wrote a field
declared `r`, named a field the event does not declare, or gave a value of another type. Your game guide lists each
event's fields.

**"X declares NAME, which is not raised in this install: …".** The event's point does not bind in the install you
play: its script is another revision than the one the point was written for (a translation or an update that replaces
it), or the game has no such point. The message gives the reason, as the note `… not raised in this install, so a
mod that subscribes to it does not link` in the report does. Mods that do not subscribe to the event still link. Play
on the install your game guide lists for the event, or ask for the point to be added for yours.

**"events/FILE.toml: NAME: …".** A declaration of your mod does not link: the message gives the field and the
location's problem, or the point. **"X and Y both declare the event NAME"** is a hard conflict: an event has one
declaring mod.

**"NAME is raised by X, which declares it; M cannot raise it", "NAME is raised at its point", "NAME: field F is
missing".** `maid.events.emit` raises only an event your mod declares with `raised_by = "lua"`, and with every
field it declares ([lua.md](lua.md), "`maid.events.emit`").

**"M writes NAME, which its [lua] writes list does not name (…)".** Add `NAME` to `[lua] writes` ([lua.md](lua.md),
"Game names"). The message shows the list as the engine read it.

**"the source writes names its [lua] writes list lacks: NAME (lua/FILE)…", "omp build: M writes names its [lua]
writes list lacks".** The link found `game.g.NAME = …` or `game.table("NAME")[…] = …` in your source, and the list
does not name `NAME`. Replace the list with the one the note gives. Under `--lua-dev` the write goes through, and
`diagnostics/lua.log` names it once. **"lua/FILE writes NAME by name, which B does not export"**: the name is
misspelled or not in your game's global reference; the write fails when it runs. **"is packed, so --lua-dev does not
apply"**: `--lua-dev` loosens the list only for a mod loaded from a folder; run the folder, or list the name and
rebuild.

**"lua.writes: 'NAME' is not a name <game>-base exports".** Check the name against your game's global reference
([games/README.md](games/README.md)). **"lua.writes names cells of a base mod, and the mod requires none"**: add
`[[requires]] id = "<game>-base"`.

**"<game>-base exports no global named 'NAME'", "NAME is a table: use .table(…)", "NAME[i]: an index is a whole
number from 0 to N".** The name is not exported, or it was used with the wrong accessor or index. Indices start at 0,
and a table ends at its last row ([lua.md](lua.md), "Game names").

**A handler's error in `diagnostics/lua.log`.** The line names the mod, the handler, the script and offset it ran
at, and the traceback. After three failures in a row the handler is off for the session; `--lua-errors halt` stops
at the first one instead. "ran out of its instruction budget" means the call ran past 1,000,000 Lua instructions,
usually an endless loop.

**"Lua: ID was not reloaded: …"** (with `--lua-reload`). The check of your changed files found a problem, and the
message gives it as the link would: `lua/FILE: line N: …`, "declares no top-level function F, which a hook names",
"lua/FILE is gone, and a hook names its F", or "its maid.state cannot be carried into the new session" with the value
a save cannot hold. The game goes on with the code it had. Fix the file and save it again. A hook on a new handler,
and any change to the manifest or `events/`, needs a restart ([lua.md](lua.md), "Reloading").

**"Lua: reload failed, the running session is kept: ID: …".** The new files parsed, but `init.lua` or a handler
file's top level raised an error as the session was rebuilt (`diagnostics/lua.log` has the traceback), or a mod your
mod requires did. The game goes on with the old code. The new files stay as they are on disk, so a load or a return
to title before you fix them runs them, and your mod is off for that session.

**Nothing happens when you save a file under `--lua-reload`.** Only a mod loaded from a folder is watched, and only
the `.lua` files under its `lua/`: the startup log's `[lua] --lua-reload watches` line lists the folders. At a text
wait the rebuild waits for the click; `Lua: reloaded ID` then shows.

**"maid.state was not saved in slot N, its last good state is kept: …"** (or "it has none saved"). Your mod's
`maid.state` holds something a save cannot hold; the message names it and where it is, as in `maid.state.items[3].f
is a function`. The game's save was written, and beside it the state your mod loaded or saved last. A state holds
booleans, finite numbers, strings and tables of them, with string or integer keys ([lua.md](lua.md), "State").

**"save slot N was made with X, which ships Lua, but SAVEnn.modstate.json is missing".** The stack stamp beside the
save names a Lua mod, so the save had a state file, and it is gone: copied without it, or deleted. The mod's state
starts empty. Copy a save with every `SAVEnn.*` file beside it.

**"SAVEnn.modstate.json does not belong to its save" or "… cannot be read".** The state file was written for another
save (the game stopped between writing the save and its state file, or the files of two slots were mixed), or it
is not a state file the engine can read. Every mod's state starts empty for that load. The next save writes a new
one.

**"off for this session: its saved state cannot be read: …".** The mod's block in the state file is damaged or was
edited by hand. The mod is off for the session; its block is kept unchanged in the next save.

**A total in `maid.state` grows at every load.** Handlers fire while a load restores the game, and on the page the
save was made in a second time. Skip the restore with `maid.session.restoring` ([lua.md](lua.md), "Loads").

**A number prints with up to 17 digits** (`0.1 + 0.2` reads `0.30000000000000004`, `1/3` reads
`0.3333333333333333`). This build's Lua library writes fractions as .NET does, not with Lua 5.2's 14 digits; whole
numbers print the same. Format a number before you show it, for example `string.format("%.2f", x)`, which gives the
same text on every build ([lua.md](lua.md), "The sandbox"). Avoid `%e` and `%g` in this build.

**"bad argument #N to 'format' (number has no integer representation)".** In this build `%d` and `%x` need a
whole number, where Lua 5.2 truncates. Pass `math.floor(x)`.

**The game hangs with a Lua mod enabled.** A pattern that backtracks (`string.find` and the other pattern functions)
is not bounded by the budget. Disable mods and enable them again one at a time to find the one.

## Game-specific problems

Each game guide lists its quirks and current limits, such as installs with several bases, missing patch points, or
recipes not yet tested on that game ([games/README.md](games/README.md)).

## Outside the repository

An exported port build cannot compile new scripts, or fragments without a stored plan, because the OMS modules and
registries live in the repository ([cli.md](cli.md), "Outside the repository"). Build packages with `omp build` and
ship the `.omp`.

**"the OMS modules in … do not build: …; they use the generated …globals.json, which is missing".** The other way
round: a program inside a repository checkout links with the checkout's OMS modules and generated registries. That
covers the port, `omp`, and the launcher's Link check, even when the program was copied into the checkout. A fresh
clone has the modules but not the files built from them under `build/`. Run
`py -3.11 -X utf8 tools/globals_build.py --game <game> --build` as the message says, or run the program from
outside the checkout. The port stops at startup with this message, `omp` reports it, and the launcher shows it as a
failed Link check.
