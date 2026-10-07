# Open Maid Script — language and toolchain design

**Date:** 2026-10-06
**Status:** draft for review. No compiler code has been written against it.
**Home in the canonical map:** this file is the language design. Direction and phase ordering stay in
`docs/remake-architecture-and-roadmap.md` §3.3 ("Decided language architecture") and §3.10. Bytecode facts stay
in `docs/sys4-format-notes.md` ("Compiler-emitted structure"). Native semantics stay in `docs/engine-re.md`
("VM operand accessors", "Statement blocks", "VM value, array and call semantics"). Opcode contracts stay in
`vm-map/opcodes.toml`. This spec cites those homes and does not restate their evidence.

## 1. Purpose and principles

Open Maid Script (OMS) is a scripting language that compiles to AGE bytecode and decompiles back from it. It must
serve three uses without changing its grammar later:

1. **Authoring:** writing new scenes and logic that run under Open Maid Engine and, where possible, under the
   native `AGE.EXE`.
2. **Editing:** decompiling original scripts, changing them and recompiling.
3. **Patching:** compiling fragments that attach to a pristine original script without replacing it.

The design follows the four principles decided in roadmap §3.3.

- **P1 — The semantics are the VM's semantics.**
  - The core language describes exactly the AGE VM: storage banks, references, three-address arithmetic,
    structured flow, local subroutines, cross-script calls, code references, statement blocks, inline data and
    comments.
  - Everything the VM can express is expressible, and nothing else is.
  - The grammar defined here is the whole grammar.
- **P2 — Growth is data or library, never grammar.** New capability arrives only through four channels:
  - intrinsics generated per engine ABI from `vm-map/opcodes.toml`;
  - symbols generated from the global registries and script catalogs;
  - libraries written in OMS itself (preludes);
  - targets (`native`, `extended`).
- **P3 — One language, two directions.**
  - Every construct has one canonical lowering, chosen to match Eushully's own compiler.
  - Library templates compiled with placeholder parameters become the decompiler's patterns, so authoring a
    construct also teaches the decompiler to recover it.
  - The source carries all meaning. The layout sidecar only restores the original bytes and original site
    identity. Compiling without it must behave identically, at every level. A layout-dependent value that the
    decompiler prints as a plain number is a decompiler bug (§9.5).
- **P4 — Fragments are first-class.** Compiling against an existing script's environment is a core mode, not an
  afterthought.

Non-goals:

- host-side event handlers (Lua, roadmap §3.8);
- heap data structures, closures or value-returning functions (the VM has none);
- translation and text replacement, which belong to declarative text tables;
- recovering Eushully's original source names or spelling.

## 2. Execution model (what the language describes)

This section states the semantic model; the citations hold the evidence.

**Storage.** Storage is organized in typed banks:

- **Global banks:** integer, float, string, integer reference and string reference cells. Their dimensions are
  declared in the SYS4INI file header (`sys4-format-notes.md`, "SYS4INI file header").
- **Saved prefix:** each global bank has a saved prefix. Cells below it persist in numbered saves; cells above
  it are rebuilt at boot.
- **Local banks:** each script frame has six local banks (integer, float, string, integer reference, float
  reference and string reference). Local subroutines share their script's frame.

**Values.**

- Arithmetic is int32 and wraps.
- `div`/`mod` truncate toward zero with no zero guard.
- Comparisons are signed and yield 0 or 1.
- Shifts use `n & 31`.
- Generic reads truncate floats and parse strings with `atol`.
- Reading a number as a string yields full-width CP932 digits.
- SYS4433 adds float-typed operand positions.

`engine-re.md`, "VM value, array and call semantics", holds the evidence.

**References.** A reference variable is bound to one storage cell:

- `lookup-array` and `lookup-array-2d` bind `base[i]` and `base[row*stride+col]`;
- `take-address` aliases a variable or another reference's target;
- reads and writes through a reference reach the bound cell;
- no bounds are checked.

**Control.**

- `jcc` has a true and a false target, where `-1` means fall through.
- `jmp`, `call`/`ret` (a 256-entry per-frame return stack), `call-script`, `exit`, and value switches
  (`0xa1`–`0xa3`).
- Callbacks are local subroutines that the engine enters by pushing the waiting PC.
- Coroutine handlers end at `0x7c` instead of `ret`.
- Code offsets are also ordinary integer values:
  - `call` accepts a variable operand: `call G[0x6be]` calls a stored offset (144 Himegari, 269 Kamidori sites);
  - scene prologues store their exit label as a plain immediate (`mov G[0x6c3] 0x45e`) and compare it with the
    host label iterator's result (op `0x140`).

**Statement blocks.** `0x1f4`/`0x1f5` make one command atomic for presentation, delayed starts and queued
launches. No control transfer crosses a block boundary.

**Operand contracts.** Each opcode operand has a native access kind recorded in `opcodes.toml`: read,
read-float, read-string, write, write-string, write-float, bind, address, string-cell, ref-slot, cell or ignored.
These kinds are the type system's ground truth.

## 3. Compilation units, modules and targets

### 3.1 Units

| Unit | Source | Output |
|---|---|---|
| Script | one `.oms` file beginning `script <name>` | one SYS4 script (`.BIN`) plus an optional layout sidecar |
| Module | one `.oms` file beginning `module <path>` | no bytecode of its own: declarations, templates, library subroutines |
| Fragment | one `.oms` file beginning `patch <script>` | a transform plan against a fingerprinted base script (§10) |

### 3.2 Modules and generated symbols

A unit imports modules with `use`. Three kinds of module exist:

