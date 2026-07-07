# Runtime Global Observation — RE state & starting point

**Goal.** Read the running game's live VM global variables by their bytecode address (e.g.
`G[0x62424]`, `global-string 0x279`). This is the **VM-validation cornerstone**: with it we can
compare our VM's global state against the real game's at any point, verify effectful opcodes that
have no machine oracle (battle math, flag logic, stat updates), and *name* the ~200 still-unclassified
globals by watching them change. The roadmap flags it as the thing that replaces most Frida/Unicorn
guesswork once the VM is validated-correct.

**Status: SHELVED (2026-07-06), deliberately.** We learned the shape of the problem but did not
achieve a general "read any global by address" capability. This doc records what we tried, what we
proved, the concrete landmarks found, and the *right* way to resume. It is **not** on the critical
path for anything else, and — importantly — it does **not** crack the asset-resolution scope selector
(see "What this does NOT solve").

---

## The core finding: our flat-address model ≠ physical layout

Our VM models globals as one flat address space (`G[addr]`), and that is **execution-correct**
(byte-exact against the dialogue-trace oracle). But it is an *abstraction*. Physically the game's
state is **structured and multi-store**:

- **Multiple stores.** VM int-globals, string-globals, and float-globals are almost certainly
  separate arrays (their address magnitudes differ wildly: `global-string 0x279` = 633 vs
  `global-int 0x62424` = 403,492). On top of that, values get **copied into native C++ objects**.
- **Structured records, not flat int32.** Entity data (units, party) is stored as records with
  **inline fixed-size string slots + mixed int fields + native heap pointers** — not a packed int
  array. A `base + addr*4` scan therefore finds nothing (confirmed: no stride in {2,4,8,12,16}
  reproduces a known consecutive-address `*INIT` value run).
- **Transient vs. stable globals.** A global is a real variable, but *how it's used* varies:
  - **Transient** (e.g. `G[0x62424]`, the CG resId): a scratch/argument register — `mov G[0x62424]
    = resId; call load` — set right before a call, overwritten right after. Never holds a stable
    value. Non-atomic value scans **race** against it and miss it.
  - **Stable** (e.g. `global-string 0x279`, the player name): persists across play. *These* are
    findable by value+stability scans; transient ones are not.
- **Multiple resId globals.** Backgrounds vs. foreground portraits/sprites appear to use different
  resId globals / churn `G[0x62424]` between visible background changes (observed live).
- **Packed process.** Main VM logic runs from a **per-run heap `r-x` region** (~30 MB, nonstable
  base) — so the interpreter can't be hooked at a fixed `AGE.EXE+off`. Archives are streamed via a
  heap block-cache through `ReadFile` (not memory-mapped). See `docs/asset-resolution-re.md`.

**Why the naive scan failed.** The first attempt scanned for a flat `base+addr*4` int32 array using a
signature of `*INIT` constants. It found 0 matches because (a) the layout isn't flat int32, and (b)
`*INIT` entity data lives in structured records. The individual values exist in memory but only
coincidentally adjacent.

---

## What we tried (chronological, with outcomes)

1. **Flat-int32 signature scan** — `tools/frida/find_globals_base.py` (+ `build/globals-signature.json`,
   15,481 distinctive `(addr,value)` pairs from `*INIT` `mov (global-int A) IMM`; 215-dword contiguous
   anchor @`0x631a9`). **Result: 0 hits** at any anchor length, in the opening AND in the first dungeon
   (ruling out load-timing). Diagnostic: the individual values occur (coincidentally); no stride in
   {2,4,8,12,16} reproduces the consecutive-address sequence ⇒ **not flat int32**.
2. **"Lily" (player name) anchor** — searched for the entered name. Found as ASCII `Lily\0`. **But the
   context proved it's the wrong anchor:** every copy is embedded in a **native unit-record** (inline
   string slot + stats + heap pointers), i.e. downstream copies, not the VM string-global store.
3. **Differential value scan on `G[0x62424]`** — `tools/frida/find_global_by_sequence.py`. Self-driven
   by the ReadFile→file_number(=resId) signal: scan for resId at load 1, keep those that become the
   next resId, etc. **Converged to 3 addresses that tracked 35→37→39→43→46 perfectly — but all were
   STACK slots** (region `0x18f000`, self-referential + `0x76xxxxxx` return addresses); their values
   are garbage between loads. They're the per-call argument copies, not the global.
