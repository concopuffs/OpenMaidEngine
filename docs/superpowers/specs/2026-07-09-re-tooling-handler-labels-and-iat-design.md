# RE tooling: whole-image handler labeling + IAT reconstruction — design

**Date:** 2026-07-09
**Status:** approved (shape), plan pending
**Home in the canonical map:** implementation notes land in `docs/engine-re.md` (native-RE) and
`docs/tools-reference.md` (the new scripts); this spec is the one-time design record.

## Motivation

Native RE keeps stalling and self-correcting at the *same* structural spots. Reviewing the
`engine-re.md` log, the recurring taxes are:

1. **Kelebek VA-drift** — the `handler(op) = ctx[0x26c93 + op]` fix is known but paid *by hand, per
   op*, and is still the #1 "read the wrong function" error source.
2. **Unnamed imports** — every `CreateFileA`/`ReadFile`/`timeGetTime`/`d3d9::Present`/`std::map`
   shows as a raw indirect call; findings get reconstructed by format-string archaeology instead of
   read off a label. Deferred when RE was one handler at a time; now we sweep dozens.

Both are one-time, static, low-risk investments that make *every* future look at the decomp cheaper.
(A third lever — cracking the statically-unreachable outer frame loop via a wider dump or the live
debugger — is explicitly **out of scope here**; it is gated on a careful anti-tamper probe first,
because the engine has integrity checks that already crash-tested a Frida CModule hook.)

## Workstream A — auto-label every dispatch handler (fully static, no game needed)

**Source of truth:** `FUN_00413860` (the handler-registration routine). It fills `0x400` slots at
`ctx[0x26c93]` with the default handler `FUN_004162b0`, then overrides specific opcodes with
`ctx[0x26c93 + op] = <handler_va>` — in asm, an immediate `handler_va` stored to `[base + disp]`
where `disp = 0x9b24c + op*4` (word index `0x26c93 + op`; e.g. `ctx[0x26e3f]=0x427fb0` → op `0x1ac`).

**Script (run over MCP into the existing annotated program):**
1. Walk `FUN_00413860`'s instructions; match immediate-to-`[base+disp]` stores with
   `disp ∈ [0x9b24c, 0x9b24c + 0x400*4)`. Compute `op = (disp − 0x9b24c)/4`, `handler_va = imm`.
2. Skip any entry whose handler is the default `FUN_004162b0` (not a real override).
3. For each real `(op, handler_va)`:
   - `create_function` at `handler_va` if none exists.
   - **Preserve good names:** if the function already has a non-`FUN_`/`LAB_` name (e.g.
     `gfx_op_0x215_register_query`, `sleep_op_0xc8`), do **not** rename — only ensure a plate comment
     records the opcode. Rename only raw `FUN_xxxx`/`LAB_xxxx` → `op_0xNN_handler`.
   - `set_plate_comment`: `opcode 0xNN dispatch handler; resolved via ctx[0x26c93+op] in FUN_00413860`.
4. Emit `build/op-handler-map.json` (`{ "0x1ac": {"handler": "0x427fb0", "name": "..."}, ... }`).
5. **Cross-check:** diff the derived map against `vm-map/opcodes.toml` handler VAs; print any op where
   toml ≠ derived. Those disagreements are latent VA-drift bugs — surfacing them is a deliverable, not
   a warning to suppress. (Do not auto-edit `opcodes.toml`; report for human reconciliation.)
6. `save_program`.

**Acceptance:**
- `build/op-handler-map.json` lists every override op with its real handler; count is sane
  (~248 opcodes are *used* by the corpus, but the table may register more — record whatever
  `FUN_00413860` actually writes, and note the total).
- Spot-check ≥3 known anchors reproduce prior findings exactly: op `0x1ac`→`0x427fb0`,
  op `0x1a2`→`0x42d360`, op `0x215`→`0x42a0b0`, op `0xc8`→`0x420ec0`.
- Every previously hand-named handler still has its name (zero clobbers).
- The cross-check runs clean or produces an explicit, reviewed disagreement list.
- `engine-re.md`'s dispatch-table section links the generated map instead of enumerating ops by hand.

## Workstream B — reconstruct the IAT + apply the Win32 type archive (needs the game running)

**Decision (approved): graft onto the existing program — never reimport.** A fresh PE import would
give named imports but discard every rename/plate comment we've accumulated. So we keep the current
program and *apply* the reconstructed import names onto it.

**Steps:**
1. With a live game instance, run from **PowerShell** (Git Bash mangles `/flags`):
   `bin\pe-sieve32.exe /pid <PID> /imp 3 /dmode 3 /dir <out>`. Capture the reconstructed module +
   pe-sieve's **import report** (`*.imports.txt`/tag files).
2. A small script reads the import report → `{ thunk_addr → "dll!Function" }` and, in the current
   Ghidra program, applies each as an external-function reference / label at the IAT thunk (via the
   appropriate MCP label / reference / external-location APIs — exact call pinned in the plan).
   Annotations stay intact.
3. Apply Ghidra's bundled **Win32 data-type archive** so the now-named imports carry real signatures.
4. `save_program`.

**Fallback (sanctioned, not a failure): keep both programs separate for cross-referencing.** If the
graft turns into an ordeal (thunk addresses don't line up cleanly, MCP can't set externals on a raw
image, etc.), import pe-sieve's reconstructed PE as a **second, clean Ghidra program** and use it
purely as a *reference* — look up an import by address there, port the name into our annotated program
by hand as we touch each function. We migrate names incrementally until enough is ported that we trust
grafting the rest (or decide the reference workflow is good enough). Either way we never lose
annotations.

**Scope boundary:** pe-sieve names *imports* (Win32 APIs) only. Statically-linked STL/CRT demangling
(`std::map`, `operator new`) is **not** in scope — that is BSim/FidDb territory, noted as an optional
future follow-up.

**Acceptance:**
- Known API sites read as named calls: the save-path handler's `%s\SAVE%2.2d.DAT` formatter shows a
  named `sprintf`/file-API neighborhood; the resolver chain (`FUN_0040e980`/`FUN_0044f390`) shows
  named `CreateFileA`/`SetFilePointer`/`ReadFile`; the `sleep` timer source resolves to a named
  `timeGetTime`-class import.
- Existing annotations (renames + plate comments) are all still present.
- `save_program` succeeds; if fallback taken, the second reference program is documented in
  `engine-re.md`'s runbook with how to use it.

## Sequencing & risk

- **A first** — static, needs no game, de-risks everything downstream (self-navigating decomp), and
  its cross-check may pre-empt B confusion.
- **B second** — when a game instance is available.
- Both update `docs/tools-reference.md` (new scripts) and `docs/engine-re.md` (results) per the doc
  discipline; both end with `save_program`.
- **Explicitly deferred:** outer-loop RE (wider dump / live debugger) — gated on a separate, careful
  anti-tamper probe.
