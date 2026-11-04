# Your first mod

This walks through one complete mod on each game: insert one line of narration into the opening scene, check that
it links, run it in the port, and export it for the original engine. Each step shows the Himegari and Kamidori
commands side by side. The finished results are in `oms/examples/mods/`:

- `example.sc0000-page3` (Himegari);
- `example.kamidori-prologue-line` (Kamidori with its translation).

Commands run from `age-reimpl/` unless a step says otherwise. `Age.Cli` is the command-line tool:

```text
dotnet run --project engine/Age.Cli -- <command> [options]
```

The examples below shorten that to `Age.Cli <command>`.

## 0. Prerequisites

- .NET 8 SDK, Python 3.11 and Godot 4.7 .NET, as for the port itself (`../../README.md`).
- The game install beside the repository: `../Himegari_Game` or `../Kamidori`.
- One build: `dotnet build engine/AgeEngine.sln`.
- The generated registries: `py -3.11 -X utf8 tools/globals_build.py --build` (and with `--profile kamidori`),
  and `py -3.11 -X utf8 tools/patch_points_build.py --build`. Fragments that `use himegari.globals` or
  `kamidori.globals` need them.

Every `Age.Cli` command selects the game with `--profile` and `--game-root`. Without them it uses Himegari. For
Kamidori with the fan translation, also pass `--overlay-root patch` (relative to the game root), because the
translation replaces scripts and therefore changes the base your fragment edits:

| Game | Options every command needs |
|---|---|
| Himegari | *(none)* |
| Kamidori, untranslated | `--profile kamidori --game-root ../Kamidori` |
| Kamidori, translated | `--profile kamidori --game-root ../Kamidori --overlay-root patch` |

## 1. Find the place to edit

Run the game in the port and stop on the page you want to change:

```text
.\run-godot.cmd                                  # Himegari
.\run-godot.cmd -Kamidori -TranslationPatch      # Kamidori with the translation
```

Press **F2** to show the page locator and **F3** to copy it. A locator reads like
`SC0000 P003 · wait SC0000@0x8a6 · text SC0000@0x89e`. Then resolve it to bytecode:

```text
py -3.11 -X utf8 tools/locate_page.py SC0000 3 --context 12
```

The output lists the page's text, its `show-text` and `wait-for-input` offsets, and the disassembly around them.
Offsets are dword offsets into the script; those are the *sites* a fragment names.

To insert a new page before an existing one, attach it to the instruction that starts the page, which is the
`reset-adv-text-layout` right after the previous page's wait. On Himegari's third page:

```text
    0x00871: wait-for-input 0x0          ; end of page 2
    0x00874: end-text-line 0x0
    0x00877: reset-adv-text-layout 0x1   ; start of page 3: insert before this
```

`patching.md` covers choosing sites in more detail.

## 2. Get the base fingerprint

A fragment names the exact bytes it edits. Print the header line for the script:

```text
Age.Cli oms-patch --fingerprint SC0000.BIN
Age.Cli oms-patch --fingerprint SC0000.BIN --profile kamidori --game-root ../Kamidori --overlay-root patch
```

Each prints a line such as `patch "SC0000.BIN" fingerprint "sha256:0297…"` to paste into the fragment. Use the full
file name, including `.BIN`.

## 3. Write the mod

Create a folder; its name is up to you. Give the mod a lowercase dotted id, ideally one that starts with a name you
own:

```text
my.first-line/
├── manifest.toml
└── patches/
    └── opening.oms
```

`manifest.toml` (Himegari):

```toml
omp = 1
id = "my.first-line"
name = "My first line"
version = "1.0.0"
description = "Adds a line of narration before the opening's third page."

[game]
profile = "himegari"
target = "native"
```

For Kamidori, set `profile = "kamidori"`.

`patches/opening.oms` (Himegari):

```text
oms 1
patch "SC0000.BIN" fingerprint "sha256:029718d6c927f90f6dcb1b89851591be310b5889dd53d6c8892604ad1fc9a9f6"
use himegari.globals
use himegari.adv

before site 0x877 {
    "[MOD] Hello from my first mod."
}
```

`patches/opening.oms` (Kamidori, translated):

```text
oms 1
patch "SC0000.BIN" fingerprint "sha256:54d6bc4d8cf85e32903808979d16ff851f10e62f06dcb8ab87bc8076a31c1e30"
use kamidori.globals
use kamidori.adv

before site 0xd0a {
    "[MOD] Hello from my first mod."
}
```

The quoted line is a *dialogue statement* from the game's ADV prelude (`oms/<profile>/adv.oms`). It expands to the
same instructions as the game's own narration pages: show the text, wait for a click, reset the text layout. Add a
speaker and voice the same way the decompiled scenes do (`oms-language.md`, "Dialogue lines").

## 4. Check that it links

```text
Age.Cli omp check --mod my.first-line
Age.Cli omp check --mod my.first-line --profile kamidori --game-root ../Kamidori --overlay-root patch
```

A good result ends with `omp check: links: 1 mods; 0 errors, …`. The note "compiled … at load time (no stored
plan)" is expected while you develop. `omp build` removes it (step 7).

A wrong fingerprint, a site that is not an instruction start, or a syntax error stops here with the file, line and
column (`troubleshooting.md`).

## 5. Run it in the port

```text
.\run-godot.cmd -Mod my.first-line
.\run-godot.cmd -Kamidori -TranslationPatch -Mod my.first-line
```

Start a new game. Your line appears as its own page at the place you chose. The console prints `[mods]` lines with
the load order and every file the mod supplied. The full link report is written to
`diagnostics/mod-link.txt` in the profile's data folder (`cli.md`).

To keep a mod installed rather than passing `-Mod` each time, copy its folder (or its `.omp`) into the profile's
`mods/` folder (`cli.md`, "Installing mods in the port").

## 6. Run it in native AGE

Native AGE cannot read mods, so export the linked result as game files:

```text
Age.Cli omp export --output-dir build/my-export --mod my.first-line
```

For this mod that writes one file, the patched `SC0000.BIN`. Native AGE reads loose `.BIN` files beside `AGE.EXE`
before its archives, so the file overrides the original. Install it into a *copy* of the game, never the original:

```text
py -3.11 -X utf8 tools/native_scratch.py --replace --overlay build/my-export/SC0000.BIN
```

That builds `build/games/<profile>/native-scratch/` with your files added. Run its `AGE.EXE`, or drive it with
`tools/drive.py native` (`../tools-reference.md`, "Game driver").

## 7. Package it

```text
Age.Cli omp build my.first-line
```

`omp build` compiles every fragment against the base and writes `my.first-line-1.0.0.omp` beside the folder. The
package is a ZIP file with fixed timestamps, so the same sources always produce the same bytes. Inside it, each
fragment's compiled transform plan sits beside its source (`patches/opening.omspatch.json`). Your folder is not
changed. Players load the stored plan instead of compiling your source on
their machine. The source stays in the package so anyone can read and rebuild it.

## Where next

- More operations (`after`, `replace`, operand edits) and how mods combine: [patching.md](patching.md).
- A scene of your own, or a data edit at boot: [new-scenes.md](new-scenes.md).
- Every manifest field: [packages.md](packages.md).
