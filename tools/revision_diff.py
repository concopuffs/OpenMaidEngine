#!/usr/bin/env python3
"""Compare the native code behind every opcode two engine builds share.

Each engine dump has its own dispatch table, so opcode N pairs exactly with opcode N. What an opcode does usually
lives a call or two below its handler (op 0x215's SYS4433 bit-0 test is in its worker), and builds differ in their
compiler setup and data layout: Himegari's AGE 4.22 calls checked-iterator asserts and out-of-line container
helpers that Kamidori's 4.33 inlines, 4.38D does not align functions, and records and the context grow between
versions. Comparing instruction text would flag nearly every opcode. Instead:

1. Correspondence. Same-opcode handlers are paired, then their instruction streams are aligned; inside aligned
   runs, each pair of fields, globals and callees casts a vote, and callees are aligned in turn. The majority
   decides how the builds' fields, globals and functions correspond. A context offset that was never learned moves
   with its learned neighbors when both moved alike (layouts move in blocks).
2. Facts. Each function is reduced to a multiset of behavior facts: operand accesses (kind and index), constants by
   role (``cmp`` compared, ``bit`` tested or masked, ``const`` stored or passed), divisions and modulos by constant,
   context fields, globals, read-only strings, float constants, virtual-call slots and calls to shared helpers.
   Compiler and layout idioms become facts of their own kinds, which are weak: object fields, container element
   counts and max_size checks, record copy sizes, shift counts, pointer strides, zero-extension masks, std::string's
   small-buffer checks, the C++ library's exception messages and Windows API calls. Operand accessors, import
   thunks and the statically linked C runtime are leaves; a tail jump to another function is a call; the stack
   cookie and stack slots are not facts.
3. Trees. An opcode's facts are those of its handler and ``--depth`` callee levels, flattened, so inlining does not
   matter. A function that many opcode trees reach (``HELPER_FANIN``) is a shared helper: it is compared once, on
   its own, and inside opcode trees only the call to it counts.
4. Verdicts. The reference build's facts are translated into the other build's terms and the multisets compared.
   Weak facts, count changes of uncounted kinds, a constant that only changed role, leftover unlearned fields on both
   sides, facts the other build has just past the depth limit or inside a helper (``INLINE_SLACK``) and calls to a
   function the other build inlined do not count. ``same``: no difference. ``minor``: only those differ.
   ``differs``: a strong fact differs; the report lists each, where it occurs, and which paired function holds it.

``same`` is strong evidence that the code behaves alike for the same state. ``differs`` is a candidate to read, not
a finding: rows whose only strong fact is one context field or one helper call (scores up to about 6) are usually
correspondence gaps. Code no opcode reaches (frame loop, renderer, input) is compared only where a helper covers it.

Usage:
  py -3.11 -X utf8 tools/revision_diff.py REFERENCE OTHER [--depth 3] [--op 0x215 ...] [--output PATH]
  e.g. revision_diff.py himegari kamidori;  revision_diff.py kamidori-433f kamidori;  revision_diff.py kamidori arterial

Engine ids are tools/operand_access.py ENGINES keys. Output: build/revision-diff/<reference>-<other>.json and .md.
See docs/tools-reference.md ("Opcode revision diff") and docs/engine-re.md ("Comparing engine builds").
"""
from __future__ import annotations

import argparse
import bisect
import collections
import difflib
import hashlib
import json
import re
import struct
from pathlib import Path

import operand_access as OA
import paths

DEFAULT_DEPTH = 3
MIN_BLOCK = 3              # instructions in an aligned run before its operand pairs vote
DISPATCH_VOTES = 1000      # weight of a same-opcode handler pair in the function correspondence
TABLE_LIMIT = 512          # jump-table entries read without a bound check
SHOWN_SITES = 6            # sites listed per differing fact
INLINE_SLACK = 2           # callee levels below a paired function searched for a fact the other build inlined
HELPER_FANIN = 8           # opcode trees that reach a function before it is compared once as a shared helper
CALL_REL32 = bytes([0xE8])
CTX_IMMEDIATE = 0x8000     # `add reg, imm` with imm in [CTX_IMMEDIATE, CTX_LIMIT) computes a ctx sub-object address
CTX_LIMIT = 0x200000
LARGE_FIELD = 0x1000       # offsets from here up are context fields: mapped, or moved with their learned neighbors
SMALL_FIELD = 0x20         # offsets below this stay literal in alignment shapes; larger ones align as any field
WEAK_VALUES = {-1, 0, 1}   # constants that compiler idioms produce (cmp r,0 / test r,r, or r,-1, inc / add 1)
STRONG_WEIGHTS = {"operand": 8, "str": 6, "fconst": 4, "div": 4, "mod": 4, "bit": 4, "cmp": 4, "const": 3, "icall": 3,
                  "code": 3, "call": 3, "addr": 2, "field": 1}
# Kinds whose count differences are strong. For the rest a build that keeps a value in a register reads it fewer
# times; only presence counts.
COUNTED_KINDS = {"operand", "bit", "cmp"}
ROLE_KINDS = {"const", "cmp", "bit"}
EXTEND_MASKS = {0xFF, 0xFFFF, 0xFFFFFF}
# Facts that compiler setup and data layout move around: Windows API calls (unidentified, and wrappers get inlined),
# container element counts, record copy sizes, shift counts (index scaling), register adds (pointer strides).
# Object fields (offsets below LARGE_FIELD) are weak too: each struct's layout moves on its own, which one offset map
# cannot follow. Context fields are one struct, so their map is reliable.
WEAK_KINDS = {"api", "elements", "copy", "shift", "step", "extend", "scale"}
SHIFTS = {"shl", "shr", "sar", "sal", "rol", "ror"}
# The C++ library's exception messages appear wherever its containers are inlined.
LIBRARY_MESSAGE = re.compile(r"^(?:[\w<>/]+ too long|invalid [\w<>/ ]+ (?:position|subscript|iterator|argument)"
                             r"|bad allocation|bad cast|Unknown exception)$")
MAPPED = {"field": "field", "addr": "addr", "code": "func", "call": "func"}   # fact kind -> correspondence map


# ----------------------------------------------------------------------------------------------- image + facts

