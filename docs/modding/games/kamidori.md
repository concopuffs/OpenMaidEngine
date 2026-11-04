# Modding Kamidori

Kamidori-specific facts for the generic guides. The identity block (profile id, engine ABI, modules, tables,
patch points) is generated in [README.md](README.md#kamidori-alchemy-meister).

## Install and options

The expected install is `Kamidori/` beside the repository, optionally with the English fan translation in
`Kamidori/patch/`. Every `Age.Cli` command needs the game options:

| Install | Options |
|---|---|
| untranslated | `--profile kamidori --game-root ../Kamidori` |
| translated | `--profile kamidori --game-root ../Kamidori --overlay-root patch` |

- Port: `.\run-godot.cmd -Kamidori`, or `.\run-godot.cmd -Kamidori -TranslationPatch` for the translation. Add
  `-Mod PATH` for a mod.
- Driver, translated:

  ```text
  py -3.11 -X utf8 tools/drive.py --profile kamidori port start --mod PATH --port-arg=--overlay-root --port-arg=<absolute path to Kamidori\patch> --port-arg=--allow-bmp-as-agf
  ```

  Kamidori has no predefined points, so define them with `point NAME X Y`. On the title screen, Game Start is at
  `700 300`; on the next screen, Begin Game is at `735 401`.
- Registries for fragments: `py -3.11 -X utf8 tools/globals_build.py --profile kamidori --build`.

## The translation is a different base

The translation replaces scripts such as `SC0000.bin` with translated ones, which have different bytes and
offsets. A fragment matches one of them only. Print fingerprints and decompile with the same options you play
with, and ship two fragments if the mod should work on both.

| `SC0000.BIN` | Fingerprint |
|---|---|
| translated | `sha256:54d6bc4d8cf85e32903808979d16ff851f10e62f06dcb8ab87bc8076a31c1e30` |
| untranslated | `sha256:e8db06e1287c557533e93380084358dbc28beb684b3344f3890852514ad97f58` |

The install's root also holds the translation's `$n$*.bin` append scripts. The port and native AGE load them even
without `--overlay-root patch`, so append content shows in English either way (`../../backlog.md`). Their strings
are partly damaged, so do not copy code from them.

## First mod values

The values for [first-mod.md](../first-mod.md), on the translated opening scene:

| Step | Value |
|---|---|
| Page | The prologue's first page. |
| Site | `0xd0a` |
| Fingerprint | the translated one above |

```text
oms 1
patch "SC0000.BIN" fingerprint "sha256:54d6bc4d8cf85e32903808979d16ff851f10e62f06dcb8ab87bc8076a31c1e30"
use kamidori.globals
use kamidori.adv

before site 0xd0a {
    "[MOD] Hello from my first mod."
}
```

The finished mod is `oms/examples/mods/example.kamidori-prologue-line`.

## Dialogue lines

The prelude's template is `line(text, speaker = none, voice = none, form = form_name)`:

```text
"[MOD] A narration line."
水那 "「…………」" (voice: 0x1be, form: form_descriptor)
```

`form` picks what the nameplate shows:

- `form_name`, the default (leave it out);
- `form_alternate`;
- `form_descriptor`, a description shown before the character is introduced;
- `form_unknown`, `？？？？`.

Speakers come from `unit_speaker_names` (`enum speaker` in `oms/kamidori/adv.oms`).

## Data edits

A boot script can edit the unit, item, skill and stage-enemy tables. This one changes the enemy unit 761, which
appears in the first stage (`oms/examples/kamidori-gate6/OMSAUTO.oms`):

```text
with unit[761] {
    .stats.max_hp = 150
    .experience_reward = 999
    .drop_chance.drop_1 = 100
}
```

The unit tables sit above the saved range, so boot rebuilds them with the edit on every start, and the edit holds
after loading a save. Saves made before the edit pick it up too. `../../kamidori-data-tables.md` explains how the
init scripts fill each table.

## Catalogs, scenes and saves

- **Selectors.** The install ships three append catalogs (`APPEND01`–`03`), so a first mod's catalog normally gets
  selector 4.
- **Scene registry.** SCJUMP decision 0 already maps to SC0000. Writing a mod scene into
  `scjump_scene_script_resource_ids[0x0]` replaces the opening instead of preceding it. Pick the decision your
  scene belongs to.
- **Untested.** No Kamidori save-capable scene mod has been tested yet (`../../backlog.md`). Copy the skeleton from
  one of Kamidori's own append scenes, and test saving on native AGE as well as in the port.
- **Free cells.** The examples use the string cells `GS[0x9000]` and `GS[0x9001]` (`oms/examples/kamidori-gate6/`).

## Examples

| Path | What it shows |
|---|---|
| `oms/examples/mods/example.kamidori-prologue-line` | a patch against the translated opening |
| `oms/examples/kamidori-gate6/` | a boot-time data edit and a script called by packed id |
