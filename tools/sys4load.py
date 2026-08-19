#!/usr/bin/env python3
"""sys4load — loader / disassembler-ish dumper for Eushully SYS4 `.BIN` scripts.

Parses the confirmed container format (see ../sys4-format-notes.md):
  * 60-byte header: magic "SYS4422 " + 13 little-endian u32 fields
  * body (dword stream) = CODE + three 1-dword pointer tables + inline strings
  * strings: XOR-0xFF cp932, NUL-terminated, referenced by a `0x02 <dword-off>` pair

The container format is byte-verified across all 481 DATA1 scripts. Opcodes are now
DECODED using the canonical AGE opcode registry (vm-map/opcodes.toml, generated into
age_opcodes.py and validated 476/476 clean on Himegari): the code section is a flat stream
of `<opcode:u32> + argc*(<argtype:u32><value:u32>)` instructions, length 1+2*argc dwords.
Inline strings live after the code inside [0,F8), so decoding stops at the first string
(type-2) or array (op 0x64) operand offset.

Usage:
  sys4load.py <file.BIN>                full disassembly listing
  sys4load.py <file.BIN> --summary      header + section sizes + table/string counts
  sys4load.py <file.BIN> --strings      decoded string pool only
  sys4load.py <file.BIN> --json         machine-readable structure (no code dump)
  sys4load.py <dir>      --validate      re-check format invariants across a folder
"""
from __future__ import annotations

import argparse
import json
import os
import struct
import sys
from dataclasses import dataclass, field
from pathlib import Path

import paths

try:
    from age_opcodes import (OPCODES, ARG_TYPES, CONTROL_FLOW, ARRAY_OPCODE,
                             is_label_argument)
except ImportError:  # allow import from another cwd
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from age_opcodes import (OPCODES, ARG_TYPES, CONTROL_FLOW, ARRAY_OPCODE,
                             is_label_argument)

# Investigated semantic layer (optional): improves labels for unnamed opcodes from the
# canonical generated registry; observation scope remains available in opcodes.json.
try:
    from age_opcode_semantics import SEMANTICS
except ImportError:
    SEMANTICS = {}

# Global-variable labels (optional): annotate global operands from the merged registry
# build/globals.json (curated vm-map/globals.toml over the auto shape map), produced by
# tools/globals_build.py --build. Curated entries show their name+category; the auto tail
# keeps only high/med confidence to stay readable. Degrades to {} if the file is absent.
GLOBAL_ATYPES = {3, 4, 5, 6, 8}          # global-int/float/string/ptr/string-ptr


def _short_global_label(lbl: str) -> str:
    if lbl.startswith("record-table[stride "):
        return "rec[s" + lbl[len("record-table[stride "):-1] + "]"
    if lbl.startswith("string-table (written by "):
        return "str<" + lbl[len("string-table (written by "):-1] + ">"
    return lbl


def _load_global_labels() -> dict:
    try:
        p = paths.BUILD / "globals.json"
        data = json.loads(p.read_text(encoding="utf-8"))
    except Exception:
        return {}
    out = {}
    for addr_s, e in data.get("globals", {}).items():
        addr = int(addr_s, 16)
        if e.get("provenance") == "curated" and e.get("name"):
            cat = e.get("category")
            out[addr] = f"{e['name']}({cat})" if cat and cat != "unknown" else e["name"]
        elif e.get("usage") and e.get("confidence") in ("high", "med"):
            out[addr] = _short_global_label(e["usage"])
    return out


GLOBAL_LABELS = _load_global_labels()


def _load_callscript_names() -> dict:
    """id -> script name. `call-script <id>` (op 0x03) is a raw index into the SYS4INI file
    table; build/callscript-names.json maps every id to its script name (see docs/engine-re.md)."""
    try:
        p = paths.BUILD / "callscript-names.json"
        data = json.loads(p.read_text(encoding="utf-8"))
    except Exception:
        return {}
    return {int(k): v for k, v in data.items()}


CALLSCRIPT_NAMES = _load_callscript_names()
CALLSCRIPT_OP = 0x03


def display_label(op: int) -> str:
    """Rendered mnemonic: canonical ABI label, else the inferred name, else u00…."""
    lbl = OPCODES.get(op, (f"?{op:x}", 0))[0]
    is_unnamed = (lbl.startswith(("u00", "dev_ukn")) or lbl.lower() == f"{op:x}")
    if is_unnamed and op in SEMANTICS:
        return SEMANTICS[op]["name"]
    return lbl

