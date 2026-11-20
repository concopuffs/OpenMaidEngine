# Example mods (mod loader gates 2, 4, 5 and 6)

Three Himegari mods that patch `SC0000.BIN` (mod loader spec §13, gate 2; `Ome.Engine.Tests/ModLinkerTests.cs`):

- `example.sc0000-page3` inserts a line before the third page (site 0x877, as the gate 7 fragment does);
- `example.sc0000-page4` inserts a line before the fourth page (site 0x8ac);
- `example.sc0000-merge` replaces the third and fourth pages (0x874 through 0x8de) with one line, so it overlaps
  both: linking it with either of the others is a hard conflict.

The first two compose.

- `example.prologue` (gate 4) ships an append catalog: record 0 (`AUTO`) registers record 1 (`SCENE`) as New Game's
  scene (SCJUMP decision 0, which falls back to SC0000 in the base game), and `SCENE` is a full ADV scene built on the
  skeleton of the shipped append scene `$1$SC1260`, so the ADV menu, saving and loading work inside it. When it ends
  it clears the row and the opening follows. A scene called with `run` from another scene cannot open the ADV menu:
  native AGE and the port both stop with "Depth が不正です" (`docs/engine-re.md`). `tools/drive/scenarios/himegari-mod-*.steps` drive it.

- `example.depth-error` raises AGE's interpreter error on purpose. SC0000 `run`s its DETOUR script, which waits
  for text while SC0000's ADV bindings are armed, so a right click there is "Depth が不正です".
  `tools/drive/scenarios/himegari-script-error*.steps` answer the box on both targets.

Gate 5 exported `example.prologue`, `example.sc0000-page3` and `example.sc0000-page4` with `Ome.Cli omp export` and
ran the same scenarios on native AGE.

- `example.kamidori-prologue-line` (gate 6) targets Kamidori with the translation overlay: it inserts a line before
  the first page of the translated `patch/SC0000.bin` (site 0xd0a, fingerprinted on the translated bytes).
  `NaturalBootIntegrationTests.KamidoriTranslatedPrologueFragmentIsTheFirstAdvPage` boots a new game into it.

- `example.arterial-prologue-line` is Arterial's first mod (`docs/modding/games/arterial.md`): a line before the
  first page of its opening scene (site 0xf1c, the instruction that sets `G[0x3141]`), through `arterial.adv`.
  `NaturalBootIntegrationTests.ArterialPrologueFragmentIsTheFirstAdvPage` boots a new game into it, and the
  exported `SC0000.BIN` shows the line on native AGE.

- `example.double-exp-lua` (Lua scripting gate 4) is the Lua version of the Himegari guide's experience patch: a
  handler on `himegari.experience.after_calculation` doubles `L[0x1]`. `ModLuaLinkTests` links it and runs
  `ADDEXP.BIN` with it and with the guide's OMS patch, and both leave the same state. It targets `extended`, so it
  runs in the port only.
- `example.double-exp-event` (Lua scripting §9.4, gate 5) is the same mod as a subscriber of `himegari-base`'s
  `himegari.experience.award`: it names no address, script or fingerprint and has no `vm`. `ModLuaLinkTests` runs it
  on `ADDEXP.BIN` and compares it with the guide's OMS patch too.
- `example.kamidori-double-exp-event` (Lua scripting gate 13) is the same subscriber on Kamidori, of `kamidori-base`'s
  `kamidori.experience.award`. `ModLuaLinkTests` runs it on both Kamidori installs' `ADDEXP.BIN` and compares it
  with the Kamidori guide's OMS patch.
- `example.lua-random` (Lua scripting gate 11) rolls `math.random` when `SC0000.BIN` is entered, in a `pairs` loop
  and with `math.random()`, and keeps the rolls in `maid.state`; `describe()` gives them to the driver's `lua` step.
  `tools/drive/scenarios/port/himegari-lua-random.steps` runs a new game under `rand-seed 7` and fails unless the
  rolls are the ones every seeded run gives. It also serves to try `--lua-reload` on a copy of the folder.
- `example.random-enemies` (Lua scripting plan §11) is a tech demo of `himegari-base`'s stage events: at
  `himegari.stage.loaded` it replaces each ordinary enemy of the stage with one drawn from every ordinary enemy of the
  game, bosses and named characters kept. It draws a seed at a playthrough's first stage and keeps it in `maid.state`,
  so the playthrough's saves keep its draws and a new game draws another. The pick is a function of the seed, the
  stage and the slot, so the loaders' later runs in a stage get the same answer.
  `tools/drive/scenarios/port/himegari-random-enemies.steps` plays a new game to stage 1 under `rand-seed 7` and fails
  unless the draw is the one every seeded run gives.

