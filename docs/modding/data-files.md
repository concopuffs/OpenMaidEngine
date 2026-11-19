# Data files

A data file is a TOML file a mod ships under `data/`. It holds values, not code: the mod that defines its **kind**
reads it and decides what the values mean. A base mod defines a kind for its game's record tables, so a mod can
change a unit's stats or an item's price with a file like this one and no Lua at all:

```toml
[unit.12]                  # the record id is a placeholder; the game's guide has real ones
experience_reward = 999
```

Data files need format version 4 and run in the port only, as Lua does. The kinds each game's base mod accepts, with
their tables and fields, are in that game's guide (`games/<game>.md`).

## Words

- **Kind:** a named format for data files, such as `<game>.tables`. One mod declares it and gives it its meaning.
- **Owner:** the mod that declares a kind, with `[[accepts]]`.
- **Contributor:** a mod that ships a file of a kind, with `[[data]]`.
- **Document:** a data file's content, as the owner's handler reads it.

## Shipping a data file

```text
example.tougher-slimes/
├── manifest.toml
└── data/
    └── slimes.toml
```

```toml
omp = 4
id = "example.tougher-slimes"
name = "Tougher slimes"
version = "1.0.0"

[game]
profile = "<game>"
target = "extended"            # a package with data files targets extended

[[requires]]
id = "<game>-base"             # the kind's owner
version = ">=1.1, <2"

[[data]]
kind = "<game>.tables"         # a kind that a mod of the stack accepts
file = "slimes.toml"           # a path under data/
priority = 0                   # optional; where the file comes among the event's handlers
```

- **Format version 4** (`omp = 4`) and **`target = "extended"`**. `omp export` refuses the package, and a player
  profile in Compatibility mode refuses it ([packages.md](packages.md)).
- **Require the owner.** A file of a kind reaches only the mod that defines the kind, so the contributor requires it,
  as a mod that subscribes to an event requires the event's owner. An owner may ship files of its own kind without
  requiring itself.
- **`file`** is a path under `data/`, with forward slashes. It may name a file in a subfolder, it ends in `.toml`,
  and no part of it is empty, `.` or `..`.
- **Every file under `data/` is named by one entry.** A file that no entry names is an error, since it would do
  nothing without a word. A mod may ship several files, of one kind or of several.
- **No `[lua]` is needed.** The package may still hold Lua, patches, scripts and assets.
- **A package without `[game]` holds no `data/`.** A kind is one game's.
- **Bounds.** A package names at most 64 data files, and a file is at most 16 MiB.

## The file

- **TOML 1.0, in UTF-8.**
- **Values are strings, booleans, integers and floats.** An integer of more than 2^53 in size, a float that is not
  finite (`inf`, `nan`), and a date or a time are errors: the owner's Lua cannot hold them as they were written.
- **Keys are strings.** `[unit.761]` has the key `"761"`. What a key means is the kind's.
- **Bounds.** A document holds at most 400,000 values (a string, a boolean or a number; each element of an array
  counts), nests tables and arrays at most 16 deep, and has keys of at most 128 characters with no control
  character.

## Defining a kind

A mod with Lua and a `[game]` table can define a kind. A library cannot, as it declares no event.

```toml
[[accepts]]
kind = "example.lists"                 # named as an event is; one mod of a stack declares it
description = "Lists of names."

[[accepts.at]]                         # one or more
event = "example.lists.ready"          # an event the owner could subscribe to: its own, or a required mod's
handler = "lists.lua:apply"            # a file under lua/, then a top-level function of it
```

- **A kind is unique in a stack.** Two mods that declare one kind are a hard conflict.
- **A kind is part of its owner's API.** Adding to a format is a minor version of the owner; taking a field away,
  or changing what a value means, is a major one.
- **`shape`** (a description the link checks a file against) is not read yet. A manifest that gives one is refused
  until the engine checks shapes.