MAGIC = b"SYS4422 "         # canonical; also seen: SYS4424 (patch scripts). Both 0x3C headers.
MAGIC_PREFIX = b"SYS4"      # accept the whole SYS4 script family (SYS4422 / SYS4424 / ...)
HEADER_SIZE = 0x3C          # 8 magic + 13*4
NUM_FIELDS = 13
BODY_OFF = HEADER_SIZE

# Tag dword found at the target of each pointer table (100% pure across corpus).
TABLE_TAGS = {"T1": 0x71, "T2": 0x03, "T3": 0x8F}
STRING_REF_TAG = 0x02       # dword preceding an inline string offset


class Sys4Error(ValueError):
    pass


@dataclass
class Instruction:
    offset: int             # dword index of the opcode within the body
    opcode: int
    label: str | None       # mnemonic from the opcode table, or None if unknown
    args: list              # list of (type, value) operand pairs
    unknown: bool = False   # opcode not in the table (decode desynced/stopped)
    truncated: bool = False # not enough dwords left for the declared arg count

    @property
    def size(self):         # length in dwords
        return 1 + 2 * len(self.args)


@dataclass
class Sys4Script:
    path: Path
    fields: tuple           # 13 header u32s (F0..F12)
    dwords: tuple           # body as tuple[int], length = nbody
    magic: str = "SYS4422 " # actual 8-byte magic (SYS4422 / SYS4424 / ...)
    strings: dict = field(default_factory=dict)   # start_dword -> (text, ndwords)
    string_refs: dict = field(default_factory=dict)  # value-operand dword index -> string start
    instructions: list = field(default_factory=list)  # decoded Instruction list
    code_end: int = 0       # dword where code stops and inline strings begin (<= code_len)

    # ---- section geometry (all in dword units, relative to body start) ----
    @property
    def nbody(self):
        return len(self.dwords)

    @property
    def code_len(self):
        return self.fields[8]                       # F8

    @property
    def t1(self):
        return (self.fields[7], self.fields[8], self.fields[10])   # count, off, end

    @property
    def t2(self):
        return (self.fields[9], self.fields[10], self.fields[12])

    @property
    def t3(self):
        return (self.fields[11], self.fields[12], self.nbody)

    def table_entries(self, which):
        count, off, _ = getattr(self, which.lower())
        return self.dwords[off:off + count]

    # ---- byte offsets for reporting ----
    @staticmethod
    def dword_to_file_off(idx):
        return BODY_OFF + idx * 4


