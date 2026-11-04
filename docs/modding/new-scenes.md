# New scripts, scenes and assets

Patches change what is there ([patching.md](patching.md)). This page is about adding things: scripts of your own, a
boot-time data edit, a scene the player can save in, and new assets.

## Append catalogs

The games already ship add-on content as *append catalogs*: `APPENDnn.AAI` (an index) and `APPENDnn.ALF` (the data).
Himegari ships one and Kamidori three. A mod with new scripts or new assets gets a catalog of its own, built when the
mods link:

- **Records.** The catalog's records are your `[scripts].names`, in order, then your `[assets].names`
  ([packages.md](packages.md)). Each script is `scripts/NAME.oms`, a unit that begins `script "NAME"`.
- **Selector.** Each catalog has a number, its *selector*. The port allocates the lowest free one to your mod and
  keeps it in `mods/selectors.toml` for as long as the mod is installed, whatever the load order. On a clean install
  that is 2 on Himegari and 4 on Kamidori, but never rely on it.
- **Packed ids.** The engine addresses record `n` of catalog `s` as `(s << 24) | n`. Write `run NAME` and
  `asset "NAME"` and the linker fills these in, so a mod never hard-codes its own ids.

## Record 0 runs at boot

When the engine starts (INIT2, after the game's data tables are built), it runs record 0 of every mounted catalog,
in selector order. This is your mod's entry point. Record 0 is the first name in `[scripts].names`.

**Record 0 must be a short script that ends with `exit`.** It runs during boot, before the title screen. A record 0
that shows text or waits for input hangs native AGE at boot. If your mod's real content is a scene, put a small
boot script first:

```toml
[scripts]
names = ["AUTO", "SCENE"]                # AUTO is record 0
```

A mod that ships new assets but no scripts gets an empty record 0 generated for it.

## Data edits at boot

Record 0 runs after the game's own init scripts fill its data tables, so a record-0 script can overwrite table
entries. This Kamidori script changes one enemy unit (`oms/examples/kamidori-gate6/OMSAUTO.oms`):

```text
oms 1
script "AUTO"
use kamidori.globals

    with unit[761] {
        .stats.max_hp = 150
        .experience_reward = 999
        .drop_chance.drop_1 = 100
    }
    exit
```

Himegari has the same `unit`, `item` and `skill` tables. Field names and meanings are in `../global-reference.md` and
`../global-reference-kamidori.md`. `../kamidori-data-tables.md` explains how Kamidori's tables are laid out.

**Does the edit survive loading a save?** Boot runs on every start, so a table that boot fills, and that a save does
not store, is rebuilt with your edit each time. Cells inside the save's stored range are overwritten by a loaded
save. The Kamidori unit tables above sit outside it, so the edit holds after a load. Check the cells you edit
against the reference before relying on this.

## Calling your scripts

From a patch or another of your scripts, `run NAME` calls a script and returns when it ends. A script with no ADV
page (a computation or a data edit) can always be called this way.

**A script you `run` from a scene cannot be a scene itself.** The scene that calls it owns the ADV menu and the
click handlers. If the player right-clicks during a text wait inside the called script, the engine sees a callback
that belongs to the caller while the callee runs, and stops with the error *"Depth が不正です"* (the engine's
"invalid depth" error). Native AGE and the port both do this. Short pages inserted with a patch are fine, because they
run in the scene's own frame. A run of pages in a separate script needs its own scene.

## A scene of your own

To add a scene the player can open the menu, save and load in, make it a full scene that the scene scheduler enters,
as it enters the shipped ones:

1. **Copy a shipped scene's skeleton.** Decompile a shipped append scene with `--level idiomatic` and keep
   everything except its content. That means the ADV handlers, hotspots, save-resume mark, load-restore code, and
   the per-pass setup and teardown. Then put your pages in the content subroutine.
2. **Register it.** Your record-0 script writes the scene's id into the SCJUMP scene registry, the table that maps
   a story decision to the scene that plays it:

   ```text
   scjump_scene_script_resource_ids[0x0] = asset "SCENE"
   ```

3. **Unregister it when it ends.** At the end of the content, clear the row and request the next scene, so the
   story continues:

   ```text
   scjump_scene_script_resource_ids[0x0] = 0x0
   system_flow_request = 0x1
   scjump_decision_out = 0x0
   ```

The complete working example is `oms/examples/mods/example.prologue`. It is a Himegari scene built on the skeleton of
`$1$SC1260`, played before the opening (decision 0, which is empty in the base game and falls back to SC0000). Saving
and loading inside it was tested in the port and in native AGE. `tools/drive/scenarios/himegari-mod-*.steps` drive
it.

**On Kamidori** the mechanism is the same: `scjump_scene_script_resource_ids` and `system_flow_request` exist in
`kamidori.globals`, and SYSTEM4 enters scenes through the registry. The details differ:

- decision 0 already maps to SC0000, so overwriting it *replaces* the opening instead of preceding it;
- no Kamidori scene mod has been tested yet (`../backlog.md`);
- the port cannot save in Kamidori yet, so test saving on native AGE.

Copy the skeleton from one of Kamidori's own append scenes.

## New assets

A file in `assets/` whose name the game does not have is a new asset. List it in `[assets].names`, and it becomes a
record of your catalog after the scripts. Use it wherever an instruction takes a resource id:

```text
adv_gfx_resource_id = asset "MYBG.AGF"
```

A file named like an original asset overrides that asset instead, and needs no listing ([packages.md](packages.md)).

## Using another mod's scripts

Declare the other mod in `[[requires]]`, then name its scripts and assets with its id:

```text
run "example.library:HELPER"
G[0x1234] = asset "example.library:ICON.AGF"
```

Only mods you require are visible, and a required mod always loads before yours.

## Keep records stable

Saves store scripts by packed id, which is selector plus record number. Once players have saves made inside your
scripts:

- do not reorder or remove `[scripts].names` entries; add new ones at the end;
- do not change the mod's `id`, because that makes it a different mod with a different selector.