class FlowImage(OA.Image):
    """OA.Image whose walk follows MSVC jump tables without decoding the tables as code, and treats a jump to
    another function's entry (a tail call), to an import thunk or into the C runtime as leaving the function."""

    def __init__(self, data: bytes, runtime_start: int, code: tuple[int, int]):
        super().__init__(data)
        self.runtime_start = runtime_start
        self.code = code
        self._entries: set[int] | None = None
        self._thunks: dict[int, bool] = {}

    def entries(self) -> set[int]:
        """Targets of every ``call rel32`` byte pattern in the code section: the image's function entries. A raw
        scan finds a few spurious targets too, which matters only if a branch happens to land on one."""
        if self._entries is None:
            start, end = (va - OA.IMAGE_BASE for va in self.code)
            found = set()
            pos = self.data.find(CALL_REL32, start, end - 5)
            while pos >= 0:
                target = OA.IMAGE_BASE + pos + 5 + struct.unpack_from("<i", self.data, pos + 1)[0]
                if self.code[0] <= target < self.code[1]:
                    found.add(target)
                pos = self.data.find(CALL_REL32, pos + 1, end - 5)
            self._entries = found
        return self._entries

    def is_thunk(self, va: int) -> bool:
        """An import thunk: a function that is only ``jmp [IAT slot]``."""
        if va not in self._thunks:
            i = self.insn(va) if self.contains(va) else None
            self._thunks[va] = bool(i is not None and i.mnemonic == "jmp" and i.operands[0].type == self.x86.X86_OP_MEM
                                    and i.operands[0].mem.base == 0 and i.operands[0].mem.index == 0)
        return self._thunks[va]

    def tail_target(self, i, entry: int) -> int | None:
        """Where a ``jmp`` that leaves the function entered at ``entry`` goes, else None."""
        if i.mnemonic != "jmp" or i.operands[0].type != self.x86.X86_OP_IMM:
            return None
        target = i.operands[0].imm
        if target == entry or not self.contains(target):
            return None
        if entry < self.runtime_start <= target or target in self.entries() or self.is_thunk(target):
            return target
        return None

    def walk(self, entry: int) -> list:
        tables: list[tuple[int, int]] = []
        insns = self._walk(entry, tables, ())
        if tables and any(lo <= i.address < hi for i in insns for lo, hi in tables):
            insns = self._walk(entry, [], tuple(tables))     # again, stopping where a jump table starts
        return insns

    def _walk(self, entry: int, tables: list, stops: tuple) -> list:
        x86 = self.x86
        seen: dict = {}
        work = [entry]
        while work and len(seen) < OA.WALK_LIMIT:
            va = work.pop()
            run: list = []
            while va not in seen and self.contains(va) and not any(lo <= va < hi for lo, hi in stops):
                i = self.insn(va)
                if i is None:
                    break
                seen[va] = i
                run.append(i)
                if i.mnemonic in ("ret", "int3"):
                    break
                if i.mnemonic.startswith("j"):
                    target = i.operands[0]
                    if target.type == x86.X86_OP_IMM:
                        if (self.contains(target.imm) and self.tail_target(i, entry) is None
                                and not (entry < self.runtime_start <= target.imm)):
                            work.append(target.imm)
                    elif i.mnemonic == "jmp" and target.type == x86.X86_OP_MEM:
                        work += self.jump_table(i, run, tables)
                    if i.mnemonic == "jmp":
                        break
                va = self.next_address(i)
        return [seen[key] for key in sorted(seen)]

    def jump_table(self, jmp, run: list, tables: list | None = None) -> list[int]:
        """Targets of ``jmp [index*4 + table]``, bounded by the run's ``cmp index, N`` when one guards it. Each table
        read (and the byte index table in front of a two-level switch) is appended to ``tables`` as [start, end)."""
        x86 = self.x86
        mem = jmp.operands[0].mem
        if mem.base != 0 or mem.index == 0 or mem.scale != 4 or not self.contains(mem.disp & 0xFFFFFFFF):
            return []
        index, count = mem.index, None
        recent = run[-8:-1][::-1]
        for k, i in enumerate(recent):
            ops = i.operands
            if (i.mnemonic == "movzx" and ops[0].type == x86.X86_OP_REG and ops[0].reg == index
                    and ops[1].type == x86.X86_OP_MEM and ops[1].mem.base != 0 and ops[1].size == 1
                    and self.contains(ops[1].mem.disp & 0xFFFFFFFF)):
                bound = self._bound(recent[k + 1:], ops[1].mem.base)
                if bound is not None:
                    start = (ops[1].mem.disp & 0xFFFFFFFF) - OA.IMAGE_BASE
                    count = max(self.data[start:start + bound]) + 1
                    if tables is not None:
                        tables.append((start + OA.IMAGE_BASE, start + OA.IMAGE_BASE + bound))
                break
            if i.mnemonic == "cmp" and ops[0].type == x86.X86_OP_REG and ops[0].reg == index:
                count = self._bound(recent[k:], index)
                break
        start = (mem.disp & 0xFFFFFFFF) - OA.IMAGE_BASE
        targets, read = [], 0
        for n in range(count if count is not None else TABLE_LIMIT):
            if start + 4 * n + 4 > len(self.data):
                break
            target = struct.unpack_from("<I", self.data, start + 4 * n)[0]
            if count is None and not abs(target - jmp.address) < 0x10000:
                break
            read = n + 1
            if self.contains(target):
                targets.append(target)
        if tables is not None and read:
            tables.append((mem.disp & 0xFFFFFFFF, (mem.disp & 0xFFFFFFFF) + 4 * read))
        return targets

    def _bound(self, recent: list, reg: int) -> int | None:
        x86 = self.x86
        for i in recent:
            ops = i.operands
            if (i.mnemonic == "cmp" and ops[0].type == x86.X86_OP_REG and ops[0].reg == reg
                    and ops[1].type == x86.X86_OP_IMM):
                return ops[1].imm + 1
        return None


def pe_sections(data: bytes) -> list[tuple[int, int]]:
    """[start, end) of each PE section in a dump. The packed builds strip the names but keep the usual order:
    code, read-only data, data."""
    header = struct.unpack_from("<I", data, 0x3C)[0]
    count = struct.unpack_from("<H", data, header + 6)[0]
    table = header + 24 + struct.unpack_from("<H", data, header + 20)[0]
    sections = []
    for n in range(count):
        vsize, va = struct.unpack_from("<II", data, table + 40 * n + 8)
        sections.append((OA.IMAGE_BASE + va, OA.IMAGE_BASE + va + vsize))
    return sections


def _signed(value: int) -> int:
    return value - (1 << 32) if value & 0x80000000 else value


