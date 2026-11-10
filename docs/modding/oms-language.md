# Open Maid Script for modders

Open Maid Script (OMS) is the text language for AGE bytecode. Everything you write in a mod, whether a patch or a new
script, is OMS that compiles to the same instructions the original games use. This page covers what you need to
write mods. The full grammar and the exact lowering rules are in the language design
(`../superpowers/specs/2026-10-06-open-maid-script-design.md`).

The best way to learn the language is to decompile a script you want to change and read it:

```text
Ome.Cli oms-decompile --script SC0000.BIN --level idiomatic --output build/SC0000.oms <game options>
```

Use the game options from your game guide ([games/README.md](games/README.md)). `--level idiomatic` names globals, scripts, library subroutines and dialogue lines. Without `--output` the source
goes to the console. Use `--level raw` to see one instruction per line with its exact operand forms.

## Files and units

Every file starts with `oms 1` (the language version), then one header that says what kind of unit it is:

| Header | Unit | Used for |
|---|---|---|
| `patch "SC0000.BIN" fingerprint "sha256:…"` | fragment | a patch in `patches/` ([patching.md](patching.md)) |
| `script "NAME"` | script | a new script in `scripts/` ([new-scenes.md](new-scenes.md)) |
| `module name.path` | module | declarations and library code, with no code of its own |

Source is UTF-8. `//` and `/* … */` are comments. A newline ends a statement, unless the line ends inside brackets or
with an operator. `;` also separates statements.

## Modules: `use`

`use` imports names. Each game has these modules, where `<profile>` is its profile id (your game's identity block in
[games/README.md](games/README.md) lists them):

- **`<profile>.globals`:** every named global variable and table, from the game's global registry. The game's
  generated global reference lists them.
- **`<profile>.scripts`:** every catalog script by its file stem, for `run`. A stem that is not an identifier is
  quoted: `run RECOVER`, `run "$1$SC2600"`.
- **`<profile>.adv`:** the ADV prelude, which holds the scene library, the dialogue line template and the speaker
  names. Its source is `oms/<profile>/adv.oms`, and it is worth reading. A game without a prelude yet has no
  dialogue statements; its identity block says so.

`<profile>.adv` itself uses the other two. Most fragments that touch a scene start with:

```text
use <profile>.globals
use <profile>.adv
```

## Storage

AGE has no variables in the usual sense. It has numbered cells in banks, which the decompiler prints like this:

| Written | Bank |
|---|---|
| `G[0x1234]` | global integers |
| `GS[0x315]` | global strings |
| `GF[0x10]` | global floats |
| `L[0x5]`, `LS[…]`, `LF[…]` | the current script frame's locals |

A registry name such as `current_unit_id` is just a name for one of these cells.

**Declaring your own names.**

```text
const BONUS = 50                         // replaced by its value wherever it appears
global int my_flag @ 0x1234              // a name for a fixed global cell
global int my_table[10] @ 0x2000         // a fixed array
local int i                              // a frame local; the compiler picks a free slot
local int exp @ 0x1                      // a name for an existing local slot (useful in patches)
enum mood { calm = 0, angry = 1 }        // named constants
```

A declaration names its cell or value for the whole script, wherever you write it: a `local` declared inside an `if`
or a loop is the same slot everywhere. A template body cannot hold a declaration, since a template may expand many
times; declare the name at the top level. A label or sub cannot share its name with a registry global or with a name
a used module declares.

A `global` always needs its cell (`@ address`). The engine has no allocator for mod-owned globals yet, so you choose
a cell no shipped script uses. Your game guide names the cells its examples use, and the range its saves keep.
`../backlog.md` tracks finding free cells safely.

The types are `int`, `float` and `str`, and arrays of them (`int[n]`, `int[r][c]`). A 2-D array declared with named
columns reads by column: `unit_stats[i].max_fs`.

**Indexing undeclared cells.** `G[0x3306][i]` is element `i` of the array that starts at `G[0x3306]`.
`G[0x341ab:0x35][row][col]` is a 2-D element with row width `0x35`.

## Tables and `with`

The games store data records as parallel arrays: one array for every unit's level, one for its max HP, and so on.
The globals module groups those arrays into **tables**, such as `unit`, `item` and `skill` (your game's identity
block lists its tables), so a field reads as one name:

```text
unit[761].stats.max_hp = 150
```

A `with` block writes several fields of one record:

```text
with unit[761] {
    .stats.max_hp = 150
    .experience_reward = 999
    .drop_chance.drop_1 = 100
}
```

A record is never a value. You cannot copy one record to another, or assign a whole record at once. The table
layouts and what each field means are in the game's global reference, linked from its identity block. Record keys
are numbers for now; naming them (`unit[<unit name>]`) is in `../backlog.md`.

## Expressions

- Arithmetic and bitwise: `+ - * / % & | ^ << >>`; comparisons: `== != < <= > >=`; logic: `! && ||`; unary `-`
  and `~`. Integer arithmetic wraps at 32 bits, and comparisons are signed, as in the engine.
- `^` compiles to `(a | b) - (a & b)`, which computes both operands twice, so the code doubles with each nested
  `^`. Nesting deeper than 8 is an error; assign part of the expression to a `local` first.