- **Generated ABI modules** (`abi.sys4422`, `abi.sys4433`): one intrinsic per opcode installed in that ABI, with
  operand contracts. They are generated from `opcodes.toml`, never written by hand.
- **Generated game modules** (`himegari.globals`, `himegari.scripts`, and their Kamidori counterparts): every
  named global from `vm-map/globals[-<profile>].toml`, with type, shape and columns, plus every catalog script.
  Also generated.
  - `<profile>.globals` is read from the registry view's bank-keyed `cells` index. A `string…` type is a
    string-bank cell; an open leading dimension (`int[][27]`) is an array of unknown length. Unnamed entries and
    entries with an unknown type are left out.
  - `<profile>.scripts` maps each catalog script's file stem to its packed id: `run RECOVER`, or
    `run "$1$SC2600"` when the stem is not an identifier.
- **Prelude and library modules** (`himegari.adv`, `kamidori.adv`, shared libraries): hand-written OMS shipped
  in the per-game base mod (roadmap §3.8). Their source lives in `oms/<profile>/<name>.oms`, and the tools load
  every module there.
  - A source module may hold `use`, `const`, `enum`, pinned `global` declarations, templates (`inline`) and
    library subroutines (`sub`, `@guard sub`, `coroutine`). It has no code of its own.
  - Every name it declares is visible to the units that use it, and a unit may not redeclare one. That includes
    each library sub's name and every label inside its body: a library sub may have several entry points, as the
    original scene library does.

### 3.3 Targets

A compilation target is the triple `(profile, ABI, mode)`. The profile supplies:

- the catalog and the bank dimensions (from the SYS4INI header);
- the global registry and the prelude;
- the ABI.

| Mode | Opcodes allowed | New scripts | New globals |
|---|---|---|---|
| `native` | opcodes installed in the target ABI's dispatch table that have a registry access contract | an append catalog generated with a mod selector (§9.3) | linker-allocated free cells; persistent ones must sit inside the saved prefix (§9.2) |
| `extended` | the above plus engine extension opcodes (§9.4) | virtual script identities owned by the engine | engine-owned, namespaced mod state |

An opcode installed in the ABI but never observed in the game's corpus is allowed, with a warning. A construct
that needs `extended` is a compile error under `native`.

## 4. Lexical structure

- **Encoding.** Source is UTF-8. Every string literal must be encodable in CP932; anything else is a compile
  error that names the character.
- **Comments.** `// …` and `/* … */` are source comments and emit nothing. `note "…"` is a statement that emits
  op `0x1a7`, the compiled comment. The decompiler renders every `0x1a7` as `note`, so Eushully's own
  annotations survive.
- **Identifiers.** Unicode letters, digits and `_`; they may not start with a digit. Keywords are reserved.
- **Literals.**
  - integers: decimal, `0x` hex or `0b` binary, with an optional leading `-`;
  - floats such as `1.5`;
  - strings in `"…"` with `\"`, `\\`, `\n`, `\t` and `\uXXXX` escapes;
  - integer arrays `[a, b, …]`, allowed only where an inline array is legal.
- **Statement termination.** A newline ends a statement unless the line ends inside brackets or with a binary
  operator. `;` may also separate statements.

## 5. Declarations and types

### 5.1 Types

| Type | Meaning | Storage forms |
|---|---|---|
| `int` | int32 | global/local integer cell, immediate |
| `float` | IEEE single | global/local float cell, float immediate |
| `str` | CP932 string | global/local string cell, inline literal |
| `ref int`, `ref float`, `ref str` | reference variable | global/local pointer cells (types 6, 0xc, 0xd, 8, 0xe) |
| `int[n]`, `int[r][c]`, `str[n]` | fixed arrays | contiguous cells; used through element access or as an `address` operand |

There are no user-defined record types. Generated game modules may expose row tables as arrays with named
columns, written `units[id].level`. This lowers to `lookup-array-2d` with the registry's stride and column.

### 5.2 Declarations

```
const MAX_LEVEL = 99                       // substituted textually; never storage
global int current_unit_id @ 0x66715       // bind a name to a fixed cell
global int unit_level[445] @ 0x6930        // fixed array
saved global int my_counter                // linker-allocated, must persist (native: inside the saved prefix)
global int scratch                         // linker-allocated, need not persist
local int i                                // frame slot chosen by the canonical allocator
local int buf[8]                           // local array
local int pinned @ 0x1                     // explicit slot, used by decompiled code in preserve mode
enum speaker { エミリオ = 0x1, 魔王 = 0x11 } // named constants a template parameter can take as its kind
```

**Constants are substituted.** `const` values are substituted and never folded (§7.4), because the original
compiler performs no constant folding.

**Shapes and slots.**

- Arrays may leave their leading dimension open: `global int grid[][27] @ 0xb8d86`.
- A row table names its columns in a block: `global int unit_stats[100][14] @ 0x6f70 { hp: 0, max_fs: 13 }`.
  Then `unit_stats[i].max_fs` is `lookup-array-2d` with stride 14 and column 13, and `unit_stats[i][2]` reaches
  an unnamed column.
- A local with `@` is pinned to its slot. A local without `@` takes the next slots above every local the source
  names (including pinned ones), and the temporary pool starts above those.
- Declared local arrays also count in the header's local bank sizes, because their upper elements are reached only
  by computed index. The decompiler therefore declares every index-addressed local array, for example
  `local int array_17[0x6978] @ 0x17`. Each array runs to the next named cell of its bank, and the topmost runs to
  where the original frame put the temporary pool or the spare slot. Without these declarations a sidecar-free
  rebuild would put temporaries over array tails (33 such scripts across both games).
