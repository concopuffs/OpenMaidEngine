#!/usr/bin/env python3
"""Generator + linter for the living opcode reference (vm-map/opcodes.toml).
  --bootstrap  seed skeletons for every used opcode (append-only; preserves hand edits)
  --build      emit age_opcodes_himegari.py + build/opcodes.json + docs/opcode-reference.md + build/opcode-coverage.md
  --lint       run the linter, print errors/warnings, exit nonzero on errors
See docs/superpowers/specs/2026-07-06-opcode-reference-design.md."""
from __future__ import annotations
import os, sys, json, argparse, collections
from pathlib import Path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import paths
import sys4load
import opcodes_model as M
from age_opcodes import OPCODES

TOML_DEFAULT = paths.VM_MAP / "opcodes.toml"

TYPE_NAMES = {0x0: "imm", 0x1: "float", 0x2: "string", 0x3: "g-int", 0x4: "g-float",
              0x5: "g-str", 0x6: "g-ptr", 0x8: "g-str-ptr", 0x9: "l-int", 0xa: "l-float",
              0xb: "l-str", 0xc: "l-ptr", 0xd: "l-float-ptr", 0xe: "l-str-ptr"}

META_TOML = '''# vm-map/opcodes.toml -- CANONICAL living opcode reference (hand-edited).
# Generated artifacts (age_opcodes_himegari.py, build/opcodes.json, docs/opcode-reference.md,
# build/opcode-coverage.md) come from this file via tools/opcodes_build.py --build. Do not edit those.
# Skeletons are appended by --bootstrap; enrich each [opcode.semantics] as we investigate.
[meta]
instruction_model = "code = seq of <opcode:u32> then argc*(<argtype:u32><value:u32>); len_dwords = 1 + 2*argc"
opcodes_used_by_himegari = 248

[meta.arg_types]
"0x0" = "immediate"
"0x1" = "float"
"0x2" = "string"
"0x3" = "global-int"
"0x4" = "global-float"
"0x5" = "global-string"
"0x6" = "global-ptr"
"0x8" = "global-string-ptr"
"0x9" = "local-int"
"0xa" = "local-float"
"0xb" = "local-string"
"0xc" = "local-ptr"
"0xd" = "local-float-ptr"
"0xe" = "local-string-ptr"

[meta.header_fields]
"F0" = "local_integer_1"
"F1" = "local_floats"
"F2" = "local_strings_1"
"F3" = "local_integer_2"
"F4" = "unknown_data"
"F5" = "local_strings_2"
"F6" = "sub_header_length(=0x1C)"
"F7" = "table_1_length"
"F8" = "table_1_offset(=code end)"
"F9" = "table_2_length"
"F10" = "table_2_offset"
"F11" = "table_3_length"
"F12" = "table_3_offset"
'''

def scan_corpus():
    """used[op] = count; argtypes[op][arg_index] = set(type-codes) across the corpus."""
    used = collections.Counter()
    argtypes: dict[int, dict[int, set]] = collections.defaultdict(lambda: collections.defaultdict(set))
    for name, path in paths.scripts().items():
        try:
            scr = sys4load.load(path)
        except Exception:
            continue
        for ins in scr.instructions:
            used[ins.opcode] += 1
            for i, (t, v) in enumerate(ins.args):
                argtypes[ins.opcode][i].add(t)
    return used, argtypes

def _is_named(label: str) -> bool:
    return not (label.startswith("u00") or label == "dev_ukn" or label.startswith("?"))

def skeleton_toml(op: int, label: str, argc: int, argtypes_for_op: dict) -> str:
    conf = "med" if _is_named(label) else "low"
    lines = ["[[opcode]]", f"op = 0x{op:x}", f'label = "{label}"', f"argc = {argc}",
             'abi_source = "kelebek+decode-validated"', "", "[opcode.semantics]",
             f'name = "{label}"', 'category = "unknown"', 'summary = ""',
             "noop_headless = false", 'source = "kelebek"', f'confidence = "{conf}"',
             "depends_on = []", 'evidence = ""']
    for i in range(argc):
        tnames = [TYPE_NAMES.get(t, "t%#x" % t) for t in sorted(argtypes_for_op.get(i, ()))]
        obs = ", ".join('"%s"' % n for n in tnames)
        lines += ["", "[[opcode.semantics.args]]", f"i = {i + 1}", 'role = ""',
                  f"observed_types = [{obs}]"]
    return "\n".join(lines) + "\n"

def bootstrap(toml_path: Path) -> None:
    used, argtypes = scan_corpus()
    present = set(M.load(toml_path).opcodes) if toml_path.exists() else set()
    blocks = []
    for op in sorted(used):
        if op in present:
            continue
        label, argc = OPCODES.get(op, ("0x%x" % op, 0))
        blocks.append(skeleton_toml(op, label, argc, argtypes[op]))
    if not toml_path.exists():
        toml_path.parent.mkdir(parents=True, exist_ok=True)
        toml_path.write_text(META_TOML + "\n", encoding="utf-8")
    with toml_path.open("a", encoding="utf-8") as f:
        f.write("\n".join(blocks))
    print(f"bootstrap: {len(used)} used opcodes; appended {len(blocks)} new skeletons -> {toml_path}")

GEN_HEADER = "# DO NOT EDIT -- generated from vm-map/opcodes.toml by tools/opcodes_build.py --build\n"

def emit_inferred_py(model: M.Model) -> str:
    lines = [GEN_HEADER,
             '"""Inferred Himegari opcode semantics (generated). sys4load reads INFERRED[op][\'name\']."""',
             "from __future__ import annotations", "", "INFERRED: dict[int, dict] = {"]
    for op, oc in sorted(model.opcodes.items()):
        s = oc.semantics
        if not s or s.name == oc.label:      # only ops we've given a distinct mnemonic
            continue
        lines.append("    0x%x: dict(name=%r, category=%r, noop=%r, confidence=%r, source=%r, summary=%r),"
                     % (op, s.name, s.category, s.noop_headless, s.confidence, s.source, s.summary))
    lines.append("}")
    return "\n".join(lines) + "\n"

def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--bootstrap", action="store_true")
    ap.add_argument("--build", action="store_true")
    ap.add_argument("--lint", action="store_true")
    ap.add_argument("--toml", default=str(TOML_DEFAULT))
    args = ap.parse_args(argv)
    tp = Path(args.toml)
    if args.bootstrap:
        bootstrap(tp)
        return 0
    if args.build:
        model = M.load(tp)
        errors, warnings = M.lint(model)
        for m in warnings:
            print("warn:", m)
        if errors:
            for m in errors:
                print("error:", m)
            return 1
        (paths.REPO / "tools" / "age_opcodes_himegari.py").write_text(emit_inferred_py(model), encoding="utf-8")
        print("build: wrote tools/age_opcodes_himegari.py")
        return 0
    if args.lint:
        errors, warnings = M.lint(M.load(tp))
        for m in warnings:
            print("warn:", m)
        for m in errors:
            print("error:", m)
        print(f"lint: {len(errors)} errors, {len(warnings)} warnings")
        return 1 if errors else 0
    ap.error("no action (expected --bootstrap/--build/--lint)")

if __name__ == "__main__":
    sys.exit(main())
