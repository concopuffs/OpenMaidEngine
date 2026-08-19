#!/usr/bin/env py -3.11 -X utf8
"""Per-scene opcode coverage / completeness tracker.

Answers "how complete SHOULD this scene be?" by histogramming the opcodes a
scene's bytecode actually contains and classifying each against what the C# VM
implements. A half-rendered scene is far less alarming once we can see it still
has effectful ops sitting un-implemented (a GAP) versus everything being handled.

Three tiers per opcode:
  impl       - the VM has an explicit handler arm (real effect OR deliberate
               no-op like set-font/comment). Correct treatment.
  safe-noop  - no VM arm, but opcodes.toml marks it noop_headless (statement /
               block markers, 0x258/0x259/0x1f4...). Skipping it is correct.
  GAP        - no VM arm AND not a safe no-op -> an effectful op the VM silently
               skips (default stub, pc+1). THESE are the completeness holes.

Single sources of truth (no hand-maintained duplicate list):
  - implemented labels  <- parsed from engine/Age.Engine/Vm/VirtualMachine.cs
                           (every `case "label":` arm)
  - opcode metadata     <- build/opcodes.json  (label/name/category/noop_headless)
  - scene opcodes       <- sys4load.load(<SCENE>.BIN).instructions (canonical loader)

Usage:
  py -3.11 -X utf8 tools/scene_opcode_coverage.py                 # SC0000 (default)
  py -3.11 -X utf8 tools/scene_opcode_coverage.py SC0240 SP0062   # specific scenes
  py -3.11 -X utf8 tools/scene_opcode_coverage.py --build         # regen the SC0000 report doc

Writes build/scene-opcode-coverage/<SCENE>.md and prints a summary to stdout.
"""
from __future__ import annotations
import sys
import re
import json
import collections
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import paths
import sys4load

VM_SRC   = paths.REPO / "engine" / "Age.Engine" / "Vm" / "VirtualMachine.cs"
OPCODES  = paths.SHARED_BUILD / "opcodes.json"
CALLNAME = paths.BUILD / "callscript-names.json"
OUT_DIR  = paths.BUILD / "scene-opcode-coverage"


def implemented_labels() -> set[str]:
    """Every opcode label the VM dispatches with an explicit `case` arm."""
    src = VM_SRC.read_text(encoding="utf8")
    return set(re.findall(r'case\s+"([^"]+)"\s*:', src))


def opcode_meta() -> dict[int, dict]:
    """op(int) -> {label,name,category,noop_headless,confidence} from opcodes.json."""
    doc = json.loads(OPCODES.read_text(encoding="utf8"))
    out: dict[int, dict] = {}
    for e in doc["opcodes"]:
        op = int(e["op"], 16)
        sem = e.get("semantics", {})
        out[op] = {
            "label": e.get("label", f"op_{op:#x}"),
            "name": sem.get("name", ""),
            "category": sem.get("category", "unknown"),
            "noop_headless": bool(sem.get("noop_headless", False)),
            "confidence": sem.get("confidence", "low"),
            "summary": sem.get("summary", ""),
        }
    return out


def callscript_names() -> dict[int, str]:
    if not CALLNAME.exists():
        return {}
    doc = json.loads(CALLNAME.read_text(encoding="utf8"))
    # file maps str(id) -> name
    return {int(k): v for k, v in doc.items()}


def classify(op: int, meta: dict, impl: set[str]) -> str:
    label = meta.get(op, {}).get("label", "")
    if label in impl:
        return "impl"
    if meta.get(op, {}).get("noop_headless", False):
        return "safe-noop"
    return "GAP"


def analyze(scene: str, meta: dict[int, dict], impl: set[str], cnames: dict[int, str]):
    scripts = paths.scripts()
    key = scene.upper()
    if not key.endswith(".BIN"):
        key += ".BIN"
    if key not in scripts:
        raise SystemExit(f"scene not found in corpus: {key}")
    scr = sys4load.load(scripts[key])

    hist = collections.Counter()
    callscript_ids = collections.Counter()
    for ins in scr.instructions:
        hist[ins.opcode] += 1
        m = meta.get(ins.opcode, {})
        if m.get("label") == "call-script" and ins.args:
            a0 = ins.args[0]
            # only resolve immediate ids (arg type 0); dynamic ids left unresolved
            if getattr(a0, "type", None) == 0:
                callscript_ids[a0.value] += 1

    rows = []
    for op, n in hist.items():
        m = meta.get(op, {"label": f"op_{op:#x}", "name": "", "category": "unknown",
                          "confidence": "low", "summary": ""})
        rows.append({
            "op": op, "count": n, "label": m["label"], "name": m["name"],
            "category": m["category"], "confidence": m["confidence"],
            "summary": m["summary"], "status": classify(op, meta, impl),
        })

    total_ins = sum(hist.values())
    by_status = collections.Counter(r["status"] for r in rows)
    ins_by_status = collections.Counter()
    for r in rows:
        ins_by_status[r["status"]] += r["count"]

    return {
        "scene": key, "rows": rows, "total_ins": total_ins,
        "distinct": len(rows), "by_status": by_status,
        "ins_by_status": ins_by_status, "callscript_ids": callscript_ids,
        "cnames": cnames,
    }