class Revision:
    """One engine dump: dispatch table, code classes and per-function facts."""

    def __init__(self, engine_id: str, data: bytes | None = None, facts: OA.EngineFacts | None = None):
        self.engine_id = engine_id
        self.facts = facts or OA.ENGINES[engine_id]
        if not self.facts.runtime_start:
            raise SystemExit(f"{engine_id}: runtime_start not pinned in tools/operand_access.py ENGINES")
        self.data = data if data is not None else OA.dump_path(engine_id).read_bytes()
        self.code, self.rdata = pe_sections(self.data)[:2]
        self.image = FlowImage(self.data, self.facts.runtime_start, self.code)
        self.table = OA.dispatch_table(self.image, self.facts)
        self._features: dict[int, list] = {}
        self._shapes: dict[int, list] = {}
        self._callees: dict[int, list] = {}
        self.cookie = self._stack_cookie()

    # -- code classes -------------------------------------------------------------------------------------------
    def is_engine(self, va: int) -> bool:
        return (self.image.contains(va) and va < self.facts.runtime_start and va not in self.facts.accessors
                and not self.image.is_thunk(va))

    def target(self, i, func: int) -> int | None:
        """The function a direct call or a tail jump in func goes to."""
        target = self.image.call_target(i)
        return target if target is not None else self.image.tail_target(i, func)

    def in_code(self, va: int) -> bool:
        return self.code[0] <= va < self.code[1]

    def in_rdata(self, va: int) -> bool:
        return self.rdata[0] <= va < self.rdata[1]

    def callees(self, func: int) -> list[int]:
        """Engine functions func calls directly, in address order of the call sites."""
        if func not in self._callees:
            self._callees[func] = [t for i in self.image.body(func).insns
                                   if (t := self.target(i, func)) is not None and self.is_engine(t)]
        return self._callees[func]

    def tree(self, entry: int, depth: int, leaves: frozenset = frozenset()) -> dict[int, int]:
        """Engine functions reachable from entry within depth call levels -> level. ``leaves`` are not expanded."""
        levels = {entry: 0}
        frontier = [entry]
        while frontier:
            following = []
            for func in frontier:
                if levels[func] >= depth or (func in leaves and func != entry):
                    continue
                for callee in self.callees(func):
                    if callee not in levels:
                        levels[callee] = levels[func] + 1
                        following.append(callee)
            frontier = following
        return levels

    def _stack_cookie(self) -> int | None:
        """The /GS cookie global: the absolute load that ``xor reg, esp`` / ``xor reg, ebp`` consumes most."""
        x86 = self.image.x86
        votes = collections.Counter()
        for op, handler in self.table.items():
            for func in self.tree(handler, 2):
                insns = self.image.body(func).insns
                for k, i in enumerate(insns[:-1]):
                    nxt = insns[k + 1]
                    if (i.mnemonic == "mov" and len(i.operands) == 2 and i.operands[1].type == x86.X86_OP_MEM
                            and i.operands[1].mem.base == 0 and i.operands[1].mem.index == 0
                            and nxt.mnemonic == "xor" and nxt.operands[1].type == x86.X86_OP_REG
                            and nxt.operands[1].reg in (x86.X86_REG_ESP, x86.X86_REG_EBP)):
                        votes[i.operands[1].mem.disp & 0xFFFFFFFF] += 1
        return votes.most_common(1)[0][0] if votes else None

    # -- memory reads -------------------------------------------------------------------------------------------
    def string_at(self, va: int) -> str | None:
        """A string literal: NUL-terminated cp932 text that starts a literal (aligned or after a NUL), 3+ chars or
        2 ASCII ones. Three bytes and a NUL that read as an address into the image are a pointer table entry."""
        start = va - OA.IMAGE_BASE
        if start <= 0 or (va % 4 and self.data[start - 1] != 0):
            return None
        end = self.data.find(b"\0", start, start + 512)
        if end < 0 or end - start < 2:
            return None
        if end - start == 3 and self.image.contains(struct.unpack_from("<I", self.data, start)[0]):
            return None
        try:
            text = self.data[start:end].decode("cp932")
        except UnicodeDecodeError:
            return None
        if not all(ch.isprintable() or ch in "\r\n\t" for ch in text):
            return None
        if len(text) < 3 and not text.isascii():
            return None
        return text

    def float_at(self, va: int, size: int) -> float | None:
        start = va - OA.IMAGE_BASE
        if size == 4:
            return struct.unpack_from("<f", self.data, start)[0]
        if size == 8:
            return struct.unpack_from("<d", self.data, start)[0]
        return None

    # -- facts --------------------------------------------------------------------------------------------------
    def features(self, func: int) -> list[tuple[tuple, int]]:
        """(fact, instruction address) for every behavior fact in one function's own body."""
        if func in self._features:
            return self._features[func]
        x86 = self.image.x86
        body = self.image.body(func)
        divisions, idiom = self._idioms(body.insns)
        out: list[tuple[tuple, int]] = []
        for k, i in enumerate(body.insns):
            if i.address in divisions:
                out.append((divisions[i.address], i.address))
                continue
            for fact in self._insn_facts(func, body, k, i, x86):
                if fact == ("addr", self.cookie) or (i.address in idiom and fact[0] in ("const", "shift")):
                    continue
                if fact[0] in ("const", "cmp") and self._string_capacity(body.insns, k, fact[1]):
                    continue
                out.append((fact, i.address))
        self._features[func] = out
        return out

    def _idioms(self, insns: list) -> tuple[dict[int, tuple], set[int]]:
        """Compiler idioms read as one fact each, by the address of their ``mov reg, imm``:

        - MSVC's division by a constant, ``mov eax, M; imul/mul r; [add edx, r]; sar/shr edx, s; ...; shr eax, 31``:
          ("div", divisor), or ("elements", size) when the dividend is a difference (a container's element count,
          which follows the element's layout). The idiom's shifts are not facts of their own.
        - A block copy or fill, ``mov ecx, n; rep movsd/stosd``: ("copy", n), the size of a record.
        """
        x86 = self.image.x86
        found, idiom = {}, set()
        for k, i in enumerate(insns):
            ops = i.operands
            if not (i.mnemonic == "mov" and len(ops) == 2 and ops[0].type == x86.X86_OP_REG
                    and ops[1].type == x86.X86_OP_IMM):
                continue
            if ops[0].reg == x86.X86_REG_ECX and any(x.mnemonic.startswith("rep ") for x in insns[k + 1:k + 4]):
                found[i.address] = ("copy", ops[1].imm & 0xFFFFFFFF)
                continue
            magic = ops[1].imm & 0xFFFFFFFF
            if magic < 0x10000 or self.image.contains(magic):
                continue
            mul = next((j for j in range(k + 1, min(k + 4, len(insns)))
                        if insns[j].mnemonic in ("imul", "mul") and len(insns[j].operands) == 1), None)
            if mul is None:
                continue
            shift = None
            for j in range(mul + 1, min(mul + 7, len(insns))):
                x = insns[j]
                if x.mnemonic in ("sar", "shr") and len(x.operands) == 2 and x.operands[1].type == x86.X86_OP_IMM:
                    if (shift is None and x.operands[1].imm < 31 and x.operands[0].type == x86.X86_OP_REG
                            and x.operands[0].reg == x86.X86_REG_EDX):
                        shift = x.operands[1].imm
                    idiom.add(x.address)
            shift = shift or 0
            difference = any(x.mnemonic == "sub" and x.operands[0].type == x86.X86_OP_REG
                             and x.operands[0].reg != x86.X86_REG_ESP for x in insns[max(0, k - 6):mul])
            found[i.address] = ("elements" if difference else "div", round(2 ** (32 + shift) / magic))
        return found, idiom

    def _modulo_fixup(self, insns: list, k: int, value: int) -> bool:
        """``or r, -2^k`` after ``and r, 0x80000000 | (2^k - 1)``: the sign fix-up of signed x % 2^k."""
        x86 = self.image.x86
        if value >= 0 or not _low_mask(-value - 1):
            return False
        mask = 0x80000000 | (-value - 1)
        return any(x.mnemonic == "and" and x.operands[-1].type == x86.X86_OP_IMM
                   and (x.operands[-1].imm & 0xFFFFFFFF) == mask for x in insns[max(0, k - 4):k])

    def _runtime_argument(self, insns: list, k: int) -> bool:
        """``push imm`` that the next call, into the C runtime, takes."""
        for x in insns[k + 1:k + 6]:
            if x.mnemonic == "call":
                target = self.image.call_target(x)
                return target is not None and self.image.contains(target) and target >= self.facts.runtime_start
        return False

    def _compared(self, insns: list, k: int) -> bool:
        """``mov reg, imm`` whose register a ``cmp`` reads next: the compiler keeps a compared constant in a register
        (``mov ebx, 2; cmp [x], ebx``) where another build compares the immediate."""
        x86 = self.image.x86
        reg = insns[k].operands[0]
        if reg.type != x86.X86_OP_REG:
            return False
        for x in insns[k + 1:k + 4]:
            ops = x.operands
            if x.mnemonic == "cmp" and any(op.type == x86.X86_OP_REG and op.reg == reg.reg for op in ops):
                return True
            if ops and ops[0].type == x86.X86_OP_REG and ops[0].reg == reg.reg:
                return False
        return False

    def _string_capacity(self, insns: list, k: int, value: int) -> bool:
        """std::string's small-buffer idioms, which inlining moves between functions: ``cmp [capacity], 16`` before an
        unsigned branch, and the reset ``mov [capacity], 15`` next to a terminating byte store."""
        x86 = self.image.x86
        i = insns[k]
        following = insns[k + 1:k + 5]
        if value == 16 and i.mnemonic == "mov" and i.operands[0].type == x86.X86_OP_REG:
            # mov reg, 16; cmp [capacity], reg; jb
            return any(x.mnemonic == "cmp" and any(y.mnemonic in ("jb", "jae") for y in insns[k + 2 + n:k + 4 + n])
                       for n, x in enumerate(following[:3]))
        if not i.operands or i.operands[0].type != x86.X86_OP_MEM:
            return False
        if value == 16 and i.mnemonic == "cmp":
            return any(x.mnemonic in ("jb", "jae") for x in following[:2])
        if value == 15 and i.mnemonic == "mov":
            return any(x.mnemonic == "mov" and x.operands[0].type == x86.X86_OP_MEM and x.operands[0].size == 1
                       for x in insns[max(0, k - 4):k] + following)
        return False

    def _insn_facts(self, func, body, k, i, x86):
        mnemonic = i.mnemonic
        tail = self.image.tail_target(i, func)
        if tail is not None:                            # a tail call counts as a call
            if not self.image.contains(tail) or self.image.is_thunk(tail):
                yield ("api",)
            elif self.is_engine(tail):
                yield ("call", tail)
            return
        if mnemonic == "call":
            target = self.image.call_target(i)
            if target is None:
                op = i.operands[0]
                if op.type == x86.X86_OP_MEM and op.mem.base == 0 and op.mem.index == 0:
                    yield ("api",)                      # call [IAT]
                elif op.type == x86.X86_OP_MEM and op.mem.base not in (x86.X86_REG_ESP, x86.X86_REG_EBP):
                    yield ("icall", op.mem.disp)        # virtual call: the vtable slot
                else:
                    yield ("icall", None)
                return
            accessor = self.facts.accessors.get(target)
            if accessor is not None:
                args = OA.stack_arguments(self.image, body, k)
                index = args[accessor.index_arg - 1] if len(args) >= accessor.index_arg else None
                yield ("operand", accessor.access, index)
            elif not self.image.contains(target) or self.image.is_thunk(target):
                yield ("api",)                          # packer stub out of the module, or an import thunk
            elif self.is_engine(target):
                yield ("call", target)
            return
        if mnemonic.startswith("j") or mnemonic in ("ret", "int3", "nop"):
            return
        ops = i.operands
        stack_adjust = (mnemonic in ("add", "sub", "and") and ops and ops[0].type == x86.X86_OP_REG
                        and ops[0].reg == x86.X86_REG_ESP)
        float_load = mnemonic.startswith("f") and not mnemonic.startswith(("fst", "fist", "fnst", "fbst"))
        sse_load = (mnemonic in ("movss", "movsd", "addss", "addsd", "subss", "subsd", "mulss", "mulsd", "divss",
                                 "divsd", "comiss", "comisd", "ucomiss", "ucomisd", "cvtss2sd", "cvtsd2ss")
                    and len(ops) == 2 and ops[1].type == x86.X86_OP_MEM)
        for n, op in enumerate(ops):
            if op.type == x86.X86_OP_IMM:
                if stack_adjust:
                    continue
                value = op.imm & 0xFFFFFFFF
                if self.image.contains(value):
                    yield self._address_fact(value)
                elif is_offset_add(i, n, x86):
                    yield ("field", value)              # add reg, ctx offset: a sub-object's address
                elif mnemonic == "and" and value in EXTEND_MASKS:
                    yield ("extend", value)             # zero extension, as movzx does
                elif mnemonic == "and" and value & 0x80000000 and _low_mask(value & 0x7FFFFFFF):
                    yield ("mod", (value & 0x7FFFFFFF) + 1)    # signed x % 2^k keeps the sign bit
                elif mnemonic == "or" and self._modulo_fixup(body.insns, k, _signed(value)):
                    continue                            # the % idiom's sign fix-up
                elif (size := _max_size(value)) is not None and mnemonic in ("cmp", "mov", "push"):
                    yield ("elements", size)            # a container's max_size: 0xFFFFFFFF // element size
                elif mnemonic == "imul" and len(ops) == 3:
                    yield ("scale", value)              # index * element size
                elif mnemonic == "push" and self._runtime_argument(body.insns, k):
                    yield ("copy", value)               # a size passed to memset, memcpy, operator new
                elif mnemonic in ("test", "and", "or", "xor"):
                    yield ("bit", _signed(value))
                elif mnemonic == "cmp" or (mnemonic == "mov" and self._compared(body.insns, k)):
                    yield ("cmp", _signed(value))
                elif mnemonic in SHIFTS:
                    yield ("shift", value)
                elif mnemonic in ("add", "sub") and ops[0].type == x86.X86_OP_REG:
                    yield ("step", _signed(value))
                else:
                    yield ("const", _signed(value))
            elif op.type == x86.X86_OP_MEM:
                mem = op.mem
                disp = mem.disp & 0xFFFFFFFF
                if mem.base in (x86.X86_REG_ESP, x86.X86_REG_EBP):
                    continue
                if mem.base == 0:
                    if not self.image.contains(disp):
                        continue
                    if (mem.index == 0 and (float_load or (sse_load and n == 1)) and self.in_rdata(disp)):
                        value = self.float_at(disp, op.size)
                        if value is not None:
                            yield ("fconst", value)
                            continue
                    yield ("code" if self.in_code(disp) else "addr", disp)    # a load: never a string literal
                elif self.image.contains(disp):
                    yield ("code" if self.in_code(disp) else "addr", disp)
                elif mem.disp:
                    yield ("field", mem.disp)

    def _address_fact(self, va: int) -> tuple:
        if self.in_code(va):
            return ("code", va)
        if self.in_rdata(va):
            text = self.string_at(va)
            if text is not None:
                return ("str", text)
        return ("addr", va)

    # -- alignment ----------------------------------------------------------------------------------------------
    def shape(self, func: int) -> list[str]:
        """Instruction tokens for alignment: registers and small offsets kept, addresses and large offsets
        abstracted."""
        if func in self._shapes:
            return self._shapes[func]
        x86 = self.image.x86
        out = []
        for i in self.image.body(func).insns:
            target = self.target(i, func)
            if target is not None:
                accessor = self.facts.accessors.get(target)
                kind = (accessor.name if accessor else
                        "api" if not self.image.contains(target) or self.image.is_thunk(target)
                        else "rt" if target >= self.facts.runtime_start else "f")
                out.append("call " + kind)
                continue
            parts = []
            for n, op in enumerate(i.operands):
                if op.type == x86.X86_OP_REG:
                    parts.append(i.reg_name(op.reg))
                elif op.type == x86.X86_OP_IMM:
                    value = op.imm & 0xFFFFFFFF
                    parts.append("A" if self.image.contains(value) or i.mnemonic.startswith("j")
                                 else "F" if is_offset_add(i, n, x86) else str(_signed(value)))
                else:
                    mem = op.mem
                    disp = mem.disp & 0xFFFFFFFF
                    base = i.reg_name(mem.base) if mem.base else ""
                    index = f"{i.reg_name(mem.index)}*{mem.scale}" if mem.index else ""
                    stack = base in ("esp", "ebp")
                    d = ("A" if self.image.contains(disp) else str(mem.disp) if stack or abs(mem.disp) < SMALL_FIELD
                         else "F")
                    parts.append(f"[{base}+{index}+{d}]")
            out.append(i.mnemonic + " " + ",".join(parts))
        self._shapes[func] = out
        return out

    def operand_pairs(self, func: int) -> list[list[tuple]]:
        """Per instruction: the (kind, value) operands alignment maps -- fields, addresses, functions."""
        x86 = self.image.x86
        out = []
        for i in self.image.body(func).insns:
            target = self.target(i, func)
            if target is not None:
                out.append([("func", target)] if self.is_engine(target) else [])
                continue
            row = []
            for n, op in enumerate(i.operands):
                if op.type == x86.X86_OP_IMM:
                    value = op.imm & 0xFFFFFFFF
                    if self.image.contains(value) and not i.mnemonic.startswith("j"):
                        row.append(("func" if self.in_code(value) else "addr", value))
                    elif is_offset_add(i, n, x86):
                        row.append(("field", value))
                elif op.type == x86.X86_OP_MEM:
                    mem = op.mem
                    disp = mem.disp & 0xFFFFFFFF
                    if self.image.contains(disp):
                        row.append(("func" if self.in_code(disp) else "addr", disp))
                    elif mem.base not in (0, x86.X86_REG_ESP, x86.X86_REG_EBP) and mem.disp:
                        row.append(("field", mem.disp))
            out.append(row)
        return out