# --------------------------------------------------------------------------- #
# parsing
# --------------------------------------------------------------------------- #
def _decode_string(dwords, start, limit=4096):
    """Decode a XOR-0xFF cp932 string beginning at dword `start`.

    Returns (text, ndwords) or (None, 0) if it isn't a clean string.
    """
    raw = bytearray()
    n = len(dwords)
    end = min(start + limit, n)
    for j in range(start, end):
        raw += struct.pack("<I", dwords[j] ^ 0xFFFFFFFF)
        if 0 in raw[-4:]:
            break
    else:
        return None, 0
    s = bytes(raw).split(b"\0")[0]
    if len(s) < 1:
        return "", 1
    # strict cp932 shape: ascii-printable or valid 2-byte lead+trail
    i, chars = 0, 0
    while i < len(s):
        b = s[i]
        if 0x20 <= b <= 0x7E:
            i += 1
            chars += 1
        elif 0x81 <= b <= 0x9F or 0xE0 <= b <= 0xEA:
            if i + 1 < len(s) and 0x40 <= s[i + 1] <= 0xFC and s[i + 1] != 0x7F:
                i += 2
                chars += 1
            else:
                return None, 0
        else:
            return None, 0
    if chars < 1:
        return None, 0
    try:
        text = s.decode("cp932")
    except UnicodeDecodeError:
        return None, 0
    ndwords = (len(bytes(raw).split(b"\0")[0]) // 4) + 1  # include the NUL dword
    return text, ndwords


def load_bytes(data: bytes, name: str | Path = "<memory>.BIN") -> Sys4Script:
    """Parse one SYS4 script payload already read from an archive or other byte source."""
    path = Path(name)
    if len(data) < HEADER_SIZE:
        raise Sys4Error(f"{path.name}: too small ({len(data)} bytes)")
    if data[:4] != MAGIC_PREFIX:
        raise Sys4Error(f"{path.name}: bad magic {data[:8]!r}")
    if len(data) % 4:
        raise Sys4Error(f"{path.name}: length {len(data)} not dword-aligned")
    fields = struct.unpack_from(f"<{NUM_FIELDS}I", data, 8)
    nbody = (len(data) - BODY_OFF) // 4
    dwords = struct.unpack_from(f"<{nbody}I", data, BODY_OFF)

    scr = Sys4Script(path=path, fields=fields, dwords=dwords,
                     magic=data[:8].decode("ascii", "replace"))
    _check_invariants(scr)
    decode_code(scr)
    return scr


def load(path) -> Sys4Script:
    path = Path(path)
    return load_bytes(path.read_bytes(), path)


def decode_code(scr: Sys4Script):
    """Walk the code section into instructions, resolving inline strings.

    Model (age_opcodes.py): instruction = <opcode> + argc*(<type><value>), length
    1+2*argc dwords. Inline strings sit after the code inside [0,F8); a type-2 (string)
    or op-0x64 array operand offset marks where code ends, so we lower `code_end` to the
    smallest such offset seen and stop there. Populates scr.instructions / .strings /
    .string_refs / .code_end.
    """
    dw = scr.dwords
    nbody = scr.nbody
    code_end = scr.code_len                 # F8; shrinks to first inline-string/array offset
    instrs, strings, refs = [], {}, {}
    i = 0
    while i < code_end:
        op = dw[i]
        info = OPCODES.get(op)
        if info is None:
            instrs.append(Instruction(i, op, None, [], unknown=True))
            break
        label, argc = info
        base = i + 1
        if base + 2 * argc > code_end:
            instrs.append(Instruction(i, op, label, [], truncated=True))
            break
        args = []
        for a in range(argc):
            atype = dw[base + 2 * a]
            aval = dw[base + 2 * a + 1]
            args.append((atype, aval))
            if atype == 2 and 0 <= aval < nbody:        # inline string operand
                code_end = min(code_end, aval)
                if aval not in strings:
                    text, nd = _decode_string(dw, aval)
                    if text is not None:
                        strings[aval] = (text, nd)
                refs[base + 2 * a + 1] = aval
            elif op == ARRAY_OPCODE and a == 1 and 0 <= aval < nbody:  # footer array ref
                code_end = min(code_end, aval)
        instrs.append(Instruction(i, op, label, args))
        i = base + 2 * argc

    scr.instructions = instrs
    scr.strings = strings
    scr.string_refs = refs
    scr.code_end = code_end
    return scr


def _check_invariants(scr: Sys4Script):
    f = scr.fields
    nbody = scr.nbody
    F7, F8, F9, F10, F11, F12 = f[7], f[8], f[9], f[10], f[11], f[12]
    problems = []
    if f[6] != 0x1C:
        problems.append(f"F6={f[6]:#x} != 0x1C")
    if not (0 <= F8 <= F10 <= F12 <= nbody):
        problems.append(f"section ordering 0<=F8({F8})<=F10({F10})<=F12({F12})<=nbody({nbody})")
    else:
        if F10 - F8 != F7:
            problems.append(f"T1 size {(F10 - F8)} != count {F7}")
        if F12 - F10 != F9:
            problems.append(f"T2 size {(F12 - F10)} != count {F9}")
        if nbody - F12 != F11:
            problems.append(f"T3 size {(nbody - F12)} != count {F11}")
    if problems:
        raise Sys4Error(f"{scr.path.name}: " + "; ".join(problems))


def check_table_tags(scr: Sys4Script):
    """Return dict which -> (ok_count, total) for target-tag purity."""
    out = {}
    for which, tag in TABLE_TAGS.items():
        entries = scr.table_entries(which)
        ok = sum(1 for e in entries if 0 <= e < scr.nbody and scr.dwords[e] == tag)
        out[which] = (ok, len(entries))
    return out


# --------------------------------------------------------------------------- #
# rendering
# --------------------------------------------------------------------------- #
def _fmt_dwords(dwords, lo, hi, per_line=8):
    out = []
    for i in range(lo, hi, per_line):
        chunk = dwords[i:min(i + per_line, hi)]
        out.append("    " + " ".join(f"{v:08x}" for v in chunk))
    return out


def decode_stats(scr: Sys4Script):
    """(n_instructions, n_unknown, n_truncated, clean) for the decoded code."""
    n = len(scr.instructions)
    unk = sum(1 for ins in scr.instructions if ins.unknown)
    trunc = sum(1 for ins in scr.instructions if ins.truncated)
    clean = unk == 0 and trunc == 0 and scr.code_end == (
        scr.instructions[-1].offset + scr.instructions[-1].size if scr.instructions else 0)
    return n, unk, trunc, clean


def render_summary(scr: Sys4Script) -> str:
    f = scr.fields
    tags = check_table_tags(scr)
    n, unk, trunc, clean = decode_stats(scr)
    lines = [
        f"file        {scr.path.name}",
        f"body        {scr.nbody} dwords ({scr.nbody * 4} bytes)",
        f"header      F0={f[0]:#x} F1={f[1]} F2={f[2]:#x} F3={f[3]:#x} "
        f"F4={f[4]} F5={f[5]:#x} F6={f[6]:#x}   (F0-F5 = local var counts)",
        f"code        [0x00000 .. 0x{scr.code_end:05x})  {scr.code_end} dwords, "
        f"{n} instructions"
        + (f", strings 0x{scr.code_end:05x}..0x{scr.code_len:05x}" if scr.code_end < scr.code_len else ""),
        f"decode      {'CLEAN' if clean else 'INCOMPLETE'}"
        + (f"  ({unk} unknown, {trunc} truncated)" if (unk or trunc) else ""),
        f"table T1    off 0x{f[8]:05x}  count {f[7]:<6} tag 0x71  "
        f"purity {tags['T1'][0]}/{tags['T1'][1]}",
        f"table T2    off 0x{f[10]:05x}  count {f[9]:<6} tag 0x03  "
        f"purity {tags['T2'][0]}/{tags['T2'][1]}",
        f"table T3    off 0x{f[12]:05x}  count {f[11]:<6} tag 0x8f  "
        f"purity {tags['T3'][0]}/{tags['T3'][1]}",
        f"strings     {len(scr.strings)} inline, {len(scr.string_refs)} references",
    ]
    return "\n".join(lines)


def render_strings(scr: Sys4Script) -> str:
    out = []
    for off in sorted(scr.strings):
        text, nd = scr.strings[off]
        fo = scr.dword_to_file_off(off)
        out.append(f"  [0x{off:05x} @file 0x{fo:06x}] ({nd}dw) {text!r}")
    return "\n".join(out) if out else "  (no inline strings)"


def _fmt_operand(op: int, arg_index: int, atype: int, aval: int, strings: dict) -> str:
    """Render one (type, value) operand the way the disassembler labels them."""
    if atype == 2:                                  # inline string
        text = strings.get(aval, ("?",))[0]
        return f'"{text}"'
    if is_label_argument(op, arg_index, aval):      # code-offset jump/call target
        return f"label_{aval:x}"
    tlabel = ARG_TYPES.get(atype)
    if atype == 0 or tlabel is None:                # immediate / unknown-tag: raw value
        if atype == 0:
            if op == CALLSCRIPT_OP and aval in CALLSCRIPT_NAMES:
                return f"{aval:#x} ={CALLSCRIPT_NAMES[aval]}"   # call-script target script name
            return f"{aval:#x}"
        return f"<t{atype:#x} {aval:#x}>"
    if tlabel == "float":
        return f"(float {aval:#x})"
    ann = ""
    if atype in GLOBAL_ATYPES and aval in GLOBAL_LABELS:
        ann = f" ={GLOBAL_LABELS[aval]}"             # inferred global-var-map alias
    return f"({tlabel} {aval:#x}{ann})"              # e.g. (global-int 17a =skill-name-table)


def render_listing(scr: Sys4Script) -> str:
    """Disassembly of the code section using the AGE opcode table."""
    out = [render_summary(scr), "", "; ---- CODE ----"]

    # collect jump/call label targets so we can print label_XXXX: anchors
    label_targets = set()
    for ins in scr.instructions:
        for x, (atype, aval) in enumerate(ins.args):
            if is_label_argument(ins.opcode, x, aval):
                label_targets.add(aval)

    for ins in scr.instructions:
        if ins.offset in label_targets:
            out.append(f"label_{ins.offset:x}:")
        if ins.unknown:
            out.append(f"  0x{ins.offset:05x}:  ??? 0x{ins.opcode:x}   ; unknown opcode — decode stopped")
            continue
        if ins.truncated:
            out.append(f"  0x{ins.offset:05x}:  {ins.label} <truncated>")
            continue
        ops = " ".join(_fmt_operand(ins.opcode, x, t, v, scr.strings)
                       for x, (t, v) in enumerate(ins.args))
        mnem = display_label(ins.opcode)          # prefers canonical label, else inferred, else u00…
        # annotate unnamed/inferred ops with their raw value for grep-ability
        raw_label = ins.label
        unnamed = raw_label.startswith(("u00", "dev_ukn")) or raw_label[:1].isdigit()
        raw = f"   ; op 0x{ins.opcode:x}" + (" investigated" if unnamed and ins.opcode in SEMANTICS else "") \
            if unnamed else ""
        out.append(f"  0x{ins.offset:05x}:  {mnem}{(' ' + ops) if ops else ''}{raw}")

    out.append("")
    out.append("; ---- TABLES ----")
    for which in ("T1", "T2", "T3"):
        count, off, end = getattr(scr, which.lower())
        entries = scr.table_entries(which)
        preview = " ".join(f"{e:x}" for e in entries[:16])
        more = " ..." if count > 16 else ""
        out.append(f"{which} (count {count}, off 0x{off:05x}): {preview}{more}")
    out.append("")
    out.append("; ---- STRINGS ----")
    out.append(render_strings(scr))
    return "\n".join(out)


def to_dict(scr: Sys4Script, with_code=False) -> dict:
    f = scr.fields
    n, unk, trunc, clean = decode_stats(scr)
    d = {
        "file": scr.path.name,
        "magic": scr.magic,
        "fields": {f"F{i}": f[i] for i in range(NUM_FIELDS)},
        "body_dwords": scr.nbody,
        "sections": {
            "code": {"off": 0, "len": scr.code_len, "code_end": scr.code_end},
            "T1": {"count": f[7], "off": f[8], "tag": 0x71},
            "T2": {"count": f[9], "off": f[10], "tag": 0x03},
            "T3": {"count": f[11], "off": f[12], "tag": 0x8F},
        },
        "decode": {"instructions": n, "unknown": unk, "truncated": trunc, "clean": clean},
        "table_tag_purity": {k: v for k, v in check_table_tags(scr).items()},
        "strings": {f"0x{off:x}": txt for off, (txt, _) in sorted(scr.strings.items())},
    }
    if with_code:
        d["code"] = [
            {"off": ins.offset, "op": f"0x{ins.opcode:x}", "label": ins.label,
             "args": [[f"0x{t:x}", f"0x{v:x}"] for t, v in ins.args]}
            for ins in scr.instructions
        ]
    return d


# --------------------------------------------------------------------------- #
# validation pass over a directory
# --------------------------------------------------------------------------- #
def validate_dir(root: Path) -> int:
    files = sorted(root.glob("*.BIN"))
    if not files:
        print(f"no .BIN files in {root}")
        return 1
    ok, bad = 0, 0
    tag_fail = 0
    decode_clean = 0
    decode_dirty = []
    for p in files:
        try:
            scr = load(p)
        except Sys4Error as e:
            print(f"  FAIL {e}")
            bad += 1
            continue
        purity = check_table_tags(scr)
        impure = [k for k, (o, t) in purity.items() if o != t]
        if impure:
            tag_fail += 1
            print(f"  TAG  {p.name}: impure tables {impure} {purity}")
        _, unk, trunc, clean = decode_stats(scr)
        if clean:
            decode_clean += 1
        else:
            decode_dirty.append((p.name, unk, trunc))
        ok += 1
    print(f"\n{len(files)} files: {ok} parsed clean, {bad} header/section failures, "
          f"{tag_fail} with impure table tags")
    print(f"opcode decode: {decode_clean}/{ok} fully clean (0 unknown/truncated)")
    for name, unk, trunc in decode_dirty[:20]:
        print(f"  DECODE {name}: {unk} unknown, {trunc} truncated")
    if len(decode_dirty) > 20:
        print(f"  ... and {len(decode_dirty) - 20} more")
    return 0 if bad == 0 and tag_fail == 0 else 2


# --------------------------------------------------------------------------- #
def main(argv=None):
    ap = argparse.ArgumentParser(description="SYS4 .BIN loader / dumper")
    ap.add_argument("target", help="a .BIN file, or a directory with --validate")
    g = ap.add_mutually_exclusive_group()
    g.add_argument("--summary", action="store_true", help="header + section sizes only")
    g.add_argument("--strings", action="store_true", help="decoded string pool only")
    g.add_argument("--json", action="store_true", help="machine-readable structure")
    g.add_argument("--validate", action="store_true", help="check invariants over a folder")
    args = ap.parse_args(argv)

    target = Path(args.target)
    if args.validate or target.is_dir():
        return validate_dir(target)

    try:
        scr = load(target)
    except Sys4Error as e:
        print(f"error: {e}", file=sys.stderr)
        return 1

    if args.summary:
        print(render_summary(scr))
    elif args.strings:
        print(render_strings(scr))
    elif args.json:
        print(json.dumps(to_dict(scr), ensure_ascii=False, indent=2))
    else:
        print(render_listing(scr))
    return 0


if __name__ == "__main__":
    sys.exit(main())