- `example.stage-enemies` (data patches gate 8) is a data file and nothing else: `himegari-base`'s
  `himegari.tables` applies its stage parts at `himegari.stage.loaded`. Stage 1's slot 2 gets ハルピュア (810) in
  place of 緑プテテット (200), and stage 2's slot 3 サーペント (834), so at stage 1 slot 3 keeps the stage's own
  unit. `example.stage-enemies-mistake` is the same file with a unit's name the save's code page cannot encode, on
  purpose (`é` in unit 810's `display_name`). No shape can say that, so the link takes the file, which matches the
  kind's shape; the boot's notice and `diagnostics/lua.log` name the file and the path, and nothing of it is applied,
  its stage part included. A mistake a shape can say, such as a misspelt field, fails the link instead
  (`omp check`; [docs/modding/data-files.md](../../../docs/modding/data-files.md), "Shapes"). The port
  scenarios `tools/drive/scenarios/port/himegari-stage-enemies.steps` (a new game to stage 1, the camp and back, a
  field save and its load), `himegari-stage-enemies-load.steps` and `-load-without.steps` (that save loaded from the
  title with the mod and without it) and `himegari-stage-enemies-mistake.steps` drive them; their headers give the
  commands.
- `example.kamidori-stage-enemies` (data patches gate 8) does the same on Kamidori with `kamidori.tables`: slot 1 of
  ◆開けた涼風西湖畔◆ (stage 51), which its spawner places at the floor's start, gets 緑プテテット (761) in place of
  ハルピュア (331), and slot 3 of 蒼月の南湖畔 (stage 52) 赤プテテット (762), so stage 51's slot 3 keeps its own 水精
  (402). Its part is applied again where INFO's third tab, アイテム情報, runs the floor's loaders again for its
  獲得地点 view, and where the stage list runs a stage's loaders for its preview.
  `example.kamidori-stage-enemies-mistake` gives unit 761 a `display_name` with `é` on purpose, which the base mod
  refuses at the boot, as the Himegari twin does. The port scenarios
  `tools/drive/scenarios/port/kamidori-stage-enemies.steps` (INFO off a floor, the floor entered afresh, INFO on it,
  a floor save and its load), `kamidori-stage-enemies-list.steps` (the stage list's preview),
  `kamidori-stage-enemies-lake.steps` (saves on 蒼月の南湖畔 loaded with the mod),
  `kamidori-stage-enemies-load-without.steps` (the floor save loaded from the title without the mod) and
  `kamidori-stage-enemies-mistake.steps` drive them. They start from saves in the clean install's story: one in ウィル's
  workshop when シセティカ湖 has just opened, before the first visit to 蒼月の南湖畔, and two on 蒼月の南湖畔 (turns 1
  and 5), at the slots each header names. The repository does not ship them.
- `example.unit-stats` and `example.kamidori-unit-stats` (the data patches plan's derived values, §5.3) each raise
  one unit's maximum HP, which the game copies into the unit's saved stats when it joins (each game's guide page,
  "A unit keeps a copy of its definition" and "A character keeps a copy of its definition").
  `tools/drive/scenarios/port/himegari-unit-stats.steps` starts a new game with the Himegari one, where リリィ (unit
  2) joins and her saved stats take the edit, and saves; `himegari-unit-stats-load-without.steps` loads that save
  without the mod, where her definition is the game's own and her saved stats keep the edit.
  `kamidori-unit-stats.steps` loads the Kamidori workshop save (above) with the Kamidori one: ウィル (unit 1)
  joined long before, so his definition holds the edit and his saved stats the save's own.

- `example.kamidori-title-hover` makes Kamidori's title draw the hovered button in the same tick that finds it. The
  title's 50 ms mouse callback (`TITLE@0x182`) draws the highlight first and reads the cursor second, so a hover
  shows one tick late, 50 to 100 ms after the cursor arrives, on native AGE and the port alike. Himegari's title and
  every other Kamidori menu read the cursor first. The fragment swaps the two calls and skips the draw on a tick
  that started an action, which draws its own. Measured on native AGE on 2026-11-15, the hover showed within
  50 ms (the original took up to about 100 ms), and Load Data and Option still opened and returned. It targets the untranslated
  `TITLE.BIN`; the translated one has the same code at the same offsets, and the fragment names its fingerprint.

Each folder is an unpacked Open Maid Pack; `Ome.Cli omp build DIR` packs one into a `.omp` with stored plans.