4. **Stability filter** (re-read survivors ~1.4 s later, during the pause) — proved the point: 2084
   locations held resId 35 *stably*, but **0** of them became 37 ⇒ **`G[0x62424]` is transient**, never
   stable at a value, so neither value-scans nor stability-filters can pin it.

---

## Concrete landmarks (for a future run — but note ASLR: these are per-process)

- **Player name** = `global-string 0x279`; default `"リリィ"` (set in `INPUTNAME.BIN` via
  `set-string (global-string 0x279)`, then the input op `0x1aa`/`u00425920` writes the entered name to
  `0x279`, then copied to `local-string 0x135`). **Stable global** — the best future anchor.
- **Native unit records** holding the name: ASCII `Lily\0` in 16-byte-ish slots, followed by int stats
  (level/…, e.g. `0c 0f 04 0f 13 1f`) and native pointers; arrays with stride ~`0x1c`. Region example
  `0x6e9c000 +0x82000` (rw-, no module). These are **native objects**, not the VM store.
- **Transient-arg stack region** where load-arg resId copies appear: ~`0x18f000 +0x11000` (68 KB rw-).
- **Large heap regions** (candidate VM stores): 51.8 MB @ `0x2f64000`, plus ~10/9.6/8/6 MB regions.
- **Unpacked code (packer)**: 30.3 MB **r-x** @ `0x62411000` (main VM logic; nonstable base per run).
- Element size / mapping: **unknown**; it is *not* uniform `base+addr*4` int32.

## Tools built (kept for resumption)

All under `tools/frida/` (see `tools/frida/README.md` and `docs/tools-reference.md`):
- `find_globals_base.py` — `--build-sig` builds `build/globals-signature.json`; scan mode does the
  flat-int32 signature scan (robust prefix ladder + verify). *Currently finds nothing → layout isn't flat.*
- `find_global_by_sequence.py` — differential resId scan with stability filter. *Finds stack proxies;
  transient globals elude it.*
- `capture_load_order.py`, `locate_resource_load.py`, `capture_resid_args.py` — asset-resolution
  captures (context: how the loader/decoder chain was found).

---

## What this does NOT solve (avoid the trap we fell into)

Reading `G[0x62424]` live would **not** reveal the asset-resolution **scope selector** (why resId 37 →
`EV052CA` and not one of the other 8 fn-37 files). The resId is already free from the ReadFile
`file_number`; the scope is *separate native scene state*. So global observation and the scope selector
are independent problems — don't chase globals expecting to crack resolution.

---

## How to resume properly (recommended plan)

Heuristic value-scans only ever find *specific, stable* globals one at a time; they give no general
`address → memory` mapping. A **guaranteed, general** capability needs the **interpreter's
address-resolution logic**. Recommended order:

1. **Anchor on a STABLE global**, not a transient one. Best: `global-string 0x279` (the name). Find its
   *VM-store* copy (distinguish from native unit copies: the VM store won't be wrapped in heap
   pointers). A distinctive name makes the value scan collapse fast.
2. **Find the interpreter's global-access function.** Set a hardware/`MemoryAccessMonitor` watchpoint on
   that stable global's physical location, trigger a bytecode read (e.g. open a menu that draws the
   name), and **backtrace into the heap interpreter**. That function's address computation *is* the
   `bytecode-address → physical` mapping — read it rather than guessing the layout.
3. **Generalize + verify.** Derive the mapping (likely per-store / per-region), build `read_global(addr)`,
   and verify against our VM's known state (e.g. read a stable flag whose value our VM predicts).
4. **Then** build a runtime global-watch tool for VM validation and effectful-op naming.

Alternative bootstraps if watchpoints are awkward: snapshot-diff at **quiescent** points (change a
known stable global via gameplay, diff memory); or a hardware **write** watchpoint on a stable global's
location to catch the interpreter's write path.

**Bottom line for the next session:** the pieces (signature, tools, landmarks, the name anchor) are in
place. Resume from a *stable* anchor and target the *interpreter*, not heuristic scans of transient
globals — and only when runtime observation is actually the priority (it isn't blocking other work).