# ------------------------------------------------------------------------------------------------ correspondence

class Correspondence:
    """Reference -> other-build maps for functions, fields and globals, learned by alignment votes."""

    def __init__(self, ref: Revision, other: Revision, depth: int):
        self.ref, self.other = ref, other
        self.votes = {kind: collections.defaultdict(collections.Counter) for kind in ("func", "field", "addr")}
        self.aligned_pairs: set[tuple[int, int]] = set()
        self._learn(depth)
        self.maps = {kind: self._resolve(votes) for kind, votes in self.votes.items()}
        self.inverse = {kind: {v: k for k, v in m.items()} for kind, m in self.maps.items()}
        self._steps = {side: self._layout_steps(m) for side, m in (("ref", self.maps["field"]),
                                                                    ("other", self.inverse["field"]))}

    def _learn(self, depth: int) -> None:
        queue = collections.deque()
        for op in sorted(set(self.ref.table) & set(self.other.table)):
            a, b = self.ref.table[op], self.other.table[op]
            self.votes["func"][a][b] += DISPATCH_VOTES       # the dispatch tables pair handlers exactly
            queue.append((a, b, 0))
        while queue:
            a, b, level = queue.popleft()
            if (a, b) in self.aligned_pairs:
                continue
            self.aligned_pairs.add((a, b))
            for callee_a, callee_b in self._align(a, b):
                if level < depth and (callee_a, callee_b) not in self.aligned_pairs:
                    queue.append((callee_a, callee_b, level + 1))

    def _align(self, a: int, b: int) -> list[tuple[int, int]]:
        shape_a, shape_b = self.ref.shape(a), self.other.shape(b)
        pairs_a, pairs_b = self.ref.operand_pairs(a), self.other.operand_pairs(b)
        matcher = difflib.SequenceMatcher(None, shape_a, shape_b, autojunk=False)
        callees = []
        for block in matcher.get_matching_blocks():
            if block.size < MIN_BLOCK:
                continue
            for n in range(block.size):
                row_a, row_b = pairs_a[block.a + n], pairs_b[block.b + n]
                if len(row_a) != len(row_b):
                    continue
                for (kind_a, value_a), (kind_b, value_b) in zip(row_a, row_b):
                    if kind_a != kind_b:
                        continue
                    self.votes[kind_a][value_a][value_b] += 1
                    if kind_a == "func" and self.ref.is_engine(value_a) and self.other.is_engine(value_b):
                        callees.append((value_a, value_b))
        # Calls outside aligned runs: when both call the same number of distinct engine functions, pair them in
        # first-call order (one vote each; the corpus-wide majority decides).
        distinct_a = list(dict.fromkeys(self.ref.callees(a)))
        distinct_b = list(dict.fromkeys(self.other.callees(b)))
        if len(distinct_a) == len(distinct_b):
            paired_a = {x for x, _ in callees}
            for callee_a, callee_b in zip(distinct_a, distinct_b):
                if callee_a not in paired_a:
                    self.votes["func"][callee_a][callee_b] += 1
                    callees.append((callee_a, callee_b))
        return callees

    @staticmethod
    def _resolve(votes) -> dict:
        resolved = {}
        for key, counter in votes.items():
            ranked = counter.most_common(2) + [(None, 0)]
            if ranked[0][1] > ranked[1][1]:
                resolved[key] = ranked[0][0]
        return resolved

    @staticmethod
    def _layout_steps(mapping: dict) -> tuple[list[int], list[int]]:
        """Learned large offsets in order, with how far each moved."""
        keys = sorted(k for k in mapping if isinstance(k, int) and k >= LARGE_FIELD)
        return keys, [mapping[k] - k for k in keys]

    def _interpolate(self, side: str, value: int) -> int | None:
        """Where an unlearned large offset moved: layouts move in blocks, so an offset between two learned ones that
        moved alike moved with them."""
        keys, deltas = self._steps[side]
        at = bisect.bisect_left(keys, value)
        if 0 < at < len(keys) and deltas[at - 1] == deltas[at]:
            return value + deltas[at]
        return None

    # -- translation into the other build's terms ---------------------------------------------------------------
    def translate_ref(self, fact: tuple) -> tuple:
        kind = fact[0]
        if kind not in MAPPED:
            return fact
        mapped = self.maps[MAPPED[kind]].get(fact[1])
        if mapped is not None:
            return (kind, mapped)
        if kind == "field":
            if abs(fact[1]) < LARGE_FIELD:
                return fact                             # object layouts mostly stay put
            moved = self._interpolate("ref", fact[1])
            if moved is not None:
                return (kind, moved)
        return (kind, "unmapped")

    def translate_other(self, fact: tuple) -> tuple:
        kind = fact[0]
        if kind not in MAPPED or fact[1] in self.inverse[MAPPED[kind]]:
            return fact
        if kind == "field" and (abs(fact[1]) < LARGE_FIELD or self._interpolate("other", fact[1]) is not None):
            return fact
        return (kind, "unmapped")


