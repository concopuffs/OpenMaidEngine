# New scripts, scenes and assets

Patches change what is there ([patching.md](patching.md)). This page is about adding things: scripts of your own, a
boot-time data edit, a scene the player can save in, and new assets. Your game guide ([games/README.md](games/README.md))
has the game's values: how many catalogs it ships, what its scene registry holds, its saved range, and working
examples.

## Append catalogs

AGE games ship add-on content as *append catalogs*: `APPENDnn.AAI` (an index) and `APPENDnn.ALF` (the data). A mod
with new scripts or new assets gets a catalog of its own, built when the mods link:

- **Records.** The catalog's records are your `[scripts].names`, in order, then your `[assets].names`
  ([packages.md](packages.md)). Each script is `scripts/NAME.oms`, a unit that begins `script "NAME"`.
- **Selector.** Each catalog has a number, its *selector*. The port allocates the lowest one that neither the
  install's own catalogs nor another mod use, and keeps it in `mods/selectors.toml` for as long as the mod is
  installed, whatever the load order. Never rely on a particular selector.
- **Packed ids.** The engine addresses record `n` of catalog `s` as `(s << 24) | n`. Write `run NAME` and
  `asset "NAME"` and the linker fills these in, so a mod never hard-codes its own ids.

## Record 0 runs at boot

When the engine starts (INIT2, after the game's data tables are built), it runs record 0 of every mounted catalog,
in selector order. This is your mod's entry point. Record 0 is the first name in `[scripts].names`.

**Record 0 must be a short script that ends with `exit`.** It runs during boot, before the title screen. A record 0
that shows text or waits for input hangs native AGE at boot. `omp build`, `omp check` and the port's link report warn
when record 0 shows text or waits for the player itself (a `note` line that starts `warning: record 0 runs at
boot`). They do not follow a `run` from record 0 into another script. If your mod's real content is a scene, put a
small boot script first:

```toml
[scripts]
names = ["AUTO", "SCENE"]                # AUTO is record 0
```

A mod that ships new assets but no scripts gets an empty record 0 generated for it.

## Data edits at boot

Record 0 runs after the game's own init scripts fill its data tables, so a record-0 script can overwrite table
entries:

```text
oms 1
script "AUTO"
use <profile>.globals

    with unit[761] {
        .stats.max_hp = 150
        .experience_reward = 999
    }
    exit
```

The tables and their fields are listed in the game's global reference, linked from its identity block.

**Does the edit survive loading a save?** Saves keep only a prefix of each global bank. Boot runs on every start, so
a table cell above the saved range is rebuilt with your edit each time, and the edit holds after a load. A cell
inside the saved range is overwritten by the loaded save. Your game guide gives the range; check your cells against
it.

## Calling your scripts

From a patch or another of your scripts, `run NAME` calls a script and returns when it ends. A script with no ADV
page (a computation or a data edit) can always be called this way.

**A script you `run` from a scene cannot be a scene itself.** The scene that calls it owns the ADV menu and the
click handlers. If the player right-clicks during a text wait inside the called script, the engine sees a callback
that belongs to the caller while the callee runs. It stops with the error *"Depth が不正です"* (the engine's "invalid
depth" error). Native AGE and the port both do this. Short pages inserted with a patch are fine, because they run in
the scene's own frame. A run of pages in a separate script needs its own scene.

## A scene of your own

To add a scene the player can open the menu, save and load in, make it a full scene that the scene scheduler enters,
as it enters the shipped ones:

1. **Copy a shipped scene's skeleton.** Decompile a shipped append scene with `--level idiomatic` and keep
   everything except its content. That means the ADV handlers, hotspots, save-resume mark, load-restore code, and
   the per-pass setup and teardown. Then put your pages in the content subroutine.
2. **Register it.** Your record-0 script writes the scene's id into the SCJUMP scene registry, the table that maps
   a story decision to the scene that plays it:

   ```text
   scjump_scene_script_resource_ids[<decision>] = asset "SCENE"
   ```

3. **Unregister it when it ends.** At the end of the content, clear the row and request the next scene, so the
   story continues:

   ```text
   scjump_scene_script_resource_ids[<decision>] = 0x0
   system_flow_request = 0x1
   scjump_decision_out = 0x0
   ```

Which decision to use depends on what the game's registry already holds there. An empty row that falls back to
another scene lets yours play first, while a row that holds a scene is replaced. Your game guide says which games
this recipe has been tested on, and names the example scene to copy.

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
