#!/usr/bin/env python3
"""Derive every opcode's operand access contract statically from a profile's unpacked engine dump.

AGE handlers never touch operand dwords directly. Each one asks a small family of VM accessors for
"operand N" -- read its value, read it as a string, write it, bind a reference to it, or take its
storage address. This tool:

1. reads the dispatch-table registration routine (``mov [ctx + table + op*4], handler``) to map
   every opcode to its real handler (no Kelebek VA drift);
2. walks each handler's control flow, plus two levels of callees, and records every accessor call
   with its constant operand index (``push imm`` or a register loaded from an immediate);
3. optionally cross-checks the result against the corpus operand-type census written by
   ``Age.Cli catalog-scan`` (``operand_type_occurrences``).

Accessor identities for the reference profile (Himegari SYS4422) are proven facts recorded below;
another revision's accessors are pinned in ``ENGINES`` after ``--align-accessors`` maps them by
aligning same-opcode handlers against the reference image. No Ghidra session is required.

Usage:
  py -3.11 -X utf8 tools/operand_access.py [--profile ID] [--check-corpus]
  py -3.11 -X utf8 tools/operand_access.py --profile kamidori --align-accessors

Output: build/games/<profile>/operand-access.json
See docs/tools-reference.md ("Native operand access") and docs/engine-re.md.
"""
from __future__ import annotations

import argparse
import collections
import hashlib
import json
from dataclasses import dataclass
from pathlib import Path

import opcodes_model as M
import paths

IMAGE_BASE = 0x400000
TABLE_SLOTS = 0x400
CALL_WINDOW = 16          # instructions searched backwards for one call's argument pushes
WALK_LIMIT = 6000         # instructions per walked function
MAX_DEPTH = 2             # callee levels followed below a handler

# Access kinds are the registry vocabulary (opcodes_model.ACCESS_KINDS); "ignored" is never derived.
ACCESS_KINDS = tuple(kind for kind in M.ACCESS_KINDS if kind != "ignored")

# Operands proven untouched by their handler (no accessor call, handler fully read). The registry
# records them as "ignored"; any other operand without native access is an extraction gap.
VERIFIED_IGNORED = {
    (0x1A7, 1),   # comment: handler only sets the instruction length (SYS4422 0x416630, SYS4433 4.33G 0x416dd0)
}
TYPE_NAMES = {0x0: "imm", 0x1: "float", 0x2: "string", 0x3: "g-int", 0x4: "g-float",
              0x5: "g-str", 0x6: "g-ptr", 0x8: "g-str-ptr", 0x9: "l-int", 0xa: "l-float",
              0xb: "l-str", 0xc: "l-ptr", 0xd: "l-float-ptr", 0xe: "l-str-ptr"}


@dataclass(frozen=True)
class Accessor:
    access: str
    index_arg: int    # 1-based stack argument that carries the operand index
    name: str


@dataclass(frozen=True)
class EngineFacts:
    constructor: int
    table_offset: int
    accessors: dict[int, Accessor]
    legacy_dump: bool = False


# Himegari SYS4422 (/v2 image). Each accessor reads frame_pc (ctx+0x53d2c) and indexes the current
# instruction's (type, value) pairs by its operand-index argument; see docs/engine-re.md.
_HIMEGARI_ACCESSORS = {
    0x41B940: Accessor("read", 1, "vm_operand_fetch"),
    0x418860: Accessor("read-string", 1, "vm_operand_read_string_buffer"),
    0x41B480: Accessor("read-string", 2, "vm_operand_read_std_string"),
    0x41AF90: Accessor("read-string", 1, "vm_operand_format_as_string"),
    0x425FB0: Accessor("write", 1, "vm_operand_write"),
    0x42B5B0: Accessor("write-string", 1, "vm_operand_write_string"),
    0x416180: Accessor("bind", 1, "vm_pointer_operand_bind_element"),
    0x416090: Accessor("bind", 1, "vm_pointer_operand_write"),
    0x425A50: Accessor("address", 1, "vm_operand_resolve_address"),
    0x415FE0: Accessor("string-cell", 1, "vm_operand_global_string_cell"),
    0x415EB0: Accessor("ref-slot", 1, "vm_pointer_operand_slot"),
    0x415F30: Accessor("cell", 1, "vm_operand_lvalue"),
}

