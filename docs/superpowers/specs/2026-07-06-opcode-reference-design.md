# Design: Living Opcode Reference (single-source-of-truth + provenance)

Status: **approved (design)** · Date: 2026-07-06 · Author: session work
Related: `docs/phase-a-slice-plan.md`, `vm-map/himegari-opcode-notes.md`, `docs/PROJECT-STRUCTURE.md`

## Problem

Opcode knowledge for the AGE/SYS4 VM is currently spread across five artifacts:

| File | Role today | Consumed by |
|------|-----------|-------------|
| `tools/age_opcodes.py` | Kelebek table verbatim: `{op: (label, argc)}` + arg-types | sys4load, vm0 |
| `tools/age_opcodes_himegari.py` | our `INFERRED` dict (name/category/noop/confidence/method/note) | sys4load |
| `vm-map/opcodes-himegari.json` | data snapshot (arg-types, header fields, per-op array) | — (reference) |
| `vm-map/himegari-opcode-notes.md` | prose evidence, buckets A–F | — (humans) |
| `build/opcode-coverage.md` | coverage tiers | — (humans) |

The same fact lives in several places, so they drift (this session already found stale "6/8 scenes"
wording, and `age_opcodes_himegari.py`'s `method` field duplicates prose in the notes doc). None of
them record **why** we believe a claim or **what other claims it rests on** — so when a reversal proves
one opcode wrong, there is no way to find the downstream inferences that depended on it.

## Goals

1. **One canonical, hand-edited file** as the single source of truth; every other opcode artifact is
   generated from it and can never disagree with it or with the VM.
2. **Provenance per claim** — record the source (Kelebek / our harness / our investigation / Frida /
   Unicorn) and confidence, with the near-certain ABI facts kept separate from the fallible semantics.
3. **Dependency tracking** — a semantic claim can declare the opcodes whose interpretation it rests on,
   so a correction's blast radius is visible and mechanically checkable.
4. **Zero disruption to working tooling** — sys4load/vm0 keep importing the same Python interface,
   now generated.
5. **Complete map** — an entry for every one of the 248 opcodes Himegari uses, so coverage is measurable
   and any opcode can be named as a dependency.

## Non-goals

- Not touching `tools/age_opcodes.py` (the Kelebek table): it stays pristine as the ABI baseline and the
  bootstrap input.
- No per-call-site type modelling (arg types are per-instruction in the bytecode, not fixed per opcode).
- No live-capture work (Frida/Unicorn) here — this is the reference structure; those remain sources a
  future entry can cite.
- Not covering the ~300 Kelebek opcodes Himegari never uses.

## Architecture

One canonical file; everything else is generated:

```
vm-map/opcodes.toml            ← CANONICAL, hand-edited. [meta] + 248 [[opcode]] tables.
        │
        ▼  tools/opcodes_build.py         (generator + linter; stdlib tomllib read, no new dep)
        ├─ tools/age_opcodes_himegari.py  GENERATED — exposes INFERRED (sys4load imports unchanged)
        ├─ build/opcodes.json             GENERATED — language-neutral machine view (future C# VM)
        ├─ docs/opcode-reference.md        GENERATED — human reference incl. reverse dependents index
        └─ build/opcode-coverage.md        GENERATED — counts by source / confidence / category
```

- `tools/age_opcodes.py` (Kelebek) **stays**: ABI baseline + bootstrap input, never hand-edited.
- **Retired** (content migrates into `opcodes.toml`, then re-emitted): hand-maintained
  `age_opcodes_himegari.py` (now generated), `vm-map/opcodes-himegari.json` (→ `build/opcodes.json`),
  `vm-map/himegari-opcode-notes.md` (per-op evidence → each entry's `details`; rendered into
  `docs/opcode-reference.md`), and hand-maintained `build/opcode-coverage.md`.
- Editing loop: edit `opcodes.toml` → run `opcodes_build.py` → tooling, machine view, human doc, and
  coverage all update together.

## Canonical file format (`vm-map/opcodes.toml`)

### `[meta]` (migrated from `opcodes-himegari.json`)
- `instruction_model` — the `1 + 2*argc` decode description (string).
- `[meta.arg_types]` — type-code → name (`0x0`→immediate, `0x2`→string, `0x9`→local-int, …).
- `[meta.header_fields]` — F0–F12 meanings.
- `[meta.sources]`, `[meta.confidence]`, `[meta.categories]` — controlled vocabularies (documented
  below), so the linter can reject unknown values.

### `[[opcode]]` — ABI block (high trust)
| field | meaning |
|-------|---------|
| `op` | opcode number (TOML `0x..`) |
| `label` | Kelebek engine address (e.g. `"u0041BEB0"`) |
| `argc` | argument count — **validated by our 481/481 clean decode** (`len = 1 + 2*argc`) |
| `code_target_args` | 1-based arg indices that are jump targets (from Kelebek's notes; optional) |
| `abi_source` | e.g. `"kelebek+decode-validated"` |
| `abi_note` | optional caveat |

### `[opcode.semantics]` — our fallible layer (where cascades live)
| field | meaning |
|-------|---------|
| `name` | short mnemonic for the disassembler (**the one field sys4load requires**) |
| `category` | `marker\|structural\|control\|adv\|draw\|audio\|input\|compute\|unknown` |
| `summary` | one-line description |
| `noop_headless` | bool — safe for the headless VM v1 to skip/fall-through (no state/visible effect) |
| `source` | `kelebek\|harness\|investigation\|frida\|unicorn\|inference` |
| `confidence` | `high\|med\|low` |
| `depends_on` | list of op ids whose interpretation this claim rests on (default `[]`) |
| `evidence` | concise grounding (one line) |
| `details` | optional multi-line long-form evidence (migrated from the notes doc) |
| `confirm_by` | optional — what would raise confidence or falsify this |

### `[[opcode.semantics.args]]` — per-arg roles (array of tables, optional)
| field | meaning |
|-------|---------|
| `i` | 1-based arg index |
| `role` | semantic role (`"x"`, `"y"`, `"target:click"`, `"count"`, …) |
| `observed_types` | list of type names actually seen at this position (**auto-filled from corpus**) |
| `note` | optional |

### Source vocabulary (grounding roots vs. fallible)
- `kelebek` — from the Kelebek table (ABI trusted; *semantics* from a later AGE title → treat as a lead,
  not truth). A root.
- `harness` — confirmed by our dialogue-oracle diff (`build/text/dialogue.jsonl`). Empirically grounded root.
- `investigation` — our own static/corpus analysis (may `depends_on` other ops).
- `frida` / `unicorn` — live runtime capture / micro-execution. Roots (direct observation).
- `inference` — reasoned from neighbours/context; weakest, usually has `depends_on`.

## Generator + linter (`tools/opcodes_build.py`)

Subcommands:
- `--bootstrap` — create `opcodes.toml`: the used-opcode set is computed by scanning the corpus with
  `sys4load` (authoritative, reproducible); seed each with `op/label/argc` from `age_opcodes.py`,
  `abi_source="kelebek+decode-validated"` (argc is validated for all 248 by our 481/481 decode),
  `semantics.source="kelebek"`, `confidence` = `med` if Kelebek names it else `low` (bare
  `u004xxx`/`dev_ukn` label → `category="unknown"`); **auto-fill `observed_types` per arg position**
  from the corpus scan. Idempotent:
  re-running preserves hand-edited entries (only fills missing).
- `--build` (default) — read `opcodes.toml`, run lint, then emit the four generated artifacts. The
  generated `age_opcodes_himegari.py` exposes `INFERRED: dict[int, dict]` with at least `name` (plus
  category/noop/confidence/source for future consumers) — a drop-in for the current interface.
- `--lint` — run the three checks; nonzero exit on any error.

Generated-file guard: each emitted file carries a `DO NOT EDIT — generated from vm-map/opcodes.toml`
header.

## Lint checks (the cascade mechanism)

1. **Dangling-ref (error):** every id in a `depends_on` must be an existing `op`. No orphan dependencies.
2. **Reverse index / dependents (report):** build `A → [ops whose semantics depend on A]`; render it in
   `docs/opcode-reference.md` as a "depended on by: 0x…" line per opcode, so a revision's blast radius is
   visible. Also emitted to the lint output.
3. **Confidence-ceiling (warning):** an entry's `confidence` may not exceed the minimum confidence among
   its `depends_on` targets (can't be `high` while resting on a `low`). Surfaces shaky foundations.
4. **Vocabulary (error):** `category`/`source`/`confidence` must be in the `[meta]` controlled lists.

Chains terminate at a `kelebek`/`harness`/`frida`/`unicorn` root or at direct `evidence`.

## Bootstrap / migration procedure

1. Implement `opcodes_build.py`; run `--bootstrap` → `opcodes.toml` with 248 seeded skeletons + observed
   arg-type histograms.
2. Hand-migrate our real inferences into their entries: the ~26 in `age_opcodes_himegari.py` plus the
   evidence in `himegari-opcode-notes.md` (buckets A–F, incl. the `0x90`/`0x97` deep-dive), each with
   proper `source`, `depends_on`, `evidence`, `details`.
3. Run `--build`; confirm generated `age_opcodes_himegari.py` reproduces the current names, and
   `sys4load … --validate` + `vm0.py --test` + `vm0.py --sweep` are unchanged (differential check).
4. Delete the retired hand-maintained files; update `docs/PROJECT-STRUCTURE.md` and the memory index.

## Testing / verification

- **Regression (must be byte-for-byte where it matters):** after migration, `sys4load` disassembly of a
  few scripts (MENU, SC0830) shows the same mnemonics; `vm0.py --test` PASS and `--sweep` still
  282/294. This proves the generated shim is a true drop-in.
- **Generator round-trip:** `--build` is deterministic; re-running produces no diff.
- **Lint unit checks:** craft a tiny fixture TOML exercising dangling-ref (error), a confidence-ceiling
  violation (warning), and an unknown category (error).
- **Coverage sanity:** generated `opcode-coverage.md` totals reconcile with 248 used opcodes.

## Risks / open questions

- **TOML verbosity for 248 entries** — acceptable; entries are mostly skeletons and diffs read cleanly.
- **`observed_types` staleness** — it's derived; re-run `--bootstrap --refresh-observed` (fills only that
  field) if the corpus set changes. Not load-bearing (evidence, not truth).
- **Deciding `confidence` for Kelebek-named ops** — Kelebek names are ABI-labels from another title;
  default seed = `med` for named, and we downgrade/confirm as the VM exercises them (harness).
- **git** — workspace isn't a git repo, so the design/spec aren't committed; offer `git init` later.

## Out of scope (future)

- Emitting a C#-native binding from `build/opcodes.json` (A1 will consume the JSON directly).
- A GUI/queryable browser over the reference.
- Automated "review-needed" flags on git-diff of `opcodes.toml` (the dependents index already gives the
  manual signal).