- Names resolve in the order: sub parameters, `const`, declared or imported names, then labels.
- An `enum` declares its members as constants. Its name can be a template parameter's kind (§6.3); the decompiler
  then prints a lifted argument by member name. Members are separated by commas or newlines.

**Decompiled names.** Decompiled code names unnamed storage `G[0x1234]`, `GS[0x315]`, `L[0x5]` and so on, and
names registry globals by their registry names. An element is named only when the declared shape lowers to the
same lookup: `name[i]` for a one-dimensional array, `name[r][c]` or `name[r].column` when the lookup's immediate
stride is the declared row width. Any other view of a named array keeps the raw base, such as
`G[0x6f70:0x7][r][c]`.

## 6. Statements and expressions

### 6.1 Expressions

- **Arithmetic and bitwise:** `+ - * / % & | << >>` (arithmetic right shift).
- **Comparisons:** `== != < <= > >=`.
- **Logical:** `! && ||`.
- **Unary:** `-` and `~`.
- **Operands:** element access `a[i]` and `a[r][c]`, column access `t[i].col`, and intrinsic calls with one
  output (§6.3).
- **Raw element access.** An undeclared cell is indexed directly: `G[0x3306][i]` binds an element of the array that
  starts at `G[0x3306]`. Two-dimensional access names the row stride inside the cell's brackets:
  `G[0x341ab:0x35][row][col]` is element `row * 0x35 + col` (op `0x12c`). The base bank chooses the reference
  type: integer cells bind `LP`, string cells `LSP`, float cells `LFP`.

Three rules govern evaluation:

- **No side effects.** Expressions have none, because the VM has no value-returning calls. Evaluation order is
  therefore unobservable.
- **`&&`/`||` evaluate both sides.** They produce 0/1 from `and`/`or` of normalized booleans, which is what the
  original code does. Comparisons and logical operators are already 0/1; any other operand is normalized with
  `!= 0` first. Use nested `if`s when the second operand must be guarded, for example against division by zero.
- **Synthesized operators.** `~`, `^`, unary `-` and `!` have no native opcode. They are synthesized
  (`sub 0 x`, `eq x 0`, and so on) and documented as such.

### 6.2 Statements

```
x = expr            x += expr   (and the other compound forms)
if c { … } else if c2 { … } else { … }
while c { … }
loop { … }                          // unconditional loop closed by a backward jmp
do { … } while c
for init; c; step { … }
switch x { case 0: … case 1, 2: … default: … }
break   continue
label:              goto label
if c goto label                     // the original language's conditional goto: one jcc c, L, -1
if !c goto label                    // the negated form: jcc c, -1, L (also break / continue)
if c goto a else goto b             // jcc c, a, b
atomic { … }                        // statement block 0x1f4/0x1f5
name(args)                          // local subroutine, template or intrinsic call
run SCRIPTNAME(args)                // cross-script call (call-script)
return                              // ret
exit                                // exit
note "text"                         // 0x1a7 (any single operand; the engine ignores it)
@op(0x249, 0x2ae2, L[4], L[0x397])  // raw instruction escape with explicit operand forms
```

**Conditional gotos.** `if c goto L` and `if c { goto L }` are distinct statements with distinct lowerings. The
corpus uses both.

**Atomic blocks.** `atomic` blocks are lexical. A transfer out of or into one is a compile error. So is `return`
or `exit` inside one, except in decompiled raw code.

**Raw instructions.** In `@op` the operand forms are written explicitly:

- literals, `float 1.5` and `"text"`;
- `G[a]`, `GF[a]`, `GS[a]`, `GP[a]`, `GSP[a]` for global cells;
- `L[a]`, `LF[a]`, `LS[a]`, `LP[a]`, `LFP[a]`, `LSP[a]` for local cells;
- `type(0x800b, v)` for any other type tag, wrapping an integer, label or data operand;
- `data(0x…, …)` for a referenced data item with no string or array shape, stored exactly.

The escape represents every instruction the VM accepts, including unknown opcodes.

### 6.3 Calls and parameters

**Intrinsics.** Intrinsic arguments are positional and checked against the operand access contracts (§8.1).
Operands the intrinsic writes are passed as lvalues with `out`: `get_cursor_virtual(out x, out y)`. When an
intrinsic has exactly one write-class operand and it is operand 1, `x = f(args)` is accepted sugar.

**Local subroutines** (`sub name { … }`) are placed blocks. A sub is emitted where it is declared, with no hidden
jump around it, which matches the original compiler: most bodies follow the main code's `exit`, and some sit
mid-code behind an explicit jump (`docs/sys4-format-notes.md`, "Where subroutine bodies sit"). The block rules
are:

- control may not fall into a sub: the code before it must end in `goto`, `return`, `exit` or `resume`, with no
  reachable label at the sub's start (a compile error otherwise);
- a sub that can fall off its end gets an implicit `return`;
- `break` and `continue` cannot leave a sub.

Subs have no native parameters. OMS supports *bound parameters*, which reproduce the original idiom of assigning
agreed cells before a call:

```
sub adv_load_layer(layer: adv_gfx_layer_index, resource: adv_gfx_resource_id) { … }
adv_load_layer(1, 0x23)        // lowers to: mov layer 1; mov resource 0x23; call adv_load_layer
```

A parameter is bound to a declared storage cell, global or local, so recursion overwrites it. Inside the body the
parameter name stands for its cell. Each argument is assigned to its cell as an ordinary assignment, so
`f(a + b)` computes straight into the cell. The compiler warns when a recursive call passes bound parameters. A
result can be bound the same way: `sub f(x: a) -> result_cell`, and `y = f(3)` then adds a copy of `result_cell`
into `y` after the call.

