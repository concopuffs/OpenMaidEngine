# Example mods (mod loader gate 2)

Three Himegari mods that patch `SC0000.BIN` (mod loader spec §13, gate 2; `Age.Engine.Tests/ModLinkerTests.cs`):

- `example.sc0000-page3` inserts a line before the third page (site 0x877, as the gate 7 fragment does);
- `example.sc0000-page4` inserts a line before the fourth page (site 0x8ac);
- `example.sc0000-merge` replaces the third and fourth pages (0x874 through 0x8de) with one line, so it overlaps
  both: linking it with either of the others is a hard conflict.

The first two compose.

- `example.prologue` (gate 4) ships an append catalog: record 0 (`AUTO`) registers record 1 (`SCENE`) as New Game's
  scene (SCJUMP decision 0, which falls back to SC0000 in the base game), and `SCENE` is a full ADV scene built on the
  skeleton of the shipped append scene `$1$SC1260`, so the ADV menu, saving and loading work inside it. When it ends
  it clears the row and the opening follows. A scene called with `run` from another scene cannot open the ADV menu:
  native AGE stops with "Depth が不正です" (`docs/engine-re.md`). `tools/drive/scenarios/himegari-mod-*.steps` drive it.

Each folder is an unpacked Open Maid Pack; `Age.Cli omp build DIR` packs one into a `.omp` with stored plans.