- Integers in decimal, `0x` hex or `0b` binary; floats like `1.5`; strings in `"…"` with `\"`, `\\`, `\n`, `\t`,
  `\uXXXX`. An integer must fit in 32 bits: up to `0xffffffff`, and down to `-2147483648`.
- Blocks, parentheses, unary operators and the operators of one chain nest at most 200 deep. An `else if` chain does
  not count.
- **`&&` and `||` always evaluate both sides.** The VM has no short-circuit. To guard the second operand (against
  dividing by zero, for example), nest two `if`s.
- Expressions have no side effects, because no call returns a value inside an expression.

## Statements

```text
x = expr            x += expr           (and -=, *=, and so on)
if c { … } else if c2 { … } else { … }
while c { … }       loop { … }          do { … } while c
for init; c; step { … }
switch x { case 0: … case 1, 2: … default: … }
break               continue
name:               goto name           if c goto name
atomic { … }        // a statement block the engine runs without presenting a frame in between
name(args)          // a subroutine, template or intrinsic call
run NAME(args)      // call another script
return              // return from a subroutine
exit                // end the script
note "text"         // a comment compiled into the bytecode (the engine ignores it)
```

**Subroutines** are `sub name { … }`. A sub sits where you write it, and control may not fall into it, so put subs
after the code's `exit`. A sub can bind parameters to cells, and the call then assigns them before jumping:

```text
sub award(amount: L[0x10]) { … }
award(5)                                 // L[0x10] = 5, then call
```

A parameter binds to one cell: a raw cell (`L[…]`, `G[…]`) or the name of a declared or registry scalar, such as `sub f(n: current_unit_id)`. In a patch, `L[…]` is a
slot of the base script's frame, so choose one the base does not use.

**Templates** are `inline name(params) { … }`. They expand in place wherever they are used. The dialogue line
template below is one. A `cell` parameter can be indexed: in `inline clear(a: cell) { a[i] = 0 }`, `clear(my_table)`
writes `my_table[i]`, and `clear(G[0x2000])` writes `G[0x2000][i]`.

## Intrinsics

Every engine instruction is callable as a function. Its name comes from the opcode registry:

```text
set_font_size(0x18)
draw_texture(0xcf08, 0x3, 0x0, 0x0, 0x320, 0x258, 0x0, 0x0)
GS[0x1400] = set_string("text")          // an intrinsic whose first operand is its output
get_cursor_virtual(out x, out y)         // outputs elsewhere are passed with out
```

Every opcode is also callable as `op_0x<hex>(…)`. The compiler checks each argument against the opcode's operand
contract: an operand the engine writes must be a cell, and a code operand must be a sub or label. The full list,
with what each operand means, is `../opcode-reference.md`. Under the `native` target only opcodes the original engine
dispatches are allowed.

`@op(0x249, 0x2ae2, L[4])` emits one raw instruction with explicitly written operands. You rarely need it; the
decompiler prints it at `--level raw`.

## Dialogue lines

Inside a scene, a line on its own is a dialogue statement. The `<profile>.adv` prelude expands it into the same
instructions as the game's own pages: voice reset, nameplate, voice, text, wait for a click, end of page.

A line is an optional speaker, a string, and optional named arguments:

```text
"[MOD] A narration line."                  // narration
speaker_name "[MOD] Spoken text."          // a speaker
speaker_name "[MOD] Spoken text." (voice: 0x34e)
```

The prelude marks one template `@dialogue`. The string binds to its `text` parameter, the speaker to `speaker`, and
named arguments to the parameters of the same name. A game's template can take more arguments, such as a nameplate
form. Your game guide shows its template and real examples.

**Speakers** are the names in the prelude's `enum speaker` (`oms/<profile>/adv.oms`). They are the game's own
character names, which may not be English. Names that repeat, or that are not identifiers (such as `？？？？`), carry
their id instead: `speaker_0x10`. Copy a speaker name from the decompiled scene you are editing, or look up the id
in the enum. **Voice ids** come from the decompiled scene as well: a voiced line prints its
`voice:`.

Text may be in any language, provided every character is in CP932, the games' text encoding. A character outside CP932 is a
compile error that names it. Pages that interpolate a string cell, carry furigana, or span rows in an unusual way
decompile as plain statements rather than dialogue lines. Copy those as they are.

## Calling scripts and naming resources

- `run SC0010` calls a catalog script; it returns when that script ends.
- `run SCENE` calls a script of your own mod by its `[scripts].names` entry.
- `run "other.mod:NAME"` calls a script of a mod you declare in `[[requires]]`.
- `asset "NAME"` is an expression: the packed resource id of a new asset of your mod, or of one of your scripts.
  Use it wherever an instruction takes a resource id, for example to register a scene
  (`scjump_scene_script_resource_ids[0x0] = asset "SCENE"`). Original assets keep their numeric ids.

Your mod's selector is chosen when the mods link (`new-scenes.md`), so never write a packed id of your own scripts
as a number. `run` and `asset` stay symbolic until the link.

## Errors

The compiler reports `file:line:col` and a message. The common ones:

- an operand that must be a cell, a sub or a constant is something else (the contract check);
- a name that does not resolve (check the `use` lines);
- a character not in CP932;
- `goto` into or out of an `atomic` block;
- an opcode the target game's engine does not dispatch.

[troubleshooting.md](troubleshooting.md) covers errors that appear only when mods link.