**Cross-script calls.** `run` uses the same binding. A game module declares
`extern script ADDEXP(unit: current_unit_id)`, and `run ADDEXP(5)` assigns the parameter and emits `call-script`.

**Code references.** Subroutine names are code references wherever an operand's contract is a code target:
`register_hotspot_callbacks(x, y, w, h, on_enter, on_exit, on_click)`. Their kinds are checked:

- operands that take callbacks require a `sub`;
- operands that take coroutine handlers require a `coroutine name { … }` body, which must end in `resume`
  (op `0x7c`).

The kind of each code-target operand comes from the registry (`code_target_kind` in `vm-map/opcodes.toml`).
Passing a coroutine where a callback is expected, or the reverse, is an error. A plain label is accepted anywhere,
because decompiled code keeps labels for entries that cannot be blocks (shared or fallen-into bodies).

**Code addresses as values.** `&name`, where `name` is a label, `sub` or coroutine, is an `int` expression holding
that code offset. The encoder relocates it like any code target. `call *expr` is an indirect local call through an
integer value. Decompiled code prints a code position held as data as `&label` wherever the decoder proves it is one
(§9.5).

**Templates** (`inline name(params) { … }`) expand in place at each use, written like a call: `twice(2, L[0])`.

- **Parameter kinds:**
  - `const`: an integer constant;
  - an enum name: a constant of that enum;
  - `text`: a string literal;
  - `expr`: any expression;
  - `cell`: an assignable cell or element.
- **Arguments:** a parameter may have a default (`voice: const = none`). Arguments bind by position or by name
  (`line("…", voice: 0x34e)`).
- **`static if`:** `static if c { … } else { … }` picks a branch at expansion from constant parameters, integers
  and `none`, the absent value of an optional constant.
- **Hygiene:**
  - labels a template defines are renamed per expansion;
  - `break` and `continue` cannot leave the expansion;
  - a template that expands itself is an error.

**Library subroutines** are the `sub`s of a used source module. The compiler links the ones the code needs after
the script, breadth first in order of first reference. That means the subs the code references, in code order,
then the subs each linked sub references in turn. The same order holds in all 326 Himegari scripts that link the
original scene library (`docs/sys4-format-notes.md`, "Where subroutine bodies sit").

- `@guard sub name { … }` places the sub behind an `exit` guard, as the original compiler placed its library
  functions. Control may then fall onto the guard.
- A decompiled script whose library subs sit elsewhere records each such sub's instruction index in the sidecar
  (`library`). The compiler links the sub there instead.

## 7. Canonical lowering

The canonical lowering is the layout Eushully's compiler produced. Each row below comes from
`tools/compiler_census.py` and the format notes. Matching the original's lowering is what makes ordinary code
round-trip without a sidecar.

### 7.1 Control flow