STATUS_ORDER = {"GAP": 0, "impl": 1, "safe-noop": 2}


def render_md(a: dict) -> str:
    L = []
    L.append(f"# Opcode coverage — {a['scene']}")
    L.append("")
    L.append("> Generated by `tools/scene_opcode_coverage.py` — do NOT hand-edit.")
    L.append("> Static histogram of the scene's bytecode vs the C# VM's implemented ops.")
    L.append("")
    d, t = a["distinct"], a["total_ins"]
    bs, ib = a["by_status"], a["ins_by_status"]

    def pct(x, whole):
        return f"{100.0*x/whole:.1f}%" if whole else "—"

    L.append("## Summary")
    L.append("")
    L.append(f"- **{d} distinct opcodes**, {t} instructions total.")
    L.append(f"- **impl**: {bs['impl']} ops ({pct(bs['impl'], d)}) / "
             f"{ib['impl']} instrs ({pct(ib['impl'], t)}) — VM has a handler.")
    L.append(f"- **safe-noop**: {bs['safe-noop']} ops ({pct(bs['safe-noop'], d)}) / "
             f"{ib['safe-noop']} instrs ({pct(ib['safe-noop'], t)}) — markers, correct to skip.")
    L.append(f"- **GAP**: {bs['GAP']} ops ({pct(bs['GAP'], d)}) / "
             f"{ib['GAP']} instrs ({pct(ib['GAP'], t)}) — effectful, silently stubbed.")
    handled = bs["impl"] + bs["safe-noop"]
    L.append(f"- **Correctly handled (impl + safe-noop): {handled}/{d} ops "
             f"= {pct(handled, d)}**; remaining {bs['GAP']} are the completeness holes below.")
    L.append("")

    def table(title, rows):
        L.append(f"## {title}")
        L.append("")
        if not rows:
            L.append("_(none)_")
            L.append("")
            return
        L.append("| op | count | label | name | category | conf | summary |")
        L.append("|----|------:|-------|------|----------|------|---------|")
        for r in rows:
            summ = r["summary"].replace("|", "\\|")
            L.append(f"| `{r['op']:#x}` | {r['count']} | `{r['label']}` | "
                     f"{r['name']} | {r['category']} | {r['confidence']} | "
                     f"{summ} |")
        L.append("")

    rows = sorted(a["rows"], key=lambda r: (STATUS_ORDER[r["status"]], -r["count"]))
    gaps = [r for r in rows if r["status"] == "GAP"]
    impl = [r for r in rows if r["status"] == "impl"]
    noop = [r for r in rows if r["status"] == "safe-noop"]

    table("GAP — effectful ops the VM silently skips (the completeness holes)", gaps)
    table("impl — handled by the VM", impl)
    table("safe-noop — markers / structural (correct to skip headless)", noop)

    if a["callscript_ids"]:
        L.append("## call-script targets (immediate ids in this scene)")
        L.append("")
        L.append("Subroutines this scene invokes statically — their own opcodes are NOT")
        L.append("counted above (run the tracker on them too for full-depth coverage).")
        L.append("")
        L.append("| id | name | sites |")
        L.append("|----|------|------:|")
        for cid, n in sorted(a["callscript_ids"].items(), key=lambda kv: -kv[1]):
            nm = a["cnames"].get(cid, "?")
            L.append(f"| `{cid:#x}` | {nm} | {n} |")
        L.append("")

    return "\n".join(L)


def print_summary(a: dict):
    bs, ib, d, t = a["by_status"], a["ins_by_status"], a["distinct"], a["total_ins"]
    handled = bs["impl"] + bs["safe-noop"]
    print(f"\n{a['scene']}: {d} distinct opcodes, {t} instructions")
    print(f"  impl      {bs['impl']:3d} ops / {ib['impl']:6d} instrs")
    print(f"  safe-noop {bs['safe-noop']:3d} ops / {ib['safe-noop']:6d} instrs")
    print(f"  GAP       {bs['GAP']:3d} ops / {ib['GAP']:6d} instrs   <- effectful, stubbed")
    print(f"  handled   {handled}/{d} ops ({100.0*handled/d:.1f}%)")
    if bs["GAP"]:
        gaps = sorted((r for r in a["rows"] if r["status"] == "GAP"),
                      key=lambda r: -r["count"])
        print("  GAP ops:", ", ".join(
            f"{r['label']}({r['op']:#x})×{r['count']}" for r in gaps))


def main(argv: list[str]):
    scenes = [x for x in argv if not x.startswith("--")]
    if not scenes:
        scenes = ["SC0000"]
    meta = opcode_meta()
    impl = implemented_labels()
    cnames = callscript_names()
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for scene in scenes:
        a = analyze(scene, meta, impl, cnames)
        out = OUT_DIR / f"{a['scene'].replace('.BIN', '')}.md"
        out.write_text(render_md(a), encoding="utf8")
        print_summary(a)
        print(f"  -> {out.relative_to(paths.REPO)}")


if __name__ == "__main__":
    main(sys.argv[1:])