def is_offset_add(i, n: int, x86) -> bool:
    """``add reg, imm`` with a context-sized immediate: how these builds take the address of a ctx sub-object."""
    if i.mnemonic != "add" or n != 1 or i.operands[0].type != x86.X86_OP_REG:
        return False
    return CTX_IMMEDIATE <= (i.operands[1].imm & 0xFFFFFFFF) < CTX_LIMIT


# --------------------------------------------------------------------------------------------------- comparison

def _low_mask(value: int) -> bool:
    """value is 2^k - 1 for some k >= 1."""
    return value > 0 and value & (value + 1) == 0


def _max_size(value: int) -> int | None:
    """The element size whose container max_size is value (0xFFFFFFFF // size), for sizes 4..4096."""
    if not 0x00100000 <= value < 0x40000000:
        return None
    size = 0xFFFFFFFF // value
    return size if 4 <= size <= 4096 and 0xFFFFFFFF // size in (value, value + 1) else None


def _take(counter: collections.Counter, key, count: int) -> None:
    counter[key] -= count
    if counter[key] <= 0:
        del counter[key]


def is_weak(fact: tuple) -> bool:
    kind, value = fact[0], fact[1] if len(fact) > 1 else None
    if value == "unmapped" or (kind == "icall" and value is None) or kind in WEAK_KINDS:
        return True
    if kind == "str" and LIBRARY_MESSAGE.match(value):
        return True
    if (kind in ("const", "cmp") and value in WEAK_VALUES) or (kind == "bit" and value in (-1, 0)):
        return True
    return kind == "field" and isinstance(value, int) and abs(value) < LARGE_FIELD


