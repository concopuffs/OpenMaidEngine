"""Derive the opcode -> real dispatch-handler map from the engine's registration routine.

The AGE interpreter dispatches every opcode through a per-context handler table:

    handler(op) = ctx[0x26c93 + op] = *(ctx + 0x9b24c + op*4)

`FUN_00413860` builds that table: it STOSD-fills 0x400 slots at ctx+0x9b24c with the
default handler 0x4162b0, then overrides specific opcodes with
`MOV dword ptr [ESI + (0x9b24c + op*4)], <handler_va>`. Kelebek's `u00XXXXXX` opcode
names encode handler VAs from *Kelebek's* build, which DRIFT in ours; this map is the
general fix -- it resolves the real handler for every opcode in *our* image.

Input: a text dump of FUN_00413860's disassembly (see build/engine-dump/FUN_00413860.disasm.txt,
produced by the ghidra-mcp `disassemble_function(0x413860)` call).
Output: build/op-handler-map.json  ({ "0x1ac": {"handler": "0x427fb0"}, ... }).

Usage:
    py -3.11 -X utf8 tools/ghidra_handler_map.py <disasm.txt>            # build the map
    py -3.11 -X utf8 tools/ghidra_handler_map.py <disasm.txt> --check    # + cross-check vs opcodes.toml

See docs/engine-re.md (dispatch table) and docs/tools-reference.md.
"""
import json
import re
import sys
from pathlib import Path

import paths

TABLE_BASE = 0x9b24c            # byte offset of ctx[0x26c93] (the op-0 slot)
TABLE_SPAN = 0x400 * 4          # 1024 word slots
DEFAULT_HANDLER = 0x4162b0      # STOSD-seeded default handler (not a real override)

# MOV dword ptr [<reg> + 0x9b...],0x4?????   -- an immediate stored to [base+disp].
_STORE = re.compile(
    r"MOV\s+dword ptr\s+\[\w+\s*\+\s*(0x[0-9a-fA-F]+)\]\s*,\s*(0x[0-9a-fA-F]+)")


def parse_overrides(lines):
    """Return {op: handler_va} for every real override in FUN_00413860's listing.

    Excludes the default-handler fill and any store outside the aligned table window.
    """
    out = {}
    for ln in lines:
        m = _STORE.search(ln)
        if not m:
            continue
        disp = int(m.group(1), 16)
        va = int(m.group(2), 16)
        if not (TABLE_BASE <= disp < TABLE_BASE + TABLE_SPAN):
            continue
        if (disp - TABLE_BASE) % 4 != 0:
            continue
        if va == DEFAULT_HANDLER:
            continue
        out[(disp - TABLE_BASE) // 4] = va
    return out


def build(disasm_path, out_path):
    lines = Path(disasm_path).read_text(encoding="utf-8").splitlines()
    m = parse_overrides(lines)
    data = {hex(op): {"handler": hex(va)} for op, va in sorted(m.items())}
    Path(out_path).write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    return m


# ---- cross-check vs opcodes.toml -------------------------------------------------
# opcodes.toml records handler VAs only in free text (summary/evidence/details/label),
# e.g. "real handler FUN_0042a0b0", "@0x42d360". We extract every module VA mentioned
# per opcode and flag any op whose DERIVED handler is absent from that set (report only).
_VA_HEX = re.compile(r"0x4[0-9a-fA-F]{5}")          # module range 0x400000..0x4fffff
_VA_FUN = re.compile(r"FUN_00(4[0-9a-fA-F]{5})")    # FUN_004xxxxx


def _toml_text_vas(entry):
    """All module VAs mentioned in an opcode entry's text fields."""
    chunks = [str(entry.get("label", ""))]
    sem = entry.get("semantics", {})
    if isinstance(sem, dict):
        for v in sem.values():
            if isinstance(v, str):
                chunks.append(v)
    text = "\n".join(chunks)
    vas = set(int(x, 16) for x in _VA_HEX.findall(text))
    vas |= set(int("0x" + x, 16) for x in _VA_FUN.findall(text))
    return vas


def cross_check(derived):
    """Compare derived handlers against opcodes.toml prose. Returns (mismatches, checked, uncheckable)."""
    import tomllib
    toml = tomllib.loads((paths.VM_MAP / "opcodes.toml").read_text(encoding="utf-8"))
    mismatches, checked, uncheckable = [], 0, 0
    for entry in toml.get("opcode", []):
        op = entry.get("op")
        if op is None or op not in derived:
            continue
        mentioned = _toml_text_vas(entry)
        if not mentioned:
            uncheckable += 1
            continue
        checked += 1
        if derived[op] not in mentioned:
            mismatches.append((op, derived[op], sorted(mentioned)))
    return mismatches, checked, uncheckable


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    disasm = sys.argv[1]
    do_check = "--check" in sys.argv[2:]
    out = paths.BUILD / "op-handler-map.json"
    m = build(disasm, out)
    print(f"{len(m)} dispatch-handler overrides -> {out}")

    anchors = {0x1ac: 0x427fb0, 0x1a2: 0x42d360, 0x215: 0x42a0b0, 0xc8: 0x420ec0}
    bad = [f"0x{op:x}->{hex(m.get(op))} (want {hex(want)})"
           for op, want in anchors.items() if m.get(op) != want]
    if bad:
        print("ANCHOR CHECK FAILED:", "; ".join(bad))
        return 1
    print("anchor check OK:", ", ".join(f"0x{op:x}->{hex(v)}" for op, v in anchors.items()))

    if do_check:
        mismatches, checked, uncheckable = cross_check(m)
        print(f"\ncross-check vs opcodes.toml: {checked} ops checked, "
              f"{uncheckable} without a recorded handler VA (skipped), {len(mismatches)} disagreements")
        for op, va, mentioned in mismatches:
            ms = ", ".join(hex(x) for x in mentioned)
            print(f"  DISAGREE op 0x{op:x}: derived {hex(va)} not among toml VAs [{ms}]")
    return 0


if __name__ == "__main__":
    sys.exit(main())
