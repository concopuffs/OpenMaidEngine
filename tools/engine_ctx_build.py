"""Build the EngineCtx struct artifacts from vm-map/engine-ctx.toml (single source of truth).

    py -3.11 -X utf8 tools/engine_ctx_build.py --build   # -> build/engine-ctx.json + docs/engine-ctx-reference.md
    py -3.11 -X utf8 tools/engine_ctx_build.py --lint     # checks only (overlap / out-of-bounds / dup name / type)

The struct is APPLIED only to the provenance-identified Himegari Ghidra /v2 image via
run_script_inline reading build/engine-ctx.json (see vm-map/engine-ctx.toml). ctx = engine context
(esi / thiscall this);
the VM global bank G[...] is a separate space (vm-map/globals.toml), never added here.
"""
import json
import re
import sys
import tomllib
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
TYPE_SIZES = {"int": 4, "uint": 4, "void*": 4}


def load(toml_text):
    d = tomllib.loads(toml_text)
    fields = [{"offset": int(f["offset"]), "name": f["name"], "type": f["type"], "note": f.get("note", "")}
              for f in d.get("field", [])]
    fields.sort(key=lambda f: f["offset"])
    return {"meta": d["meta"], "fields": fields}


def lint(model):
    errs, size = [], int(model["meta"]["size"])
    meta = model["meta"]
    for key in ("profile_id", "catalog_revision", "script_revision"):
        if not meta.get(key):
            errs.append(f"missing EngineCtx provenance {key}")
    for owner in ("executable", "analysis_image"):
        identity = meta.get(owner, {})
        if not identity:
            errs.append(f"missing EngineCtx provenance {owner}")
            continue
        if int(identity.get("size", 0)) <= 0:
            errs.append(f"{owner} requires a positive size")
        if not re.fullmatch(r"[0-9a-f]{64}", identity.get("sha256", "")):
            errs.append(f"{owner} requires a lowercase SHA-256")
    if not meta.get("executable", {}).get("name"):
        errs.append("executable requires a name")
    if not meta.get("analysis_image", {}).get("ghidra_program"):
        errs.append("analysis_image requires a Ghidra program")
    if int(meta.get("analysis_image", {}).get("image_base", -1)) < 0:
        errs.append("analysis_image requires an image base")
    seen_names, prev = {}, None
    for f in model["fields"]:
        sz = TYPE_SIZES.get(f["type"])
        if sz is None:
            errs.append(f"unknown type {f['type']!r} for {f['name']}")
            sz = 4
        if f["offset"] + sz > size:
            errs.append(f"field {f['name']} @0x{f['offset']:x} out of bounds (size 0x{size:x})")
        if f["name"] in seen_names:
            errs.append(f"duplicate name {f['name']!r}")
        seen_names[f["name"]] = True
        if prev is not None and f["offset"] < prev["end"]:
            errs.append(f"overlap: {f['name']} @0x{f['offset']:x} into {prev['name']} (ends 0x{prev['end']:x})")
        prev = {"name": f["name"], "end": f["offset"] + sz}
    return errs


def emit_json(model):
    return {"meta": model["meta"],
            "fields": {hex(f["offset"]): {"name": f["name"], "type": f["type"]} for f in model["fields"]}}


def emit_reference_md(model):
    meta = model["meta"]
    executable = meta["executable"]
    image = meta["analysis_image"]
    lines = ["# EngineCtx field reference", "",
             "> Generated from `vm-map/engine-ctx.toml` by `tools/engine_ctx_build.py --build`. Do not edit.",
             "", f"Struct `{meta['struct_name']}`, size `0x{int(meta['size']):x}`. "
             f"Scope: profile `{meta['profile_id']}`, catalog `{meta['catalog_revision']}`, "
             f"scripts `{meta['script_revision']}`.", "",
             f"Executable identity: `{executable['name']}`, {int(executable['size'])} bytes, "
             f"SHA-256 `{executable['sha256']}`.", "",
             f"Applied only to Ghidra `{image['ghidra_program']}` at image base "
             f"`0x{int(image['image_base']):x}`; {int(image['size'])} bytes, "
             f"SHA-256 `{image['sha256']}`. Offsets must not be transferred to another executable "
             "without separately matching provenance.", "",
             "| offset | name | type | note |", "|---|---|---|---|"]
    for f in model["fields"]:
        lines.append(f"| `0x{f['offset']:x}` | `{f['name']}` | `{f['type']}` | {f['note']} |")
    return "\n".join(lines) + "\n"


def main():
    text = (REPO / "vm-map" / "engine-ctx.toml").read_text(encoding="utf-8")
    model = load(text)
    errs = lint(model)
    if errs:
        print("LINT ERRORS:")
        for e in errs:
            print("  " + e)
        return 1
    if "--lint" in sys.argv[1:]:
        print(f"lint clean: {len(model['fields'])} fields")
        return 0
    if "--build" in sys.argv[1:]:
        (REPO / "build" / "engine-ctx.json").write_text(
            json.dumps(emit_json(model), indent=2) + "\n", encoding="utf-8")
        (REPO / "docs" / "engine-ctx-reference.md").write_text(emit_reference_md(model), encoding="utf-8")
        print(f"built {len(model['fields'])} fields -> build/engine-ctx.json + docs/engine-ctx-reference.md")
        return 0
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main())
