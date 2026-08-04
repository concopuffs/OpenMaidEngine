# AGE engine reimplementation

Open reimplementation of Eushully's AGE engine with first-class modding. The first target is
*Himegari Dungeon Meister* (SYS4, 2009); the runtime is Godot 4.7 .NET plus a platform-neutral C# VM.

This repository contains only authored engine, tooling, native-boundary, test, and documentation files.
It does not contain the original game. Supply a legally obtained AGE installation separately; never copy
original game data into this repository.

The repository's authored work is available under the [MIT License](LICENSE). That grant does not cover
Eushully game files or override the separate terms recorded for third-party material.

## Start here

Prerequisites for the current Windows development workflow are Python 3.11, the .NET 8 SDK, Godot 4.7
.NET, and a Himegari install containing `SYS4INI.BIN`. Pass paths explicitly or configure
`AGE_GODOT_CONSOLE` and `AGE_GAME_ROOT`.

```powershell
# Verify and display the resolved development environment without launching.
.\run-godot.ps1 -Doctor -GodotConsole <godot-console.exe> -GameRoot <Himegari-install>

# Run source-only/core validation. The default validation level is the complete workspace/runtime suite.
py -3.11 -X utf8 tools\validate.py --level core

# Build and launch the natural SYSTEM4 → title → game route.
.\run-godot.ps1 -GodotConsole <godot-console.exe> -GameRoot <Himegari-install>
```

Linux and Windows release exports are asset-free build products. From a Linux host,
`./tools/build-linux-x64.sh` builds, packages, and smoke-tests the Linux runtime, while
`./tools/build-windows-x64.sh` cross-builds and structurally verifies the Windows runtime without Wine.
Both write archives under `build/package/`; see the tools reference for exact inputs and output contents.

`workspace` and `full` validation additionally require the extracted script corpus and disposable derived
inputs described by the tools reference. `runtime` and `full` require Godot plus a valid game root. A
selected requirement that is unavailable is an error, not a silent skip.

## Canonical references

- [Architecture and roadmap](docs/remake-architecture-and-roadmap.md) — direction, phases, and the current
  codebase-consolidation effort.
- [Project structure](docs/PROJECT-STRUCTURE.md) — source/original/derived ownership and directory layout.
- [Tools reference](docs/tools-reference.md) — exact commands, prerequisites, inputs, and outputs.
- [Phase B framework](docs/phase-b-framework.md) — boot, menu, session, and gameplay execution order.
- [Engine reverse engineering](docs/engine-re.md) — native AGE findings and provenance.
- [Third-party notices](THIRD_PARTY_NOTICES.md) — incorporated-code licenses and research acknowledgments.

Follow the repository's canonical-document map when recording new knowledge: extend the existing owner and
cross-link it instead of duplicating facts here.

## Generated data

`build/` is disposable and ignored. Generated references identify their canonical source in their header;
edit that source and run its owning generator rather than patching generated output. The original game
install and extracted archives remain sibling directories outside this Git repository.