_KAMIDORI_ACCESSORS = {
    0x419900: Accessor("read", 1, "vm_operand_fetch"),
    0x419C60: Accessor("read-float", 1, "vm_operand_fetch_float"),
    0x419020: Accessor("read-string", 1, "vm_operand_read_string_buffer"),
    0x425640: Accessor("read-string", 2, "vm_operand_read_std_string"),
    0x419390: Accessor("read-string", 1, "vm_operand_format_as_string"),
    0x4266B0: Accessor("write", 1, "vm_operand_write"),
    0x42C570: Accessor("write-string", 1, "vm_operand_write_string"),
    0x426C00: Accessor("write-float", 1, "vm_operand_write_float"),
    0x416920: Accessor("bind", 1, "vm_pointer_operand_bind_element"),
    0x416830: Accessor("bind", 1, "vm_pointer_operand_write"),
    0x426100: Accessor("address", 1, "vm_operand_resolve_address"),
    0x416780: Accessor("string-cell", 1, "vm_operand_global_string_cell"),
    0x416650: Accessor("ref-slot", 1, "vm_pointer_operand_slot"),
    0x4166D0: Accessor("cell", 1, "vm_operand_lvalue"),
}

REFERENCE_PROFILE = "himegari"
ENGINES: dict[str, EngineFacts] = {
    "himegari": EngineFacts(0x413860, 0x9B24C, _HIMEGARI_ACCESSORS, legacy_dump=True),
    # SYS4433, AGE 4.33G (the clean install's AGE.EXE; docs/engine-re.md, "Kamidori engine builds"): the ctx
    # constructor at 0x413e70 fills ctx+0x9f704; accessors read frame_pc at ctx+0x57fb8. First pinned on the
    # translation's 4.33F (constructor 0x413e50, table ctx+0x9f700) from --align-accessors (every alignment
    # unanimous, 2026-10-06), except the SYS4433-only float accessors, identified from their bodies (fild/float
    # store). Moved to 4.33G on 2026-11-08 by aligning same-opcode handlers of the two builds (unanimous), and the
    # derived contracts are the same for both builds.
    "kamidori": EngineFacts(0x413E70, 0x9F704, _KAMIDORI_ACCESSORS),
}


def dump_path(profile_id: str) -> Path:
    candidates = [paths.SHARED_BUILD / "games" / profile_id / "engine-dump" / "range_00400000.bin"]
    if ENGINES[profile_id].legacy_dump:
        candidates.append(paths.SHARED_BUILD / "engine-dump" / "range_00400000.bin")
    for candidate in candidates:
        if candidate.is_file():
            return candidate
    raise FileNotFoundError(
        f"{profile_id}: no unpacked engine dump; run tools/frida/dump_engine.py "
        f"(looked in {', '.join(str(c) for c in candidates)})")