| Construct | Lowering |
|---|---|
| `if c {T}` | eval `c` → t; `jcc t, -1, end`; T; `end:` |
| `if !c {T}` | eval; `jcc t, end, -1`; T; `end:` (the negation flips the branch sense) |
| `if c {T} else {E}` | eval; `jcc t, -1, else`; T; `jmp end`; `else:` E; `end:` |
| `else if` | nested `if` in the else block (same bytes): every arm's `jmp` targets the chain's one end |
| `while c {B}` | `head:` eval; `jcc t, -1, end`; B; `jmp head`; `end:` |
| `loop {B}` | `head:` B; `jmp head`; `end:` (the corpus's `while` loops evaluate their condition inside the body, so they lift as `loop` with `if !c break` until expressions fold, stage C2) |
| `for i; c; s {B}` | i; `jmp cond`; `inc:` s; `cond:` eval; `jcc t, -1, end`; B; `jmp inc`; `end:` |
| `do {B} while c` | `head:` B; eval; `jcc t, head, -1` (the 20 / 12 backward jump-if-true forms; confirmed by the core round trip) |
| `if c goto L` | eval; `jcc t, L, -1` |
| `if !c goto L` | eval; `jcc t, -1, L` |
| `if c goto A else goto B` | eval; `jcc t, A, B` |
| `if c { goto L }` | eval; `jcc t, -1, skip`; `jmp L`; `skip:` |
| `switch` | `0xa1`, one `0xa2` per case, `0xa3` with the default; bodies follow as labels (from opcode semantics and samples such as `BTL`; the census does not yet measure switch layout) |
| `break` / `continue` | `jmp` to the loop end / increment-or-head |
| `atomic {B}` | `0x1f4`; B; `0x1f5` |
| `x = &L` | `mov x <offset of L>`, an immediate the encoder relocates |
| `call *v` | `call v` with a variable operand |

Conditions that are plain variables are used directly (`jcc var, …`). Otherwise the condition is evaluated into
the statement's first temporary.

### 7.2 Expressions and temporaries

The original compiler's expression lowering was measured on both corpora; `docs/sys4-format-notes.md` ("How
statements are lowered") records the facts. OMS lowers the same way, through one shared component
(`Compile/ExpressionLowering.cs`) that both the compiler and the decompiler's fold check use.

- **Three-address code.** An assignment whose value is an operation writes straight to its destination
  (`x = a + b * c` → `mul t b c; add x a t`). Deeper operations go through temporaries.
- **Evaluation order.** A binary operation computes its binary operands, then binds its element operands just
  before its own instruction. `x = y` evaluates `y` before the target; `x op= y` binds an element target first and
  binds it again as the left operand. Call operands are evaluated one at a time.
- **Element access** lowers to `lookup-array` or `lookup-array-2d` into a reference temporary that is then used as
  the operand, read or written.
- **Temporary pool.** Integer temporaries come from the pool above the highest local integer the source names
  (so they never collide with named cells). Reference temporaries start at slot 0 of their bank.
- **Canonical numbering.** One counter per statement labels the temporaries in the original compiler's order. An
  assignment labels its value, then its target; a binary node labels its binary operands, then itself, then its
  element operands; an element labels itself, then its indices. Label `k` is pool slot `k` or reference slot `k`.
- **Sidecar residue.** The decompiler keeps a fold only when lowering the folded statement reproduces its
  instructions. Where the original slots differ from the canonical plan, the sidecar records them:
  - `temp_base`: the pool's first slot, when it differs from the highest named local integer plus one;
  - `ref_swap`: the script starts its integer-reference registers as `LP1`, `LP0`;
  - `temps`: the exact slots of individual statements, keyed by the index of the statement's first instruction.

  The residue is published in §16.

### 7.3 Header local counts

Each local bank count is the highest slot used plus 2, so every bank has one spare slot. This holds in all but a
few dozen script/bank pairs, which are covered by the sidecar.

### 7.4 Constants

- No folding. `const` names and literal arithmetic compile literally: `100 - 25` emits `sub t 100 25`.
- A negative integer literal such as `-1` is an immediate, which the VM accepts as is. Unary minus on anything else,
  including a parenthesized literal `-(1)`, emits `sub t 0 x`, which is how the original compiler built negative
  values (`docs/sys4-format-notes.md`); decompiled code therefore prints it as `-(0x1)`.
- An optional `--fold` flag (rebuild mode only) may fold, but never in preserve mode or canonical output.

### 7.5 Data and tables

- **Strings and arrays.** Each string reference gets its own copy, placed after the code in first-reference
  order. Inline integer arrays (`local int t[5] = [4, 0, 5, 0, 538]` → op `0x64`) interleave in the same order.
- **Tables.** T1, T2 and T3 are derived from the code: the `0x71`, `0x03` and `0x8f` sites in order.

## 8. Static checking

### 8.1 Operand contracts

Each argument is checked against the access kind of its operand position under the target ABI:

| Access kind | Accepted argument |
|---|---|
| `read` | int or float expression, or a string literal (warning: parsed with `atol`) |
| `read-float` | numeric expression |
| `read-string` | any value; numbers become full-width digits (warning on implicit int→text) |
| `write` / `write-float` | int or float lvalue |
| `write-string` | str lvalue |
| `bind` | a `ref` variable |
| `address` | a storage lvalue, such as an array base, span or variable |
| `cell` | a global int lvalue or `ref int` |
| `string-cell` | a global str lvalue or `ref str`; local strings are rejected, matching the native throw |
| `ref-slot` | a `ref` variable (global or local integer reference) |
| code target | a `sub`, `coroutine`, label or `-1` as the registry's `code_target_kind` requires |
| `ignored` | any operand; only `note` uses it |

### 8.2 Other errors and warnings

**Errors:**

- unknown symbol;
- an opcode not installed in the target ABI;
- an extended-only construct under `native`;
- a transfer crossing an `atomic` boundary;
- `coroutine` without `resume`;
- text not encodable in CP932;
- an inline array outside a legal position;
- local-call nesting deeper than the 256-entry return stack, when statically provable (a warning when it
  cannot be proven).

**Warnings:**

- implicit string↔number conversion;
- division by a constant zero;
- recursion with bound parameters;
- an opcode never observed in the game's corpus;
- an `atomic` body containing a wait.

The compiler reports the middle three so far (`OmsWarning`, printed by `Age.Cli oms-compile`). The type-dependent
string↔number warning and the `atomic`-wait warning wait for the checker.
## 9. Linking and identity

### 9.1 Scripts

`run NAME` resolves NAME in the target catalog: SYS4INI, then the mounted `*.AAI` catalogs in selector order
(`engine-re.md`, "S4AC append catalogs"). The packed id `selector << 24 | index` is what `call-script` encodes.

### 9.2 Globals

Registry globals resolve to fixed addresses. Linker-allocated globals:

- **under `native`:** allocated from free cells the profile declares. `saved` globals must fall inside the SYS4INI
  saved prefix. Finding free cells is a per-game base-mod responsibility, and the evidence for it is still open
  (§13);
- **under `extended`:** stored in engine-owned, namespaced mod state reached through extension intrinsics. They
  never alias native banks.

### 9.3 New scripts under `native`

The linker emits a selector-scoped append catalog (`S4AC`) naming the mod's scripts, and the scripts ship as loose
files beside it. Op `0x143` runs each mounted catalog's record 0 at INIT2, which gives a native mod an entry point.
A synthetic catalog has not yet been tested under native AGE (§12, gate 6).

### 9.4 Extension opcodes (`extended` only)

Engine-owned opcodes live at `0x10000` and above, well outside the native 0x400-slot dispatch table and the
catalog. They are declared in an extension registry with the same schema as `opcodes.toml`, including access
contracts, and reach OMS as an `abi.ome` module. This is the only way the language gains host capability.

### 9.5 Site identity

Every instruction of a decompiled original keeps its original site:
`(profile, catalog identity, base fingerprint, original dword offset)`. The sidecar carries these identities, so
edits and fragments can refer to original sites stably (roadmap §3.4).

Site identity is for referring to original code. It must never decide behavior. Two rules keep code positions out of
plain numbers:

- **Code positions held as data are labels.** The registry marks operands whose value is a code position
  (`value = "code-address"`; op 0x140's returned PC). The decoder follows such a value through `mov` and
  `eq`/`ne` inside a basic block. An immediate it meets there becomes a label, printed `&label`, so it relocates
  with the code. Every ADV scene's loop exit test is this shape: `G[0x6c3] = &loop_head; if G[0x6c3] == G[0x6be]`
  (144 Himegari / 269 Kamidori scenes). A proven code value that is not an instruction start stays a number and
  is counted.
- **The runtime finds sites by role, not offset.** The port's labeled-yield model derives the loop head from the
  code and halts when the script's terminal names something else. Its one site-specific quirk (SC0000's intro
  movie surface) is keyed by the movie it plays, not its offset.

## 10. Fragments and patches

```
patch ADDEXP fingerprint "sha256:…" {
    after site himegari.experience.after_calculation {
        exp_gain *= 2
    }
    replace operand site 0x4f1 #2 with 10
}
```

How a fragment compiles:

- **Environment.** It compiles against the decompiled base. Base locals, labels and globals are visible by their
  decompiled or registry names. New locals are appended (the bank counts grow), and temporaries follow the base
  pool rules.
- **Output.** The result is a transform plan made of insertions and operand or region replacements, anchored to
  original sites. It contains no byte offsets.
- **Application.** The OMP loader applies plans and detects conflicts (roadmap §3.5–3.6). The compiler only
  guarantees that each plan is valid against the fingerprinted base.

## 11. Decompilation (the reverse direction)

The pipeline runs in eight steps:

1. **Decode** to the intermediate form: instructions with symbolic labels, strings and inline arrays.
2. **Build** the control-flow graph.
3. **Structure** it using the canonical shapes of §7.1. Irreducible or unmatched flow stays as labels and `goto`.
4. **Recognize library subroutines** by position-independent body fingerprints (the census fingerprint). Calls
   are renamed and the bodies are removed from the source; their placement is recorded in the sidecar.
5. **Lift templates.** Every template in the imported modules is compiled once per `static if` branch, with
   typed placeholder parameters, into an instruction pattern. Labels and temporaries are pattern variables.
   Matching uses normalized instructions; when several patterns match, longest wins, then declaration order.
   A lift is accepted only if recompiling the lifted statement in place reproduces the consumed instructions,
   temporaries included.
6. **Name** storage from the registry, scripts from the catalog, and constants from prelude enums where a
   template declares them.
7. **Emit** source at the requested level:
   - `raw`: every instruction as `@op`;
   - `core`: structured, no templates;
   - `idiomatic`: templates and libraries.
8. **Write the sidecar**: original sites, data order, unreferenced dwords, slot pins, library placement, and raw
   CP932 bytes wherever decode-then-encode differs. Nothing in it changes behavior (P3): gate 3 runs every scene
   without it, at the core and idiomatic levels.

The idiomatic level (`Age.Cli oms-decompile --level idiomatic`) implements steps 4 and 5 as follows.

- **Patterns.** `OmsIdioms` compiles every library sub of the source modules once, and every template once per
  combination of its optional constant parameters.
  - Temporaries use a probe slot range.
  - Arguments are placeholder integers, cells and strings.
  - A variant in which a passed parameter leaves no trace is dropped, because nothing could recover it.
- **Matching.** Code matches a pattern when opcodes and operands agree, with these exceptions:
  - temporaries match any slot of their bank;
  - internal labels match by offset;
  - references to library labels match by name.
- **Library recognition.** A carved sub is a library sub when its code matches a pattern, from its `exit` guard
  when the sub is `@guard`. Code outside it may enter it only at the module's labels. Matches whose references to
  other library labels do not land on the matching names are dropped until the set is consistent.
- **Template lifting.** A template use is lifted at a node when its longest matching variant ends on a node
  boundary, is entered only at its start, and calls only recognized library labels.
- **Verification.** The lifted source is compiled in fit mode against the original, which re-derives every
  statement's temporary pins from the original instructions. It is kept only if the result is byte-identical.
  Otherwise the core source is written, with the reason.

### 11.1 Template rules that keep lifting certifiable

- A template parameter of kind `text`, `const` or storage must appear in the expansion in a recoverable position.
  A parameter used only by `static if` selects which pattern applies.
- Expansions may contain `atomic` blocks, library calls and intrinsics, but no bare `goto` out of the template.
- At module build time, two templates whose patterns can match the same instruction sequence are an error,
  unless one is strictly longer.

### 11.2 Dialogue statements

One statement form has a prelude-bound meaning: an optional speaker, a string literal, and optional named
arguments.

```
"――かつて、戦いがあった。"
mao "「まおーさま、だいじょうぶ？」" (voice: 0x34e)
```

A prelude marks one template `@dialogue`, for example `@dialogue inline line(text: text, speaker: speaker = none,
voice: const = none)`. A dialogue line binds its text to the template's `text` parameter and its speaker, a
prelude constant, to `speaker`. Other arguments bind by name.

The Himegari prelude's `line` template is to reproduce the decoded ADV line expansion:

- the voice-state reset block;
- the speaker nameplate block, or the line-prologue library call;
- the `mute_next_voice` branches before `play_voice`;
- `show_text`, `end_text_line`, `wait_for_input`, `end_text_line`;
- `reset_adv_text_layout`.

Decompiling `SC0000` with that prelude should then yield lines like those above.

`oms/himegari/adv.oms` implements this. Its `enum speaker` holds CNINIT's names for `unit_story_display_names`;
names that are not identifiers, or that repeat, carry their id. The template has three traceable variants:

- narration;
- speaker;
- speaker with voice.

Pages that interpolate a string cell, carry furigana, span several rows, or skip the prologue stay core
statements for now.

## 12. Validation gates

Each gate must pass on the complete Himegari and Kamidori corpora before the next phase starts:

1. **Encoder/decoder:** intermediate-form round trip is byte-identical for every script.
2. **Core level:** `BIN → OMS(core) → BIN` is byte-identical, with sidecar use counted and published.
   - The canonical allocator is tuned per ABI until the sidecar records no temporary-slot overrides beyond a
     published residue.
3. **Canonical rebuild:** compiling without the sidecar yields valid scripts that are VM-trace-equivalent to the
   originals on the port's regression scenes.
4. **Fixed point:** `OMS → BIN → OMS → BIN` is byte-identical for compiler-generated code.
5. **Idiomatic level:** lift coverage, as the percentage of instructions inside accepted template or library
   lifts, is published per prelude. Every accepted lift recompiles exactly by construction.
6. **Native AGE:**
   - an edited scene runs as a loose-file override;
   - a new script runs from a generated append catalog;
   - both are tested in a hard-linked scratch copy of the install, never the pristine game directory.

Byte identity proves the encoding, not the semantics. Semantic trust still comes from the operand contracts, the
port's VM tests and native traces.

## 13. Open questions

| Question | Blocks | Plan |
|---|---|---|
| Allocator state carried across statements | the gate 2 temporary residue (§16) | the labelling rule and the per-script reference order are recorded (format notes); the remaining overrides show references held through `if` bodies and LP0/LP1 order flips, to be modelled if the residue matters |
| Library placement policy (where shared library helpers sit physically) | resolved 2026-10-07 | the compiler links library subs after the script, breadth first in order of first reference, `@guard` subs behind an `exit` (§6.3); subs a script keeps elsewhere (Himegari's scene-header hotspot handlers) are placed by the sidecar |
| Free global cells inside the saved prefix | native `saved global` | array extents from registry/INIT data plus runtime write captures; base mod publishes free ranges |
| Native acceptance of generated append catalogs, and save behavior with them | §9.3 | gate 6 experiment |
| Meaning of the 0x90-byte SYS4INI block | none known | revisit only if a consumer appears |
| Extended mod-state model and its save interplay | `extended` globals | OMP / `maid.state` design (roadmap §3.8) |
| Code-address immediates outside code-target positions | resolved 2026-10-07 | the decoder relabels immediates that meet a registry `code-address` value (§9.5); the scene loop's `G[0x6c3] = 0x45e` decompiles as `&label_45e`, so edits and sidecar-free rebuilds that move code keep it right |
| Concrete surface syntax details (this draft's spelling) | none: semantics are fixed above | settle in review before the parser is written |

## 14. Language evolution

- **Version declaration.** Every unit may declare `oms 1` (the default).
- **What changes grammar:** only a new language version. That requires a spec revision, and the compiler keeps
  accepting older versions.
- **Data changes, no version bump:** new opcode contracts, extension opcodes and game registries regenerate
  their modules.
- **Library changes, no version bump:** new idioms are prelude templates. Because templates also feed the
  decompiler, a new template can only add lifts. It can never change the bytes of existing code.

## 15. Implementation outline

The implementation is a C# library (`Age.Script`) beside `Sys4Loader`, exposed as `Age.Cli oms-compile`,
`oms-decompile`, `oms-roundtrip` and `oms-rebuild`, so the engine can compile fragments at mod-load time. Its
stages:

- lexer, parser and syntax tree;
- binder over the generated modules;
- checker (§8);
- template expander;
- lowerer to the intermediate form;
- allocator;
- encoder, which writes the layout, data, tables, header counts and sidecar;
- decoder, CFG, structurer, library and template lifter, and printer.

The build follows roadmap step 10 and the gates in order: encoder/decoder, core language, structurer, templates,
Himegari prelude, native validation, then the Kamidori prelude. The Python tools stay as analysis and as
independent oracles. `compiler_census.py` describes the lowering target, and `operand_access.py` derives the
contracts.

## 16. Implementation status

| Gate | Status | Evidence |
|---|---|---|
| 1. Encoder/decoder | **passed 2026-10-06** | `Age.Cli oms-roundtrip`: 520/520 Himegari and 732/732 Kamidori catalog scripts byte-identical; tables always derived; workspace tests `OmsIrCodecTests.Installed*CorpusRoundTripsByteIdentically` |
| 2. Raw level (`BIN → OMS(raw) + sidecar → BIN`) | **passed 2026-10-06** | `Age.Cli oms-roundtrip --level raw`: 520/520 and 732/732 byte-identical; workspace tests `OmsSourceTests.Installed*CorpusRoundTripsThroughRawSource` |
| 2. Core level, stage C1 (structure) | **passed 2026-10-07** | `Age.Cli oms-roundtrip --level core`: 520/520 and 732/732 byte-identical with **0** raw `@op` statements; workspace tests `OmsCoreTests.Installed*CorpusRoundTripsThroughCoreSource` |
| 3. Canonical rebuild | **passed 2026-10-07** | `Age.Cli oms-rebuild` (also a `validate.py --level workspace` gate): every catalog script rebuilt from core source without its sidecar (441 / 568 change bytes); all 297 / 279 SC/SP scenes, walked page by page and also from the data-table boot, execute the same instruction sequence (27.7M / 38.1M steps) with the same text, halts and final state on a simulated clock. Scenes run with the profile's VM compatibility facts, as the runtime runs them, so the ADV scene-entry coroutine (op 0x140) executes. `--level idiomatic` checks sidecar-free idiomatic rebuilds the same way, comparing positions by relocation key; it passes on all 297 / 279 scenes. A negative control (decoder code-value pass disabled) diverges at SC0000's labeled yield |
| 4. Fixed point | **passed 2026-10-07** | `Age.Cli oms-roundtrip --level core --fixed-point`: decompiling each canonical rebuild and recompiling it is byte-identical for 520/520 and 732/732; 506 / 713 of them need no sidecar at all, and the rest need only temporary pins (named locals interleaved with the original's temporaries); workspace tests `OmsCoreTests.Installed*CanonicalRebuildIsAFixedPoint` |
| 5. Idiomatic level (Himegari prelude) | **passed 2026-10-07** | `Age.Cli oms-roundtrip --level idiomatic`: 520/520 byte-identical with `oms/himegari/adv.oms` and 0 abandoned lifts. 9,256 library subs are recognized (the 32 scene-library subs), and 24,297 of 26,667 ADV pages lift to dialogue lines. Lift coverage is 998,654 instructions, **65.6%** of the corpus. Source shrinks from 44.7 MB (core) to 18.8 MB. With `--fixed-point`, idiomatic rebuilds are still 520/520 identical and 506 need no sidecar. The 307 scene sidecars that keep the header handlers' place hold `library` entries. Workspace tests `OmsCoreTests.InstalledHimegari*Idiomatic*`. Kamidori has no prelude yet |
| 2. Core level, stage C4 (declarations and registry naming) | **passed 2026-10-07** | still 520/520 and 732/732 byte-identical with `<profile>.globals` and `<profile>.scripts` applied; `use`, `const`, `global` and `local` declarations, named elements and `run` names covered by `OmsCoreTests` |
| 2. Core level, stage C3 (`sub`, `coroutine`, bound parameters) | **passed 2026-10-07** | still 520/520 and 732/732 byte-identical; 10,914 / 12,287 subs and 614 / 644 coroutines recovered; the compiler's falls-into, implicit-return, coroutine-ending and code-reference-kind checks are covered by `OmsCoreTests`; warnings (§8.2, including recursion with bound parameters) await a warning channel |
| 2. Core level, stage C2 (expressions, `for`, `while`) | **passed 2026-10-07** | `Age.Cli oms-roundtrip --level core`: still 520/520 and 732/732 byte-identical, with 331,075 / 330,708 instructions folded into expressions; temporary overrides in 6,548 / 8,332 statements (2.9% / 4.4% of folded statements); `Age.Cli oms-temps` ranks the residue |

Gate-1 residue, reported by `Age.Cli oms-residue`:

- **Labels resolved:** 241,972 / 403,429 code-target operands become labels. The only code-target operands left
  literal are the indirect calls `call G[0x6be]` / `call G[0x4f8]` (144 / 269), whose operand is a variable.
  Another 144 / 269 immediates are code positions held as data (§9.5) and become labels too.
- **Data:** every data operand resolves to a typed item (39,004 / 78,211 strings and 5,487 / 13,576 inline
  arrays).
  - The last unresolved item was Kamidori `ITMES.BIN`'s `　ぐろｰぶによる基本攻撃`. Its half-width katakana was
    rejected by the shared string decoder (`Sys4StringCodec`, and `sys4load` in Python), which also blanked that
    text at runtime.
  - Both decoders now accept the single-byte katakana range 0xA1–0xDF.
- **Unreferenced data:** 660 / 3,492 dwords are preserved as raw items.

Core stage C1 lifts three-address code into structured statements: `if`/`else if`/`else`, `loop`, `do … while`,
`atomic`, `break`/`continue`, conditional gotos, intrinsic calls with `out`, `run`, `note`, `return`/`exit` and
`call *v`. Assignments stay single operations, so temporaries are still visible. Two structuring rules keep the
round trip exact:

- **Shared joins stay flat.** An `if` whose then-body ends in `jmp end` lifts as an `if … else` only when the
  join is private to the chain: no forward transfer outside the chain's own arm jumps targets it. Otherwise the
  arm prints as `if c { …; goto L }`. Backward jumps to the join (a loop starting there) do not count. Long
  dispatch ladders such as `SCJUMP` and `ITMES` therefore stay flat gotos, as their original source evidently
  was.
- **Iterative chains.** `else if` chains are parsed, lowered and printed iteratively, because the corpus has
  chains thousands of arms deep.

Core stage C2 folds compiler temporaries back into expressions and recovers loops:

- **Expressions:** operators with C precedence, `&&`/`||` over comparisons, unary minus, element access
  (`G[0x3306][i]`, `G[0x341ab:0x35][r][c]`), and compound assignment (`+=` and the others).
- **Loops:** `for init; c; step { … }` where the layout is `init; jmp cond; inc: step; cond: test`, and
  `while c { … }` where a loop opens with its exit test. Himegari recovers 2,472 `for` and 62 `while` loops; 303
  plain `loop`s remain.
- **Rejected folds:** 211 / 219 statements whose lowering order differs from the canonical one keep their
  temporaries explicit.
- **Source size:** core source shrinks from 53.1 MB to 41.6 MB (Himegari) and from 70.3 MB to 59.0 MB (Kamidori).

Core stage C3 carves top-level code into placed `sub` and `coroutine` blocks. An entry becomes a block when:

- only calls, callbacks or coroutine registrations reach it;
- the code before it does not fall through;
- it extends to the first boundary where no jump crosses in or out and control cannot fall off;
- for a coroutine, that boundary follows a `resume`.

Intrinsics whose only written operand is operand 1 print as `x = f(args)`.

Core-level sidecar use:

- Empty in 71 / 156 scripts.
- `ref_swap` in 191 / 53 scripts, `temp_base` in 88 / 305, and `temps` in 376 / 497.

Raw-level sidecar use:

- 383 of 520 Himegari and 389 of 732 Kamidori scripts need an empty sidecar.
- Data order or unreferenced data needs one in 132 / 332 scripts.
- Local counts need one in 5 / 16 scripts, and magic in 5 / 0 (Himegari's `SYS4424` scripts).
- No string needed exact bytes: every decoded string re-encodes canonically.
