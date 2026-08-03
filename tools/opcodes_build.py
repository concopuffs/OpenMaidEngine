#!/usr/bin/env python3
"""Generator + linter for the living opcode reference (vm-map/opcodes.toml).
  --bootstrap  seed skeletons for every used opcode (append-only; preserves hand edits)
  --bootstrap-age  seed compatibility stubs for every opcode in the broader AGE catalog
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

def skeleton_toml(op: int, label: str, argc: int, argtypes_for_op: dict,
                  *, observed_in_himegari: bool = True) -> str:
    conf = "med" if _is_named(label) else "low"
    abi_source = "kelebek+decode-validated" if observed_in_himegari else "kelebek"
    summary = ("" if observed_in_himegari else
               "Broader AGE-catalog compatibility stub; the port currently traces and skips it.")
    evidence = ("" if observed_in_himegari else
                "Not observed in Himegari's script corpus; ABI label/argc come from Kelebek's AGE table.")
    lines = ["[[opcode]]", f"op = 0x{op:x}", f'label = "{label}"', f"argc = {argc}"]
    if not observed_in_himegari:
        lines.append("observed_in_himegari = false")
    lines += [f'abi_source = "{abi_source}"', "", "[opcode.semantics]",
             f'name = "{label}"', 'category = "unknown"', f'summary = "{summary}"',
             "noop_headless = false", 'source = "kelebek"', f'confidence = "{conf}"',
             "depends_on = []", f'evidence = "{evidence}"']
    for i in range(argc if observed_in_himegari else 0):
        tnames = [TYPE_NAMES.get(t, "t%#x" % t) for t in sorted(argtypes_for_op.get(i, ()))]
        obs = ", ".join('"%s"' % n for n in tnames)
        lines += ["", "[[opcode.semantics.args]]", f"i = {i + 1}", 'role = ""',
                  f"observed_types = [{obs}]"]
    return "\n".join(lines) + "\n"

def bootstrap(toml_path: Path, *, corpus_scan=None) -> None:
    """Append observed skeletons from a real scan or an injected synthetic scan fixture."""
    used, argtypes = corpus_scan if corpus_scan is not None else scan_corpus()
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

def bootstrap_age(toml_path: Path) -> None:
    """Append compatibility stubs for catalog opcodes absent from the canonical map."""
    present = set(M.load(toml_path).opcodes) if toml_path.exists() else set()
    blocks = [
        skeleton_toml(op, label, argc, {}, observed_in_himegari=False)
        for op, (label, argc) in sorted(OPCODES.items())
        if op not in present
    ]
    if not toml_path.exists():
        toml_path.parent.mkdir(parents=True, exist_ok=True)
        toml_path.write_text(META_TOML + "\n", encoding="utf-8")
    with toml_path.open("a", encoding="utf-8") as f:
        f.write("\n".join(blocks))
    print(f"bootstrap-age: {len(OPCODES)} catalog opcodes; "
          f"appended {len(blocks)} compatibility stubs -> {toml_path}")

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

def emit_json(model: M.Model) -> str:
    rev = M.dependents(model)
    out = {"meta": model.meta, "opcodes": [],
           "dependents": {"0x%x" % k: ["0x%x" % d for d in v] for k, v in rev.items() if v}}
    for op, oc in sorted(model.opcodes.items()):
        e = {"op": "0x%x" % op, "label": oc.label, "argc": oc.argc,
             "observed_in_himegari": oc.observed_in_himegari,
             "code_target_args": oc.code_target_args, "abi_source": oc.abi_source}
        s = oc.semantics
        if s:
            e["semantics"] = {"name": s.name, "category": s.category, "summary": s.summary,
                              "noop_headless": s.noop_headless, "source": s.source,
                              "confidence": s.confidence, "depends_on": ["0x%x" % d for d in s.depends_on],
                              "evidence": s.evidence, "details": s.details, "args": s.args}
        out["opcodes"].append(e)
    return json.dumps(out, ensure_ascii=False, indent=2) + "\n"

def emit_reference_md(model: M.Model) -> str:
    rev = M.dependents(model)
    observed = sum(oc.observed_in_himegari for oc in model.opcodes.values())
    catalog_only = len(model.opcodes) - observed
    L = ["<!-- DO NOT EDIT -- generated from vm-map/opcodes.toml by tools/opcodes_build.py --build -->",
         "# Opcode Reference (generated)", "",
         f"{len(model.opcodes)} AGE catalog opcodes: {observed} observed in Himegari and "
         f"{catalog_only} compatibility stubs. Source of truth: `vm-map/opcodes.toml`.", ""]
    by_cat = collections.defaultdict(list)
    for op, oc in model.opcodes.items():
        cat = oc.semantics.category if oc.semantics else "unknown"
        by_cat[cat].append(op)
    for cat in sorted(by_cat):
        L += [f"## {cat}", ""]
        for op in sorted(by_cat[cat]):
            oc = model.opcodes[op]
            s = oc.semantics
            name = s.name if s else oc.label
            L.append(f"### 0x{op:x} `{name}` ({oc.label}, argc {oc.argc})")
            if s:
                L.append(f"- **summary:** {s.summary}" if s.summary else "- **summary:** —")
                L.append(f"- **grounding:** source={s.source}, confidence={s.confidence}"
                         + (f", noop_headless={s.noop_headless}" if s.noop_headless else ""))
                if s.depends_on:
                    L.append("- **depends on:** " + ", ".join("0x%x" % d for d in s.depends_on))
                if rev.get(op):
                    L.append("- **depended on by:** " + ", ".join("0x%x" % d for d in rev[op]))
                if s.evidence:
                    L.append(f"- **evidence:** {s.evidence}")
                if s.details:
                    L += ["", s.details]
            L.append("")
    return "\n".join(L) + "\n"

def emit_coverage_md(model: M.Model) -> str:
    by_src = collections.Counter()
    by_conf = collections.Counter()
    by_cat = collections.Counter()
    named = 0
    for oc in model.opcodes.values():
        s = oc.semantics
        if s:
            by_src[s.source] += 1
            by_conf[s.confidence] += 1
            by_cat[s.category] += 1
            if s.name != oc.label:
                named += 1
    observed = sum(oc.observed_in_himegari for oc in model.opcodes.values())
    L = ["<!-- DO NOT EDIT -- generated from vm-map/opcodes.toml -->", "# Opcode Coverage (generated)", "",
         f"- AGE catalog opcodes: {len(model.opcodes)}",
         f"- observed in Himegari: {observed}",
         f"- compatibility stubs: {len(model.opcodes) - observed}",
         f"- given a distinct mnemonic: {named}", "",
         "## by source", ""]
    L += [f"- {k}: {v}" for k, v in sorted(by_src.items())]
    L += ["", "## by confidence", ""] + [f"- {k}: {by_conf[k]}" for k in ("high", "med", "low")]
    L += ["", "## by category", ""] + [f"- {k}: {v}" for k, v in sorted(by_cat.items())]
    return "\n".join(L) + "\n"

def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--bootstrap", action="store_true")
    ap.add_argument("--bootstrap-age", action="store_true")
    ap.add_argument("--build", action="store_true")
    ap.add_argument("--lint", action="store_true")
    ap.add_argument("--toml", default=str(TOML_DEFAULT))
    args = ap.parse_args(argv)
    tp = Path(args.toml)
    if args.bootstrap:
        bootstrap(tp)
        return 0
    if args.bootstrap_age:
        bootstrap_age(tp)
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
        paths.BUILD.mkdir(parents=True, exist_ok=True)
        (paths.BUILD / "opcodes.json").write_text(emit_json(model), encoding="utf-8")
        (paths.REPO / "docs" / "opcode-reference.md").write_text(emit_reference_md(model), encoding="utf-8")
        (paths.BUILD / "opcode-coverage.md").write_text(emit_coverage_md(model), encoding="utf-8")
        print("build: wrote build/opcodes.json, docs/opcode-reference.md, build/opcode-coverage.md")
        return 0
    if args.lint:
        errors, warnings = M.lint(M.load(tp))
        for m in warnings:
            print("warn:", m)
        for m in errors:
            print("error:", m)
        print(f"lint: {len(errors)} errors, {len(warnings)} warnings")
        return 1 if errors else 0
    ap.error("no action (expected --bootstrap/--bootstrap-age/--build/--lint)")

if __name__ == "__main__":
    sys.exit(main())