class Image:
    """Capstone view over one flat dump based at IMAGE_BASE."""

    def __init__(self, data: bytes):
        import capstone
        from capstone import x86
        self.x86 = x86
        self.data = data
        self.md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
        self.md.detail = True
        self._insns: dict = {}
        self._walks: dict = {}

    def contains(self, va: int) -> bool:
        return IMAGE_BASE < va < IMAGE_BASE + len(self.data)

    def insn(self, va: int):
        if va not in self._insns:
            offset = va - IMAGE_BASE
            self._insns[va] = next(self.md.disasm(self.data[offset:offset + 16], va), None)
        return self._insns[va]

    def body(self, entry: int) -> "Body":
        if entry not in self._walks:
            self._walks[entry] = Body(self, self.walk(entry))
        return self._walks[entry]

    def walk(self, entry: int) -> list:
        """Recursive descent over one function body: follow branches, not calls; stop at ret."""
        seen: dict = {}
        work = [entry]
        while work and len(seen) < WALK_LIMIT:
            va = work.pop()
            while va not in seen and self.contains(va):
                i = self.insn(va)
                if i is None:
                    break
                seen[va] = i
                if i.mnemonic in ("ret", "int3"):
                    break
                if i.mnemonic.startswith("j"):
                    target = i.operands[0]
                    if target.type == self.x86.X86_OP_IMM:
                        work.append(target.imm)
                    if i.mnemonic == "jmp":
                        break
                va = self.next_address(i)
        return [seen[key] for key in sorted(seen)]

    def call_target(self, i) -> int | None:
        if i.mnemonic != "call" or i.operands[0].type != self.x86.X86_OP_IMM:
            return None
        return i.operands[0].imm

    def next_address(self, i) -> int:
        """Where execution continues after i when it does not branch.

        A packed image (Kamidori's AGE 4.33G) calls each Windows API through `call rel32` into a stub outside the
        module, followed by one junk byte that the stub skips on return (docs/engine-re.md, "Kamidori engine
        builds"). Decoding resumes past that byte.
        """
        target = self.call_target(i)
        junk = 1 if target is not None and not self.contains(target) else 0
        return i.address + i.size + junk


class Body:
    """One walked function in address order, with branch predecessors for backward scans."""

    def __init__(self, image: Image, insns: list):
        self.image = image
        self.insns = insns
        self.preds: dict[int, list[int]] = collections.defaultdict(list)
        for k, i in enumerate(insns):
            if i.mnemonic.startswith("j") and i.operands[0].type == image.x86.X86_OP_IMM:
                self.preds[i.operands[0].imm].append(k)

    def previous(self, k: int) -> int | None:
        """The instruction executed before k on the fall-through path, else its first branch source.

        MSVC hoists argument pushes above conditional branches, so a call that begins a branch
        target can take its arguments from the block that branched to it.
        """
        i = self.insns[k]
        if k > 0:
            prior = self.insns[k - 1]
            falls_through = (self.image.next_address(prior) == i.address
                             and prior.mnemonic not in ("jmp", "ret", "int3"))
            if falls_through:
                return k - 1
        sources = self.preds.get(i.address)
        return sources[0] if sources else None

    def backwards(self, k: int):
        """Instructions before k along one predecessor path, nearest first, within CALL_WINDOW."""
        for _ in range(CALL_WINDOW):
            k = self.previous(k)
            if k is None:
                return
            yield k


def register_value(image: Image, body: Body, k: int, reg: int) -> int | None:
    """Immediate held by ``reg`` at instruction k, from the nearest earlier write on its path."""
    x86 = image.x86
    for j in body.backwards(k):
        i = body.insns[j]
        if i.mnemonic == "call":
            return None
        _, written = i.regs_access()
        if reg not in written:
            continue
        ops = i.operands
        if i.mnemonic == "mov" and ops[1].type == x86.X86_OP_IMM:
            return ops[1].imm
        if i.mnemonic == "xor" and ops[0].type == ops[1].type == x86.X86_OP_REG and ops[0].reg == ops[1].reg:
            return 0
        return None
    return None


def stack_arguments(image: Image, body: Body, k: int) -> list[int | None]:
    """Arguments pushed for the call at k, first stack argument first (None = not constant)."""
    x86 = image.x86
    args: list[int | None] = []
    for j in body.backwards(k):
        i = body.insns[j]
        if i.mnemonic in ("call", "ret"):
            break
        if i.mnemonic != "push":
            continue
        op = i.operands[0]
        if op.type == x86.X86_OP_IMM:
            args.append(op.imm & 0xFFFFFFFF)
        elif op.type == x86.X86_OP_REG:
            args.append(register_value(image, body, j, op.reg))
        else:
            args.append(None)
    return args


