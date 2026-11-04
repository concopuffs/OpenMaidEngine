# Patching original scripts

A patch, or *fragment*, edits one original script in place and leaves everything else in it untouched. Several mods
can patch the same script, and old saves keep working. Prefer a patch to a replacement whenever you can. A
fragment is a file `patches/NAME.oms` in your mod ([packages.md](packages.md)).

## Anatomy

```text
oms 1
patch "ADDEXP.BIN" fingerprint "sha256:b4f38c43f2c500012be48517ce773d3c9e81c12deeb4d712cab4b99749661e1c"
use himegari.globals

local int exp @ 0x1                       // name an existing local of the base script

before site himegari.experience.after_calculation {
    exp = exp * 2                         // double every experience award
}
```

- **Header:** the script's catalog file name and the SHA-256 of its bytes. Print it with
  `Age.Cli oms-patch --fingerprint ADDEXP.BIN` (add the Kamidori options from [first-mod.md](first-mod.md) for
  Kamidori). If the installed bytes differ, compilation and loading both fail and print the real fingerprint.
- **Declarations:** `use`, `const`, `enum`, `global`, `local`, `table`, `inline`, and any `sub`s the operations call.
- **Operations:** one or more `before`, `after` or `replace` blocks. Code outside an operation is an error.

## Sites

A site names one instruction of the original script. Write it in one of three ways:

| Form | Example | Notes |
|---|---|---|
| offset | `site 0x877` | The dword offset shown by the page locator, `locate_page.py` and the decompiler. It must be the start of an instruction. |
| label or sub | `site label_877`, `site adv_wait_page` | `label_<hex>` always names the instruction at that offset, even where the decompiled source prints no label. |
| named patch point | `site himegari.experience.after_calculation` | A stable name from the game's patch-point registry. It resolves to the right offset for the installed script revision. |

Named patch points are the most robust choice, because a future revision of the game can move the offset while the
name stays the same. The registry is small for now. Himegari has one point (`../patch-point-reference.md`), and
Kamidori has none yet (`../backlog.md`), so most sites are offsets.

## Operations

| Operation | Effect |
|---|---|
| `before site S { … }` | Runs every time control reaches S, by falling through or by a jump. Jumps to S now land on your code. |
| `after site S { … }` | Runs every time S completes and falls through to the next instruction. A jump to that next instruction skips your code. S must be able to fall through: `goto`, `return` and `exit` cannot be after-sites. |
| `replace site S { … }` | Replaces the single instruction S with your code. |
| `replace site S through E { … }` | Replaces S through E inclusive. An empty body deletes them. A jump into the middle of the region from outside it is an error, because its target would disappear. |
| `replace site S operand N with V` | Changes one operand of S. N counts from 1. V is a constant, a cell, a string or `&label`. |

Your code falls through to whatever follows the site. Inside a block:

- labels and subs of the decompiled original work as `goto` and call targets;
- the prelude's library subs are shared with the original if it already uses them, and added otherwise;
- `local int x @ 0x3` names a slot the original uses. A local without `@`, and every temporary the compiler needs,
  goes into a fresh slot above all of the original's, so your code cannot clobber the original's values.

## Choosing a site

Use the page locator in the port (**F2**, then **F3** to copy) and resolve it:

```text
py -3.11 -X utf8 tools/locate_page.py SC0000 3 --context 12
```

Then decompile the script (`oms-decompile … --level idiomatic`) and read around the offsets it prints. Some
patterns:

- **A new page before page N:** use `before` on the instruction that starts page N. That is the
  `reset-adv-text-layout` after the previous page's `wait-for-input` and `end-text-line`. Himegari SC0000 page 3
  starts at `0x877`, and Kamidori's translated SC0000 starts its first page at `0xd0a`.
- **Change a value:** `replace site … operand N with …` on the instruction that sets it.
- **Skip or rewrite a passage:** `replace site S through E`. Start and end on whole statements, and check the
  decompiled source for labels inside the region that code outside it jumps to.
- **React to a computation:** `before` or `after` the instruction that finishes it. A named patch point, where one
  exists, says exactly what holds at that moment.

## Kamidori's translation is a different base

The fan translation replaces scripts such as `SC0000.bin` with translated ones, which have different bytes and
different offsets. A fragment fingerprints the bytes it was written against:

- for a translated install, print fingerprints and decompile with `--overlay-root patch`;
- a mod that should work on both needs two fragments, one per base.

A fragment whose base is not installed fails with a fingerprint mismatch.

## How several mods combine

Every installed mod's fragments apply to the pristine original, in load order ([packages.md](packages.md)). The
link report (`diagnostics/mod-link.txt`) classifies what it finds:

| Situation | Result |
|---|---|
| Edits at different sites | They compose. |
| Several `before` (or `after`) blocks at one site | They compose in load order, and the report lists the order. |
| Two replacements of one operand | Hard conflict: the load stops. |
| Overlapping `replace` regions | Hard conflict. |
| An insertion, operand edit or site reference inside another mod's replaced region | Hard conflict. |
| A patch to a script another mod replaces wholesale | Fails unless the patch targets the replacement's fingerprint. |

You cannot patch code another mod inserted. If your mod builds on another, declare it in `[[requires]]` and patch
the original around it.

## Saves stay valid

Saves and the read-message history refer to pages and calls by index into three tables in the script. A patch keeps
every original entry at its index and adds entries for new pages after them. So:

- a save made before you install a patch resumes on the same page afterwards;
- read-message flags stay on the right messages;
- a save that stands on a message your patch removed resumes at your replacement code.

Removing the mod again is less forgiving. A save made on a page the mod added has no such page in the original
script. The port warns when a save's mod stamp differs from the installed mods ([troubleshooting.md](troubleshooting.md)).

## Compiled plans

A fragment compiles to a *transform plan*, `NAME.omspatch.json`. You do not write plans by hand:

- during development, the loader compiles your `.oms` when the game starts;
- `omp build` stores the plan beside the source in the package, so players do not need the compiler for it.

The loader revalidates a stored plan against the installed script on every load, and recompiles from source when
the fingerprint differs. To see the patched result directly, compile fragments with `oms-patch` and write the
patched scripts:

```text
Age.Cli oms-patch --source patches/opening.oms --output-dir build/patched
```