def fact_text(fact: tuple) -> str:
    kind = fact[0]
    value = fact[1] if len(fact) > 1 else None
    if kind == "operand":
        return f"operand {fact[2]} {fact[1]}"
    if kind in ("const", "cmp", "bit", "step", "shift", "copy", "div", "elements", "extend", "scale", "mod"):
        return f"{kind} {value} (0x{value & 0xFFFFFFFF:x})"
    if kind in ("field", "icall"):
        return f"{kind} {value}" if not isinstance(value, int) else f"{kind} +0x{value & 0xFFFFFFFF:x}"
    if kind in ("addr", "code", "call"):
        return f"{kind} {value}" if not isinstance(value, int) else f"{kind} 0x{value:x}"
    if kind == "str":
        return f"str {value!r}"
    if kind == "fconst":
        return f"fconst {value!r}"
    return kind


class Comparison:
    """Compares call trees of the two builds, with shared helpers as leaves."""

    def __init__(self, ref: Revision, other: Revision, depth: int):
        self.ref, self.other, self.depth = ref, other, depth
        self.corr = Correspondence(ref, other, depth)
        self.shared = sorted(set(ref.table) & set(other.table))
        self.helpers_ref, self.used_by = self._helpers(ref)
        self.helpers_other, _ = self._helpers(other)
        self._deep: dict[tuple[str, int], set] = {}

    def _helpers(self, rev: Revision) -> tuple[frozenset, dict[int, list[int]]]:
        roots = set(rev.table.values())
        used_by = collections.defaultdict(list)
        for op in self.shared:
            for func in rev.tree(rev.table[op], self.depth):
                used_by[func].append(op)
        helpers = frozenset(f for f, ops in used_by.items() if len(ops) >= HELPER_FANIN and f not in roots)
        return helpers, used_by

    def compare(self, a: int, b: int) -> dict:
        ref, other, corr = self.ref, self.other, self.corr
        tree_a = ref.tree(a, self.depth, self.helpers_ref)
        tree_b = other.tree(b, self.depth, self.helpers_other)
        leaves_a = {f for f in tree_a if f in self.helpers_ref and f != a}
        leaves_b = {f for f in tree_b if f in self.helpers_other and f != b}

        def collect(rev, tree, leaves, translate):
            facts, sites = collections.Counter(), collections.defaultdict(list)
            for func in sorted(tree):
                if func in leaves:
                    continue
                for fact, address in rev.features(func):
                    if fact[0] == "call" and fact[1] not in leaves:
                        continue                        # a flattened callee: its facts are counted instead
                    fact = translate(fact)
                    facts[fact] += 1
                    sites[fact].append((func, address))
            return facts, sites

        facts_a, sites_a = collect(ref, tree_a, leaves_a, corr.translate_ref)
        facts_b, sites_b = collect(other, tree_b, leaves_b, corr.translate_other)
        only_a, only_b = facts_a - facts_b, facts_b - facts_a
        assumed = {}
        for kind in MAPPED:                             # unlearned on both sides: assume they correspond
            key = (kind, "unmapped")
            common = min(only_a.get(key, 0), only_b.get(key, 0))
            if common:
                assumed[kind] = common
                _take(only_a, key, common)
                _take(only_b, key, common)
        # The same constant in another role (``or r, 700`` / ``mov r, 700``, ``cmp x, K`` / ``mov r, K``): a compiler
        # choice, unless the value is one the idioms produce.
        for fact_a in [f for f in only_a if f[0] in ROLE_KINDS]:
            for kind in ROLE_KINDS - {fact_a[0]}:
                fact_b = (kind, fact_a[1])
                common = min(only_a.get(fact_a, 0), only_b.get(fact_b, 0))
                if common:
                    assumed["role"] = assumed.get("role", 0) + common
                    _take(only_a, fact_a, common)
                    _take(only_b, fact_b, common)
        # Context fields left over on both sides: as with unlearned ones, assume they correspond.
        fields_a = [f for f in only_a if f[0] == "field" and not is_weak(f)]
        fields_b = [f for f in only_b if f[0] == "field" and not is_weak(f)]
        common = min(sum(only_a[f] for f in fields_a), sum(only_b[f] for f in fields_b))
        if common:
            assumed["field-pairing"] = common
            for side, fields in ((only_a, fields_a), (only_b, fields_b)):
                left = common
                for f in fields:
                    taken = min(left, side[f])
                    _take(side, f, taken)
                    left -= taken

        counted_a = set(tree_a) - leaves_a
        counted_b = set(tree_b) - leaves_b
        deeper_a = self._covered(only_a, sites_a, "other", corr.maps["func"], counted_b)
        deeper_b = self._covered(only_b, sites_b, "ref", corr.inverse["func"], counted_a)
        for only, sites, rev, translate, facts_other, deeper in (
                (only_a, sites_a, ref, corr.translate_ref, facts_b, deeper_a),
                (only_b, sites_b, other, corr.translate_other, facts_a, deeper_b)):
            for fact, count in only.items():
                if fact[0] == "call" and self._inlined(rev, translate, sites[fact], facts_other):
                    deeper[fact] = count

        def strong_count(fact, count, counts_other, deeper):
            if is_weak(fact):
                return 0
            if fact[0] not in COUNTED_KINDS and counts_other.get(fact, 0):
                return 0                                # both have it, only a different number of times
            return count - deeper.get(fact, 0)

        def describe(side, sites, counts_other, deeper):
            rows = []
            for fact, count in side.items():
                strong = strong_count(fact, count, counts_other, deeper)
                row = {"fact": fact_text(fact), "count": count, "strong": strong,
                       "sites": [f"0x{addr:x} in 0x{func:x}" for func, addr in sites.get(fact, [])[:SHOWN_SITES]]}
                if deeper.get(fact):
                    row["deeper"] = deeper[fact]
                rows.append(row)
            rows.sort(key=lambda row: (-(row["strong"] > 0), -STRONG_WEIGHTS.get(row["fact"].split()[0], 1),
                                       row["fact"]))
            return rows

        sides = ((only_a, facts_b, deeper_a), (only_b, facts_a, deeper_b))
        score = sum(STRONG_WEIGHTS.get(fact[0], 1) * strong_count(fact, count, counts_other, deeper)
                    for side, counts_other, deeper in sides for fact, count in side.items())
        weak = sum(count - strong_count(fact, count, counts_other, deeper)
                   for side, counts_other, deeper in sides for fact, count in side.items())
        verdict = "differs" if score else "minor" if weak else "same"
        record = {
            "verdict": verdict,
            "score": score,
            "weak_differences": weak,
            "functions": {"reference": len(tree_a), "other": len(tree_b)},
            "facts": {"reference": sum(facts_a.values()), "other": sum(facts_b.values())},
            "assumed_corresponding_unmapped": assumed,
            "only_reference": describe(only_a, sites_a, facts_b, deeper_a),
            "only_other": describe(only_b, sites_b, facts_a, deeper_b),
        }
        if verdict == "differs":
            record["localized"] = self._localize(tree_a, tree_b, leaves_a, leaves_b)
        return record

    def _deeper(self, side: str, func: int) -> dict[int, set]:
        """Facts of each of func's callees within INLINE_SLACK levels, helpers expanded, in the other build's
        terms."""
        key = (side, func)
        if key not in self._deep:
            rev, translate = ((self.ref, self.corr.translate_ref) if side == "ref"
                              else (self.other, self.corr.translate_other))
            self._deep[key] = {callee: {translate(fact) for fact, _ in rev.features(callee)}
                               for callee in rev.tree(func, INLINE_SLACK) if callee != func}
        return self._deep[key]

    @staticmethod
    def _inlined(rev: Revision, translate, sites: list, facts_other) -> bool:
        """A call only one build makes, to a function whose strong facts the other build's tree all has: the other
        build inlined it."""
        for func, address in sites[:1]:
            target = rev.target(rev.image.insn(address), func)
            if target is None:
                return False
            own = {translate(fact) for fact, _ in rev.features(target)}
            return all(fact in facts_other for fact in own if not is_weak(fact) and fact[0] != "call")
        return False

    def _covered(self, only, sites, other_side: str, partner_of: dict, counted_other: set) -> collections.Counter:
        """How many of each one-sided fact the other build has in callees of the paired function that its compared
        tree leaves out (past the depth limit, or inside a shared helper): inlining moved that code there."""
        covered = collections.Counter()
        for fact, count in only.items():
            if is_weak(fact):
                continue
            found = 0
            for func, _ in sites.get(fact, []):
                partner = partner_of.get(func)
                if partner is None:
                    continue
                if any(fact in facts for callee, facts in self._deeper(other_side, partner).items()
                       if callee not in counted_other):
                    found += 1
            if found:
                covered[fact] = min(count, found)
        return covered

    def _localize(self, tree_a, tree_b, leaves_a, leaves_b) -> list[dict]:
        """Paired functions whose own facts differ strongly, then the functions only one tree has."""
        corr = self.corr
        funcs_a = {f for f in tree_a if f not in leaves_a}
        funcs_b = {f for f in tree_b if f not in leaves_b}
        rows, paired_b = [], set()
        for a in sorted(funcs_a, key=lambda f: (tree_a[f], f)):
            b = corr.maps["func"].get(a)
            if b not in funcs_b:
                continue
            paired_b.add(b)
            own_a = collections.Counter(corr.translate_ref(f) for f, _ in self.ref.features(a))
            own_b = collections.Counter(corr.translate_other(f) for f, _ in self.other.features(b))
            extra_a = [f for f in own_a - own_b if not is_weak(f)]
            extra_b = [f for f in own_b - own_a if not is_weak(f)]
            if extra_a or extra_b:
                rows.append({"pair": f"0x{a:x}=0x{b:x}", "level": tree_a[a],
                             "only_reference": sorted(fact_text(f) for f in extra_a),
                             "only_other": sorted(fact_text(f) for f in extra_b)})
        unpaired_a = sorted(f for f in funcs_a if corr.maps["func"].get(f) not in funcs_b)
        unpaired_b = sorted(f for f in funcs_b if f not in paired_b)
        if unpaired_a or unpaired_b:
            rows.append({"unpaired_reference": [f"0x{f:x}" for f in unpaired_a],
                         "unpaired_other": [f"0x{f:x}" for f in unpaired_b]})
        return rows


