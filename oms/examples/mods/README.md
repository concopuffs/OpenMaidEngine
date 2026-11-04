# Example mods (mod loader gate 2)

Three Himegari mods that patch `SC0000.BIN` (mod loader spec §13, gate 2; `Age.Engine.Tests/ModLinkerTests.cs`):

- `example.sc0000-page3` inserts a line before the third page (site 0x877, as the gate 7 fragment does);
- `example.sc0000-page4` inserts a line before the fourth page (site 0x8ac);
- `example.sc0000-merge` replaces the third and fourth pages (0x874 through 0x8de) with one line, so it overlaps
  both: linking it with either of the others is a hard conflict.

The first two compose. Each folder is an unpacked Open Maid Pack; `omp build` (plan M6) will pack them.
