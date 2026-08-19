#!/usr/bin/env python3
"""Generator + linter for the living opcode reference (vm-map/opcodes.toml).
  --bootstrap  seed skeletons for every used opcode (append-only; preserves hand edits)
  --bootstrap-age  seed compatibility stubs from the canonical broader AGE catalog
  --build      emit age_opcodes.py + age_opcode_semantics.py + JSON/Markdown views
  --lint       run the linter, print errors/warnings, exit nonzero on errors
See docs/superpowers/specs/2026-07-06-opcode-reference-design.md."""
from __future__ import annotations
import os, sys, json, argparse, collections, re
from pathlib import Path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import paths
import sys4load
import opcodes_model as M

TOML_DEFAULT = paths.VM_MAP / "opcodes.toml"

TYPE_NAMES = {0x0: "imm", 0x1: "float", 0x2: "string", 0x3: "g-int", 0x4: "g-float",
              0x5: "g-str", 0x6: "g-ptr", 0x8: "g-str-ptr", 0x9: "l-int", 0xa: "l-float",
              0xb: "l-str", 0xc: "l-ptr", 0xd: "l-float-ptr", 0xe: "l-str-ptr"}

META_TOML = '''# vm-map/opcodes.toml -- CANONICAL living opcode reference (hand-edited).
# Generated artifacts (age_opcode_semantics.py, build/opcodes.json, docs/opcode-reference.md,
# build/opcode-coverage.md) come from this file via tools/opcodes_build.py --build. Do not edit those.
# Skeletons are appended by --bootstrap; enrich each [opcode.semantics] as we investigate.
[meta]
instruction_model = "code = seq of <opcode:u32> then argc*(<argtype:u32><value:u32>); len_dwords = 1 + 2*argc"
profile_ids = []
inline_array_opcode = 0x64

[evidence_defaults.kelebek]
method = "upstream-catalog"
artifact = "Kelebek1/Eushully-Decompiler age-shared.cpp"
scope = "upstream AGE opcode catalog; engine revision applicability unspecified"

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
                  *, catalog_only: bool = False) -> str:
    conf = "med" if _is_named(label) else "low"
    abi_source = "kelebek+corpus-framing" if not catalog_only else "kelebek"
    summary = ("" if not catalog_only else
               "Broader AGE-catalog compatibility stub; the port currently traces and skips it.")
    evidence = ("" if not catalog_only else
                "Not present in a recorded game observation; ABI label/argc come from Kelebek's AGE table.")
    lines = ["[[opcode]]", f"op = 0x{op:x}", f'label = "{label}"', f"argc = {argc}"]
    lines += [f'abi_source = "{abi_source}"', "", "[opcode.semantics]",
             f'name = "{label}"', 'category = "unknown"', f'summary = "{summary}"',
             "noop_headless = false", 'source = "kelebek"', f'confidence = "{conf}"',
             "depends_on = []", f'evidence = "{evidence}"']
    for i in range(argc if not catalog_only else 0):
        tnames = [TYPE_NAMES.get(t, "t%#x" % t) for t in sorted(argtypes_for_op.get(i, ()))]
        obs = ", ".join('"%s"' % n for n in tnames)
        lines += ["", "[[opcode.semantics.args]]", f"i = {i + 1}", 'role = ""',
                  f"observed_types = [{obs}]"]
    return "\n".join(lines) + "\n"

def bootstrap(toml_path: Path, *, corpus_scan=None, catalog_model: M.Model | None = None) -> None:
    """Append observed skeletons from a real scan or an injected synthetic scan fixture."""
    used, argtypes = corpus_scan if corpus_scan is not None else scan_corpus()
    catalog = catalog_model or M.load(TOML_DEFAULT)
    present = set(M.load(toml_path).opcodes) if toml_path.exists() else set()
    blocks = []
    for op in sorted(used):
        if op in present:
            continue
        entry = catalog.opcodes.get(op)
        label, argc = ((entry.label, entry.argc) if entry else ("0x%x" % op, 0))
        blocks.append(skeleton_toml(op, label, argc, argtypes[op]))
    if not toml_path.exists():
        toml_path.parent.mkdir(parents=True, exist_ok=True)
        toml_path.write_text(META_TOML + "\n", encoding="utf-8")
    with toml_path.open("a", encoding="utf-8") as f:
        f.write("\n".join(blocks))
    print(f"bootstrap: {len(used)} used opcodes; appended {len(blocks)} new skeletons -> {toml_path}")

def bootstrap_age(toml_path: Path, *, catalog_model: M.Model | None = None) -> None:
    """Append compatibility stubs for catalog opcodes absent from the canonical map."""
    catalog = catalog_model or M.load(TOML_DEFAULT)
    present = set(M.load(toml_path).opcodes) if toml_path.exists() else set()
    blocks = [
        skeleton_toml(op, entry.label, entry.argc, {}, catalog_only=True)
        for op, entry in sorted(catalog.opcodes.items())
        if op not in present
    ]
    if not toml_path.exists():
        toml_path.parent.mkdir(parents=True, exist_ok=True)
        toml_path.write_text(META_TOML + "\n", encoding="utf-8")
    with toml_path.open("a", encoding="utf-8") as f:
        f.write("\n".join(blocks))
    print(f"bootstrap-age: {len(catalog.opcodes)} catalog opcodes; "
          f"appended {len(blocks)} compatibility stubs -> {toml_path}")

GEN_HEADER = "# DO NOT EDIT -- generated from vm-map/opcodes.toml by tools/opcodes_build.py --build\n"

def emit_runtime_py(model: M.Model) -> str:
    """Emit the Python disassembler/runtime ABI view from the canonical registry."""
    arg_types = {
        int(key, 0): value
        for key, value in model.meta.get("arg_types", {}).items()
    }
    targets = {
        op: tuple(oc.code_target_args)
        for op, oc in model.opcodes.items()
        if oc.code_target_args
    }
    lines = [
        GEN_HEADER,
        '"""AGE opcode framing and operand metadata (generated canonical view)."""',
        "from __future__ import annotations",
        "",
        "# opcode -> (historical/canonical label, argument count)",
        "OPCODES: dict[int, tuple[str, int]] = {",
    ]
    for op, oc in sorted(model.opcodes.items()):
        lines.append(f"    0x{op:04x}: ({oc.label!r}, {oc.argc}),")
    lines += ["}", "", "# argument type tag -> disassembly label", "ARG_TYPES: dict[int, str] = {"]
    for tag, label in sorted(arg_types.items()):
        lines.append(f"    0x{tag:04x}: {label!r},")
    lines += [
        "}",
        "",
        "# One-based operand indices whose raw values are code offsets.",
        "CODE_TARGET_ARGS: dict[int, frozenset[int]] = {",
    ]
    for op, indices in sorted(targets.items()):
        values = ", ".join(str(i) for i in indices)
        if len(indices) == 1:
            values += ","
        lines.append(f"    0x{op:04x}: frozenset(({values})),")
    lines += [
        "}",
        "CONTROL_FLOW = frozenset(CODE_TARGET_ARGS)",
        f"ARRAY_OPCODE = 0x{int(model.meta['inline_array_opcode']):x}",
        "",
        "def is_label_argument(op: int, arg_index: int, raw_value: int) -> bool:",
        '    """Return whether a zero-based operand is a non-fallthrough code target."""',
        "    return (raw_value != 0xFFFFFFFF",
        "            and arg_index + 1 in CODE_TARGET_ARGS.get(op, ()))",
    ]
    return "\n".join(lines) + "\n"

def emit_semantics_py(model: M.Model) -> str:
    lines = [GEN_HEADER,
             '"""Investigated AGE opcode semantics (generated). sys4load reads SEMANTICS[op][\'name\']."""',
             "from __future__ import annotations", "", "SEMANTICS: dict[int, dict] = {"]
    for op, oc in sorted(model.opcodes.items()):
        s = oc.semantics
        if not s or s.name == oc.label:      # only ops we've given a distinct mnemonic
            continue
        lines.append("    0x%x: dict(name=%r, category=%r, noop=%r, confidence=%r, source=%r, summary=%r),"
                     % (op, s.name, s.category, s.noop_headless, s.confidence, s.source, s.summary))
    lines.append("}")
    lines.append("INFERRED = SEMANTICS  # compatibility alias for older external tooling")
    return "\n".join(lines) + "\n"


def _semantic_status(opcode: M.Opcode) -> str:
    semantics = opcode.semantics
    return "investigated" if semantics and semantics.source != "kelebek" else "catalog-only"


def load_runtime_coverage() -> frozenset[int]:
    """Read the engine-owned implementation set without making it opcode-registry source data."""
    source = (
        paths.REPO / "engine" / "Age.Engine" / "Vm" / "OpcodeRuntimeCoverage.cs"
    ).read_text(encoding="utf-8")
    match = re.search(
        r"ImplementedOpcodes\s*=\s*new HashSet<int>\s*\{(?P<body>.*?)\}\s*\.ToFrozenSet",
        source,
        re.DOTALL,
    )
    if not match:
        raise ValueError("could not locate OpcodeRuntimeCoverage.ImplementedOpcodes")
    return frozenset(int(value, 16) for value in re.findall(r"0x[0-9a-fA-F]+", match.group("body")))


def profile_contract_errors(model: M.Model) -> list[str]:
    """Cross-check observation revision ids against the embedded runtime manifests."""
    errors: list[str] = []
    manifests = {}
    for manifest_path in paths.PROFILE_MANIFEST_DIR.glob("*.json"):
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        manifests[manifest["id"]] = manifest
    if set(model.meta.get("profile_ids", [])) != set(manifests):
        errors.append(
            "meta.profile_ids does not match embedded profiles: "
            f"declared={sorted(model.meta.get('profile_ids', []))}, "
            f"runtime={sorted(manifests)}"
        )
    for profile_id, observation in model.observations.items():
        manifest = manifests.get(profile_id)
        if manifest is None:
            continue
        catalog_revisions = {
            identity["catalogRevision"] for identity in manifest["catalogIdentities"]
        }
        if observation.catalog_revision not in catalog_revisions:
            errors.append(
                f"observation[{profile_id}]: catalog revision is absent from profile manifest"
            )
        if observation.script_revision not in manifest["scriptRevisions"]:
            errors.append(
                f"observation[{profile_id}]: script revision is absent from profile manifest"
            )
        if observation.engine_abi_id != manifest["engineAbiId"]:
            errors.append(
                f"observation[{profile_id}]: engine ABI differs from profile manifest"
            )
    return errors


def _evidence_json(evidence: M.Evidence) -> dict:
    return {
        "method": evidence.method,
        "profile_ids": list(evidence.profile_ids),
        "engine_revisions": list(evidence.engine_revisions),
        "artifact": evidence.artifact,
        "site": evidence.site,
        "scope": evidence.scope,
        "confidence": evidence.confidence,
        "text": evidence.text,
    }


def emit_json(model: M.Model, runtime_implemented: frozenset[int] = frozenset()) -> str:
    rev = M.dependents(model)
    out = {"meta": model.meta,
           "observations": [
               {
                   "profile_id": observation.profile_id,
                   "catalog_revision": observation.catalog_revision,
                   "script_revision": observation.script_revision,
                   "engine_abi_id": observation.engine_abi_id,
                   "method": observation.method,
                   "artifact": observation.artifact,
                   "script_count": observation.script_count,
                   "instruction_count": observation.instruction_count,
                   "opcodes": [f"0x{op:x}" for op in sorted(observation.opcodes)],
               }
               for observation in model.observations.values()
           ], "opcodes": [],
           "dependents": {"0x%x" % k: ["0x%x" % d for d in v] for k, v in rev.items() if v}}
    for op, oc in sorted(model.opcodes.items()):
        e = {"op": "0x%x" % op, "label": oc.label, "argc": oc.argc,
             "observed_by": sorted(oc.observed_by),
             "observed_revisions": sorted(oc.observed_revisions),
             "runtime_implemented": op in runtime_implemented,
             "semantic_status": _semantic_status(oc),
             "code_target_args": oc.code_target_args, "abi_source": oc.abi_source,
             "abi_applicability": {
                 "catalog_scope": "upstream AGE catalog; revision applicability unspecified",
                 "proven_by_observation": sorted(oc.observed_revisions),
             }}
        s = oc.semantics
        if s:
            e["semantics"] = {"name": s.name, "category": s.category, "summary": s.summary,
                              "noop_headless": s.noop_headless, "source": s.source,
                              "confidence": s.confidence, "depends_on": ["0x%x" % d for d in s.depends_on],
                              "evidence": [_evidence_json(item) for item in s.evidence],
                              "details": s.details, "confirm_by": s.confirm_by, "args": s.args}
        out["opcodes"].append(e)
    return json.dumps(out, ensure_ascii=False, indent=2) + "\n"

def emit_reference_md(
    model: M.Model, runtime_implemented: frozenset[int] = frozenset()
) -> str:
    rev = M.dependents(model)
    observed_union = {op for op, oc in model.opcodes.items() if oc.observed_by}
    L = ["<!-- DO NOT EDIT -- generated from vm-map/opcodes.toml by tools/opcodes_build.py --build -->",
         "# Opcode Reference (generated)", "",
         f"{len(model.opcodes)} revision-unscoped upstream AGE catalog entries; "
         f"{len(observed_union)} are observed in at least one recorded game corpus. "
         "Source of truth: `vm-map/opcodes.toml`.", "",
         "Observation proves presence in that exact script revision; it does not prove a base SYS4 "
         "definition, revision ancestry, or absence from any unscanned game.", ""]
    for observation in model.observations.values():
        L.append(
            f"- **{observation.profile_id}:** {len(observation.opcodes)} opcodes in "
            f"{observation.script_count} scripts / {observation.instruction_count} instructions; "
            f"catalog `{observation.catalog_revision}`, scripts `{observation.script_revision}`, "
            f"artifact `{observation.artifact}`"
        )
    L.append("")
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
            observations = ", ".join(
                f"{profile} ({model.observations[profile].script_revision})"
                for profile in sorted(oc.observed_by)
            ) or "none in recorded corpora"
            L.append(f"- **observed by:** {observations}")
            L.append(
                "- **ABI applicability:** upstream catalog framing is available; proven revision "
                + (", ".join(f"`{revision}`" for revision in sorted(oc.observed_revisions))
                   if oc.observed_revisions else "membership is not yet established")
            )
            L.append(f"- **semantic status:** {_semantic_status(oc)}")
            L.append(f"- **runtime implemented:** {'yes' if op in runtime_implemented else 'no'}")
            if s:
                L.append(f"- **summary:** {s.summary}" if s.summary else "- **summary:** —")
                L.append(f"- **grounding:** source={s.source}, confidence={s.confidence}"
                         + (f", noop_headless={s.noop_headless}" if s.noop_headless else ""))
                if s.depends_on:
                    L.append("- **depends on:** " + ", ".join("0x%x" % d for d in s.depends_on))
                if rev.get(op):
                    L.append("- **depended on by:** " + ", ".join("0x%x" % d for d in rev[op]))
                for evidence in s.evidence:
                    profiles = ", ".join(evidence.profile_ids) or "none (engine/catalog scoped)"
                    revisions = ", ".join(evidence.engine_revisions) or "unspecified"
                    L.append(
                        f"- **evidence:** method={evidence.method}; confidence={evidence.confidence}; "
                        f"profiles={profiles}; revisions={revisions}; artifact=`{evidence.artifact}`; "
                        f"site=`{evidence.site}`; scope={evidence.scope}. {evidence.text}"
                    )
                if s.details:
                    L += ["", s.details]
            L.append("")
    return "\n".join(L) + "\n"

def emit_coverage_md(
    model: M.Model,
    runtime_implemented: frozenset[int] = frozenset(),
    profile_id: str | None = None,
) -> str:
    if profile_id is None:
        observed_ops = {op for op, oc in model.opcodes.items() if oc.observed_by}
        title = "Opcode Coverage — observed union (generated)"
        scope = "union of: " + ", ".join(sorted(model.observations))
    else:
        observed_ops = set(model.observations[profile_id].opcodes)
        title = f"Opcode Coverage — {profile_id} (generated)"
        scope = (
            f"{model.observations[profile_id].catalog_revision} / "
            f"{model.observations[profile_id].script_revision}"
        )
    by_src = collections.Counter()
    by_conf = collections.Counter()
    by_cat = collections.Counter()
    named = 0
    for op in sorted(observed_ops):
        oc = model.opcodes[op]
        s = oc.semantics
        if s:
            by_src[s.source] += 1
            by_conf[s.confidence] += 1
            by_cat[s.category] += 1
            if s.name != oc.label:
                named += 1
    implemented_observed = observed_ops & runtime_implemented
    unsupported_observed = observed_ops - runtime_implemented
    investigated_observed = {
        op for op in observed_ops if _semantic_status(model.opcodes[op]) == "investigated"
    }
    L = ["<!-- DO NOT EDIT -- generated from vm-map/opcodes.toml -->", f"# {title}", "",
         f"- scope: {scope}",
         f"- AGE catalog opcodes: {len(model.opcodes)}",
         f"- observed opcodes: {len(observed_ops)}",
         f"- catalog entries not observed in this scope: {len(model.opcodes) - len(observed_ops)}",
         f"- observed and semantically investigated: {len(investigated_observed)}",
         f"- observed and runtime implemented: {len(implemented_observed)}",
         f"- observed but runtime unsupported: {len(unsupported_observed)}",
         f"- given a distinct mnemonic: {named}", "",
         "## observed but runtime unsupported", "",
         (", ".join(f"`0x{op:x}`" for op in sorted(unsupported_observed)) or "None."), "",
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
        errors.extend(profile_contract_errors(model))
        for m in warnings:
            print("warn:", m)
        if errors:
            for m in errors:
                print("error:", m)
            return 1
        runtime_implemented = load_runtime_coverage()
        (paths.REPO / "tools" / "age_opcodes.py").write_text(emit_runtime_py(model), encoding="utf-8")
        (paths.REPO / "tools" / "age_opcode_semantics.py").write_text(
            emit_semantics_py(model), encoding="utf-8"
        )
        print("build: wrote tools/age_opcodes.py, tools/age_opcode_semantics.py")
        paths.SHARED_BUILD.mkdir(parents=True, exist_ok=True)
        (paths.SHARED_BUILD / "opcodes.json").write_text(
            emit_json(model, runtime_implemented), encoding="utf-8"
        )
        (paths.REPO / "docs" / "opcode-reference.md").write_text(
            emit_reference_md(model, runtime_implemented), encoding="utf-8"
        )
        (paths.SHARED_BUILD / "opcode-coverage.md").write_text(
            emit_coverage_md(model, runtime_implemented), encoding="utf-8"
        )
        for profile_id in model.observations:
            game_build = paths.ToolContext.resolve(profile_id, environ={}).game_build
            game_build.mkdir(parents=True, exist_ok=True)
            (game_build / "opcode-coverage.md").write_text(
                emit_coverage_md(model, runtime_implemented, profile_id), encoding="utf-8"
            )
        print("build: wrote shared opcode views plus build/games/<profile-id>/opcode-coverage.md")
        return 0
    if args.lint:
        model = M.load(tp)
        errors, warnings = M.lint(model)
        errors.extend(profile_contract_errors(model))
        for m in warnings:
            print("warn:", m)
        for m in errors:
            print("error:", m)
        print(f"lint: {len(errors)} errors, {len(warnings)} warnings")
        return 1 if errors else 0
    ap.error("no action (expected --bootstrap/--bootstrap-age/--build/--lint)")

if __name__ == "__main__":
    sys.exit(main())