def build(reference: str, other_id: str, depth: int, only_ops: list[int] | None) -> dict:
    ref, other = Revision(reference), Revision(other_id)
    comparison = Comparison(ref, other, depth)
    corr = comparison.corr
    ops = [op for op in comparison.shared if only_ops is None or op in only_ops]
    opcodes = {}
    for op in ops:
        record = comparison.compare(ref.table[op], other.table[op])
        record["handler"] = {"reference": f"0x{ref.table[op]:x}", "other": f"0x{other.table[op]:x}"}
        opcodes[f"0x{op:x}"] = record
    helpers = {}
    if only_ops is None:
        for a in sorted(comparison.helpers_ref):
            b = corr.maps["func"].get(a)
            users = [f"0x{op:x}" for op in comparison.used_by[a]]
            if b is None or not other.is_engine(b):
                helpers[f"0x{a:x}"] = {"verdict": "unpaired", "used_by": users}
                continue
            record = comparison.compare(a, b)
            record["pair"] = f"0x{a:x}=0x{b:x}"
            record["used_by"] = users
            helpers[f"0x{a:x}"] = record
    return {
        "generated_by": "tools/revision_diff.py",
        "reference": _identity(ref),
        "other": _identity(other),
        "depth": depth,
        "helper_fanin": HELPER_FANIN,
        "shared_opcodes": len(comparison.shared),
        "only_reference": [f"0x{op:x}" for op in sorted(set(ref.table) - set(other.table))],
        "only_other": [f"0x{op:x}" for op in sorted(set(other.table) - set(ref.table))],
        "correspondence": {kind: len(m) for kind, m in corr.maps.items()},
        "verdicts": dict(sorted(collections.Counter(r["verdict"] for r in opcodes.values()).items())),
        "helper_verdicts": dict(sorted(collections.Counter(r["verdict"] for r in helpers.values()).items())),
        "opcodes": opcodes,
        "helpers": helpers,
    }