def dispatch_table(image: Image, facts: EngineFacts) -> dict[int, int]:
    """Opcode -> handler from the constructor's immediate stores into the dispatch table."""
    x86 = image.x86
    table: dict[int, int] = {}
    default = None
    insns = image.walk(facts.constructor)
    for k, i in enumerate(insns):
        if i.mnemonic in ("rep stosd",) and k:
            for j in range(k - 1, max(-1, k - 8), -1):
                prior = insns[j]
                if (prior.mnemonic == "mov" and prior.operands[0].type == x86.X86_OP_REG
                        and prior.operands[0].reg == x86.X86_REG_EAX
                        and prior.operands[1].type == x86.X86_OP_IMM):
                    default = prior.operands[1].imm
                    break
        if i.mnemonic != "mov" or len(i.operands) != 2:
            continue
        dst, src = i.operands
        if dst.type != x86.X86_OP_MEM or src.type != x86.X86_OP_IMM:
            continue
        offset = dst.mem.disp - facts.table_offset
        if 0 <= offset < TABLE_SLOTS * 4 and offset % 4 == 0 and image.contains(src.imm):
            table[offset // 4] = src.imm
    return {op: handler for op, handler in table.items() if handler != default}


def accesses(image: Image, accessors: dict[int, Accessor], entry: int,
             depth: int = 0, visiting: frozenset = frozenset()) -> list[dict]:
    """Every accessor call reachable from ``entry`` (up to MAX_DEPTH callee levels)."""
    found: list[dict] = []
    body = image.body(entry)
    insns = body.insns
    for k, i in enumerate(insns):
        target = image.call_target(i)
        if target is None:
            continue
        accessor = accessors.get(target)
        if accessor is not None:
            args = stack_arguments(image, body, k)
            index = args[accessor.index_arg - 1] if len(args) >= accessor.index_arg else None
            found.append({"arg": index, "access": accessor.access, "accessor": accessor.name,
                          "site": f"0x{i.address:x}", "depth": depth,
                          "via": None if depth == 0 else f"0x{entry:x}"})
        elif depth < MAX_DEPTH and image.contains(target) and target not in visiting:
            found += accesses(image, accessors, target, depth + 1, visiting | {entry})
    return found


def contracts(found: list[dict], argc: int | None) -> tuple[dict[int, list[str]], list[dict]]:
    """Fold accessor calls into {operand index: sorted access kinds} plus unresolved calls."""
    by_arg: dict[int, set[str]] = collections.defaultdict(set)
    unresolved = []
    for record in found:
        index = record["arg"]
        if isinstance(index, int) and 1 <= index <= (argc if argc is not None else 64):
            by_arg[index].add(record["access"])
        else:
            unresolved.append(record)
    return ({index: sorted(kinds, key=ACCESS_KINDS.index) for index, kinds in sorted(by_arg.items())},
            unresolved)


def load_opcode_argc() -> dict[int, int]:
    data = json.loads((paths.SHARED_BUILD / "opcodes.json").read_text(encoding="utf-8"))
    return {int(entry["op"], 16): int(entry["argc"]) for entry in data["opcodes"]}


def corpus_census(profile_id: str) -> dict[int, list[dict[int, int]]]:
    path = paths.SHARED_BUILD / "games" / profile_id / "catalog-opcode-coverage.json"
    report = json.loads(path.read_text(encoding="utf-8"))
    census = report.get("operand_type_occurrences")
    if census is None:
        raise SystemExit(f"{path}: no operand_type_occurrences; rerun Age.Cli catalog-scan")
    return {int(op, 16): [{int(t, 16): n for t, n in position.items()} for position in positions]
            for op, positions in census.items()}


IMMEDIATE_TYPES = {0x0, 0x1, 0x2}           # integer, float, inline string literal
STORE_KINDS = {"write", "write-string", "write-float", "bind", "address", "string-cell",
               "ref-slot", "cell"}


def corpus_findings(opcodes: dict[int, dict], census: dict[int, list[dict[int, int]]]) -> dict:
    """Contradictions and gaps between native access and observed operand types."""
    literal_stores, silent = [], []
    for op, positions in sorted(census.items()):
        record = opcodes.get(op)
        if record is None:
            continue
        for index, types in enumerate(positions, start=1):
            kinds = set(record["args"].get(str(index), []))
            literal = sum(n for t, n in types.items() if t in IMMEDIATE_TYPES)
            if kinds and kinds <= STORE_KINDS and literal:
                literal_stores.append({"op": f"0x{op:x}", "arg": index, "kinds": sorted(kinds),
                                       "literal_occurrences": literal})
            if not kinds:
                silent.append({"op": f"0x{op:x}", "arg": index,
                               "types": {f"0x{t:x}": n for t, n in types.items()}})
    return {"literal_operand_in_store_position": literal_stores,
            "operand_without_native_access": silent}


def align_accessors(profile_id: str) -> dict[int, collections.Counter]:
    """Map a revision's accessor VAs by aligning its handlers with the reference image's."""
    ref_facts, facts = ENGINES[REFERENCE_PROFILE], ENGINES[profile_id]
    ref = Image(dump_path(REFERENCE_PROFILE).read_bytes())
    image = Image(dump_path(profile_id).read_bytes())
    ref_table, table = dispatch_table(ref, ref_facts), dispatch_table(image, facts)

    def calls(img: Image, entry: int) -> list[tuple[int, tuple]]:
        body = img.body(entry)
        insns = body.insns
        return [(img.call_target(i), tuple(stack_arguments(img, body, k)[:2]))
                for k, i in enumerate(insns) if img.call_target(i) is not None]

    votes: dict[int, collections.Counter] = collections.defaultdict(collections.Counter)
    for op in sorted(set(ref_table) & set(table)):
        ref_calls, calls_here = calls(ref, ref_table[op]), calls(image, table[op])
        if len(ref_calls) != len(calls_here):
            continue
        for (ref_target, ref_args), (target, args) in zip(ref_calls, calls_here):
            accessor = ref_facts.accessors.get(ref_target)
            if accessor is not None and ref_args == args:
                votes[target][accessor.name] += 1
    return votes


def registry_contracts(model: M.Model, reports: dict[str, dict]) -> dict[int, dict[int, dict[str, list]]]:
    """{op: {operand: {abi: kinds}}} for every observed opcode, from per-profile reports."""
    plan: dict[int, dict[int, dict[str, list]]] = {}
    missing = []
    for op, oc in sorted(model.opcodes.items()):
        if not oc.observed_by or oc.argc == 0:
            continue
        per_arg: dict[int, dict[str, list]] = collections.defaultdict(dict)
        for profile_id, report in sorted(reports.items()):
            abi = model.observations[profile_id].engine_abi_id
            record = report["opcodes"].get(f"0x{op:x}")
            if record is None:
                if profile_id in oc.observed_by:
                    missing.append(f"0x{op:x}: no {abi} handler")
                continue
            for index in range(1, oc.argc + 1):
                kinds = record["args"].get(str(index))
                if kinds is None and (op, index) in VERIFIED_IGNORED:
                    kinds = ["ignored"]
                if kinds is None:
                    missing.append(f"0x{op:x} operand {index}: no {abi} native access")
                    continue
                per_arg[index][abi] = list(kinds)
        plan[op] = dict(per_arg)
    if missing:
        raise SystemExit("cannot apply; resolve these first:\n  " + "\n  ".join(missing))
    return plan


def _render_access(by_abi: dict[str, list]) -> str:
    def kinds(values: list) -> str:
        return "[" + ", ".join(f'"{value}"' for value in values) + "]"
    distinct = {tuple(values) for values in by_abi.values()}
    if len(distinct) == 1:
        return f"access = {kinds(next(iter(by_abi.values())))}"
    pairs = ", ".join(f"{abi} = {kinds(values)}" for abi, values in sorted(by_abi.items()))
    return f"access_by_abi = {{ {pairs} }}"


def apply_to_registry(text: str, plan: dict[int, dict[int, dict[str, list]]],
                      census: dict[int, dict[int, set[int]]]) -> str:
    """Rewrite access lines in opcodes.toml text; append operand entries an opcode lacks.

    Only `access`/`access_by_abi` lines inside `[[opcode.semantics.args]]` blocks are owned by
    this function; every other line is preserved byte-for-byte.
    """
    lines = text.split("\n")
    out: list[str] = []
    op = None
    arg_index = None
    seen_args: dict[int, set[int]] = collections.defaultdict(set)
    pending_insert: tuple[int, int] | None = None   # (op, operand) whose access line is still due

    def flush_record_tail():
        """Append operand entries the finished opcode record lacks."""
        if op is None or op not in plan:
            return
        absent = sorted(set(plan[op]) - seen_args[op])
        if not absent:
            return
        while out and out[-1] == "":
            out.pop()
        for index in absent:
            types = ", ".join(f'"{TYPE_NAMES.get(t, f"t{t:#x}")}"'
                              for t in sorted(census.get(op, {}).get(index, ())))
            out.extend(["", "[[opcode.semantics.args]]", f"i = {index}", 'role = ""',
                        f"observed_types = [{types}]", _render_access(plan[op][index])])
        out.append("")

    def emit_due():
        nonlocal pending_insert
        if pending_insert is not None:
            owner, index = pending_insert
            if index in plan.get(owner, {}):
                out.append(_render_access(plan[owner][index]))
            pending_insert = None

    for line in lines:
        stripped = line.strip()
        if stripped.startswith("[") and stripped != "[[opcode.semantics.args]]":
            emit_due()
            arg_index = None
            if stripped == "[[opcode]]" or stripped == "[[observation]]" or stripped == "[[abi_layer]]":
                flush_record_tail()
                op = None
        elif stripped == "[[opcode.semantics.args]]":
            emit_due()
        if stripped == "[[opcode.semantics.args]]":
            arg_index = 0
            out.append(line)
            continue
        if op is None and stripped.startswith("op = ") and arg_index is None:
            op = int(stripped.split("=", 1)[1].strip(), 0)
        if arg_index == 0 and stripped.startswith("i = "):
            arg_index = int(stripped.split("=", 1)[1].strip())
            seen_args[op].add(arg_index)
            pending_insert = (op, arg_index)
            out.append(line)
            continue
        if arg_index and (stripped.startswith("access = ") or stripped.startswith("access_by_abi = ")):
            continue
        if arg_index and stripped == "":
            emit_due()
            arg_index = None
        out.append(line)
    emit_due()
    flush_record_tail()
    return "\n".join(out)


def build(profile_id: str) -> dict:
    facts = ENGINES[profile_id]
    if not facts.accessors:
        raise SystemExit(f"{profile_id}: accessors not pinned; run --align-accessors and record them")
    path = dump_path(profile_id)
    data = path.read_bytes()
    image = Image(data)
    argc = load_opcode_argc()
    table = dispatch_table(image, facts)
    opcodes = {}
    for op, handler in sorted(table.items()):
        found = accesses(image, facts.accessors, handler)
        by_arg, unresolved = contracts(found, argc.get(op))
        opcodes[op] = {
            "handler": f"0x{handler:x}",
            "argc": argc.get(op),
            "args": {str(index): kinds for index, kinds in by_arg.items()},
            "unresolved_calls": unresolved,
            "calls": found,
        }
    return {
        "generated_by": "tools/operand_access.py",
        "profile_id": profile_id,
        "engine_dump": str(path.relative_to(paths.REPO)).replace("\\", "/"),
        "engine_dump_sha256": hashlib.sha256(data).hexdigest(),
        "constructor": f"0x{facts.constructor:x}",
        "table_offset": f"0x{facts.table_offset:x}",
        "accessors": {f"0x{va:x}": {"access": a.access, "index_arg": a.index_arg, "name": a.name}
                      for va, a in sorted(facts.accessors.items())},
        "handler_count": len(table),
        "opcodes": {f"0x{op:x}": record for op, record in opcodes.items()},
    }


def apply_main() -> int:
    toml_path = paths.VM_MAP / "opcodes.toml"
    model = M.load(toml_path)
    reports, census = {}, collections.defaultdict(lambda: collections.defaultdict(set))
    for profile_id in model.observations:
        report_path = paths.SHARED_BUILD / "games" / profile_id / "operand-access.json"
        if not report_path.is_file():
            raise SystemExit(f"{report_path}: missing; run operand_access.py --profile {profile_id}")
        reports[profile_id] = json.loads(report_path.read_text(encoding="utf-8"))
        for op, positions in corpus_census(profile_id).items():
            for index, types in enumerate(positions, start=1):
                census[op][index].update(types)
    plan = registry_contracts(model, reports)
    text = toml_path.read_text(encoding="utf-8")
    updated = apply_to_registry(text, plan, census)
    toml_path.write_bytes(updated.encode("utf-8"))   # keep the registry LF on Windows
    errors, _ = M.lint(M.load(toml_path))
    access_errors = [error for error in errors if "access" in error or "operand" in error]
    differing = sum(1 for args in plan.values() for by_abi in args.values()
                    if len({tuple(v) for v in by_abi.values()}) > 1)
    print(f"apply: {len(plan)} observed opcodes, "
          f"{sum(len(args) for args in plan.values())} operands ({differing} revision-specific) "
          f"-> {toml_path}; access lint errors={len(access_errors)}")
    for error in access_errors[:20]:
        print("  error:", error)
    return 1 if access_errors else 0


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--check-corpus", action="store_true",
                        help="cross-check against the catalog-scan operand census")
    parser.add_argument("--align-accessors", action="store_true",
                        help="propose this revision's accessor VAs by aligning with the reference image")
    parser.add_argument("--apply", action="store_true",
                        help="write access contracts from every profile's report into vm-map/opcodes.toml")
    parser.add_argument("--output", type=Path, default=None)
    args = parser.parse_args(argv)
    if args.apply:
        return apply_main()
    profile_id = paths.CONTEXT.profile_id
    if profile_id not in ENGINES:
        raise SystemExit(f"{profile_id}: no engine facts recorded in tools/operand_access.py")

    if args.align_accessors:
        for va, names in sorted(align_accessors(profile_id).items()):
            total = sum(names.values())
            name, count = names.most_common(1)[0]
            print(f"0x{va:x}  {name:36s} {count}/{total} aligned handler calls")
        return 0

    report = build(profile_id)
    if args.check_corpus:
        census = corpus_census(profile_id)
        used = {int(op, 16): rec for op, rec in report["opcodes"].items() if int(op, 16) in census}
        report["corpus_findings"] = corpus_findings(used, census)
        report["corpus_unmapped_opcodes"] = sorted(
            f"0x{op:x}" for op in census if f"0x{op:x}" not in report["opcodes"])
    output = args.output or paths.SHARED_BUILD / "games" / profile_id / "operand-access.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    unresolved = sum(len(r["unresolved_calls"]) for r in report["opcodes"].values())
    print(f"operand-access: profile={profile_id} handlers={report['handler_count']} "
          f"unresolved_calls={unresolved} -> {output}")
    if args.check_corpus:
        findings = report["corpus_findings"]
        print(f"  corpus: literal-in-store={len(findings['literal_operand_in_store_position'])} "
              f"no-native-access={len(findings['operand_without_native_access'])} "
              f"unmapped-opcodes={report['corpus_unmapped_opcodes']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