def _identity(rev: Revision) -> dict:
    return {"engine_id": rev.engine_id, "version": rev.facts.version, "handlers": len(rev.table),
            "runtime_start": f"0x{rev.facts.runtime_start:x}",
            "stack_cookie": None if rev.cookie is None else f"0x{rev.cookie:x}",
            "engine_dump_sha256": hashlib.sha256(rev.data).hexdigest()}


# ----------------------------------------------------------------------------------------------------- report

def render_markdown(report: dict, labels: dict[int, str]) -> str:
    ref, other = report["reference"], report["other"]
    name_ref = f"{ref['engine_id']} ({ref['version']})"
    name_other = f"{other['engine_id']} ({other['version']})"

    def strong_facts(record: dict, limit: int = 5) -> str:
        parts = [f"−{row['fact']}" + (f" ×{row['strong']}" if row["strong"] > 1 else "")
                 for row in record["only_reference"] if row["strong"]][:limit]
        parts += [f"+{row['fact']}" + (f" ×{row['strong']}" if row["strong"] > 1 else "")
                  for row in record["only_other"] if row["strong"]][:limit]
        return "; ".join(parts).replace("|", "¦")

    def where(record: dict) -> str:
        pairs = [row["pair"] for row in record.get("localized", []) if "pair" in row]
        return ", ".join(pairs[:3])

    lines = [
        f"# Opcode revision diff: {name_ref} vs {name_other}",
        "",
        "Generated by `tools/revision_diff.py`; do not edit. `−` facts are only in the reference build, `+` only in "
        "the other. A `differs` row is a candidate to read, not a finding; rows whose only strong fact is one "
        "context field or one helper call are usually correspondence gaps.",
        "",
        f"- Depth {report['depth']}, shared helpers at fan-in {report['helper_fanin']}; "
        f"{report['shared_opcodes']} shared opcodes.",
        f"- Opcode verdicts: {report['verdicts']}. Helper verdicts: {report['helper_verdicts']}.",
        f"- Learned correspondence: {report['correspondence']}.",
        f"- Only in {ref['engine_id']}: {', '.join(report['only_reference']) or 'none'}. "
        f"Only in {other['engine_id']}: {', '.join(report['only_other']) or 'none'}.",
        "",
        "## Opcodes that differ",
        "",
        f"| op | label | score | handlers | strong differences | localized in |",
        "|---|---|---:|---|---|---|",
    ]
    for op, record in sorted(report["opcodes"].items(), key=lambda item: (-item[1]["score"], item[0])):
        if record["verdict"] != "differs":
            continue
        handlers = f"{record['handler']['reference']} / {record['handler']['other']}"
        lines.append(f"| {op} | {labels.get(int(op, 16), '')} | {record['score']} | {handlers} | "
                     f"{strong_facts(record)} | {where(record)} |")
    lines += ["", "## Shared helpers that differ", "",
              "| helper | score | used by | strong differences | localized in |", "|---|---:|---|---|---|"]
    for name, record in sorted(report["helpers"].items(), key=lambda item: (-item[1].get("score", 0), item[0])):
        if record["verdict"] != "differs":
            continue
        users = ", ".join(record["used_by"][:6]) + (f" (+{len(record['used_by']) - 6})"
                                                    if len(record["used_by"]) > 6 else "")
        lines.append(f"| {record['pair']} | {record['score']} | {users} | {strong_facts(record)} | "
                     f"{where(record)} |")
    unpaired = [name for name, record in report["helpers"].items() if record["verdict"] == "unpaired"]
    minor = [op for op, record in report["opcodes"].items() if record["verdict"] == "minor"]
    lines += ["", f"Unpaired helpers: {', '.join(unpaired) or 'none'}.", "",
              f"Opcodes with only weak differences ({len(minor)}): {', '.join(minor) or 'none'}.", ""]
    return "\n".join(lines)


def opcode_labels() -> dict[int, str]:
    path = paths.SHARED_BUILD / "opcodes.json"
    if not path.is_file():
        return {}
    data = json.loads(path.read_text(encoding="utf-8"))
    return {int(entry["op"], 16): entry.get("label") or "" for entry in data["opcodes"]}


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("reference", choices=sorted(OA.ENGINES), help="reference engine id")
    parser.add_argument("other", choices=sorted(OA.ENGINES), help="engine id compared against it")
    parser.add_argument("--depth", type=int, default=DEFAULT_DEPTH, help="callee levels below each handler")
    parser.add_argument("--op", action="append", default=None, help="limit to these opcodes (repeatable)")
    parser.add_argument("--output", type=Path, default=None, help="JSON path (Markdown is written beside it)")
    args = parser.parse_args(argv)
    only_ops = [int(op, 16) for op in args.op] if args.op else None
    report = build(args.reference, args.other, args.depth, only_ops)
    output = args.output or paths.SHARED_BUILD / "revision-diff" / f"{args.reference}-{args.other}.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    output.with_suffix(".md").write_text(render_markdown(report, opcode_labels()), encoding="utf-8")
    print(f"revision-diff: {args.reference} vs {args.other}, depth {args.depth}: {report['shared_opcodes']} shared "
          f"opcodes {report['verdicts']}, helpers {report['helper_verdicts']} -> {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
