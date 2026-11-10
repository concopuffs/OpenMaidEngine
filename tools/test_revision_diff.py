#!/usr/bin/env python3
"""Tests for tools/revision_diff.py. Run: py -3.11 -X utf8 tools/test_revision_diff.py

The idiom helpers are pure. The comparison tests synthesize two tiny engine images (PE section table, dispatch
constructor, handlers, a worker, a switch) and need capstone; without it they are reported as skipped.
"""
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import operand_access as OA
import revision_diff as RD

FAILS = []


def check(cond, msg):
    print(("  ok  " if cond else " FAIL ") + msg)
    if not cond:
        FAILS.append(msg)


# --------------------------------------------------------------------------- pure helpers
def test_pure():
    check(RD._max_size(0x0FFFFFFF) == 16, "max_size 0x0fffffff -> 16-byte elements")
    check(RD._max_size(0x038E38E3) == 72, "max_size 0x038e38e3 -> 72-byte elements")
    check(RD._max_size(0x3FFFFFFF) == 4, "max_size 0x3fffffff -> 4-byte elements")
    check(RD._max_size(0x10000000) is None, "a flag bit is no max_size")
    check(RD._low_mask(1) and RD._low_mask(0xFFF) and not RD._low_mask(0x1000), "low masks")
    check(RD.is_weak(("str", "vector<T> too long")) and not RD.is_weak(("str", "set:BlankExtentMode")),
          "library exception messages are weak, engine strings strong")
    check(RD.is_weak(("field", 0x40C)) and not RD.is_weak(("field", 0x2000)),
          "object fields weak, context fields strong")
    check(RD.is_weak(("const", 1)) and not RD.is_weak(("bit", 1)) and RD.is_weak(("bit", -1)),
          "constant 1 is weak, a bit-0 test is not")


# --------------------------------------------------------------------------- synthetic images
BASE = OA.IMAGE_BASE
FETCH = 0x401000
H1, H2, WORKER, CALLER, H3 = 0x401100, 0x401200, 0x401300, 0x401380, 0x401400
CONSTRUCTOR, DEFAULT, DIVF, ELEMF = 0x401700, 0x401800, 0x401900, 0x401980
RUNTIME = 0x402700
RDATA = 0x402800
TABLE = 0x100
CODE_END = RDATA


class Asm:
    def __init__(self, va):
        self.va, self.code = va, bytearray()

    def here(self):
        return self.va + len(self.code)

    def emit(self, *chunks):
        for chunk in chunks:
            self.code += chunk
        return self

    def call(self, target):
        return self.emit(b"\xE8", struct.pack("<i", target - (self.here() + 5)))

    def jmp(self, target):
        return self.emit(b"\xE9", struct.pack("<i", target - (self.here() + 5)))


def pe_header(image: bytearray) -> None:
    image[0:2] = b"MZ"
    struct.pack_into("<I", image, 0x3C, 0x40)
    image[0x40:0x44] = b"PE\0\0"
    struct.pack_into("<H", image, 0x46, 3)           # sections
    struct.pack_into("<H", image, 0x54, 0)           # optional header size
    for n, (va, size) in enumerate(((0x1000, CODE_END - 0x401000), (RDATA - BASE, 0x800), (0x3000, 0x1000))):
        struct.pack_into("<II", image, 0x58 + 40 * n + 8, size, va)


def image(*, compare=0x40, field=0x2000, tail_call=True) -> bytes:
    data = bytearray(0x4000)
    pe_header(data)

    def place(asm):
        data[asm.va - BASE:asm.va - BASE + len(asm.code)] = asm.code

    place(Asm(FETCH).emit(b"\xC2\x04\x00"))                              # ret 4
    place(Asm(DEFAULT).emit(b"\xC3"))
    place(Asm(RUNTIME).emit(b"\xC3"))
    # op 0x55: fetch operand 2, compare it, store it into a context field
    h = Asm(H1).emit(b"\x56", b"\x8B\xF1", b"\x6A\x02", b"\x8B\xCE").call(FETCH)
    h.emit(b"\x83\xF8", bytes([compare]), b"\x7E\x06")                  # cmp eax, imm8; jle
    h.emit(b"\x89\x86", struct.pack("<I", field), b"\x5E\xC3")          # mov [esi+field], eax; pop esi; ret
    place(h)
    # op 0x56: fetch operand 1, store it, then the worker (tail jump or call)
    h = Asm(H2).emit(b"\x56", b"\x8B\xF1", b"\x6A\x01", b"\x8B\xCE").call(FETCH)
    h.emit(b"\x89\x86", struct.pack("<I", field))
    if tail_call:
        h.emit(b"\x5E").jmp(WORKER)
    else:
        h.call(WORKER).emit(b"\x5E\xC3")
    place(h)
    place(Asm(WORKER).emit(b"\x8B\x44\x24\x04", b"\xA8\x01", b"\xC3"))    # mov eax,[esp+4]; test al,1; ret
    place(Asm(CALLER).call(WORKER).emit(b"\xC3"))                        # another caller: WORKER is an entry
    # op 0x57: a bounded switch whose table follows a call that does not return
    h = Asm(H3).emit(b"\x8B\x44\x24\x04", b"\x83\xF8\x02")               # mov eax,[esp+4]; cmp eax,2
    ja_at = len(h.code)
    h.emit(b"\x77\x00")                                                  # ja throw
    jmp_at = len(h.code)
    h.emit(b"\xFF\x24\x85\x00\x00\x00\x00")                              # jmp [eax*4 + table]
    h.code[ja_at + 1] = len(h.code) - (ja_at + 2)
    h.call(RUNTIME)                                                      # throw: the runtime never returns
    table_va = h.here()
    struct.pack_into("<I", h.code, jmp_at + 3, table_va)
    h.emit(b"\0" * 12)
    cases = []
    for value in (0x111, 0x222, 0x333):
        cases.append(h.here())
        h.emit(b"\xB8", struct.pack("<I", value), b"\xC3")               # mov eax, value; ret
    for n, case in enumerate(cases):
        struct.pack_into("<I", h.code, table_va - H3 + 4 * n, case)
    place(h)
    # a division by 28, and the element count of a 28-byte container
    place(Asm(DIVF).emit(b"\x8B\x4C\x24\x04", b"\xB8\x93\x24\x49\x92", b"\xF7\xE9", b"\x03\xD1",
                         b"\xC1\xFA\x04", b"\x8B\xC2", b"\xC1\xE8\x1F", b"\x03\xC2", b"\xC3"))
    place(Asm(ELEMF).emit(b"\x8B\x4E\x08", b"\x2B\x4E\x04", b"\xB8\x93\x24\x49\x92", b"\xF7\xE9", b"\x03\xD1",
                          b"\xC1\xFA\x04", b"\x8B\xC2", b"\xC1\xE8\x1F", b"\x03\xC2", b"\xC3"))
    c = Asm(CONSTRUCTOR)
    c.emit(b"\xB8", struct.pack("<I", DEFAULT), b"\x8D\xBE", struct.pack("<I", TABLE))
    c.emit(b"\xB9\x00\x04\x00\x00", b"\xF3\xAB")                          # mov ecx,0x400; rep stosd
    for op, handler in ((0x55, H1), (0x56, H2), (0x57, H3)):
        c.emit(b"\xC7\x86", struct.pack("<II", TABLE + op * 4, handler))
    c.emit(b"\xC3")
    place(c)
    data[RDATA - BASE:RDATA - BASE + 4] = struct.pack("<I", 0x00402A40)   # "@*@" and a NUL: a pointer
    data[RDATA - BASE + 0x10:RDATA - BASE + 0x15] = b"Font\0"
    return bytes(data)


FACTS = OA.EngineFacts(CONSTRUCTOR, TABLE, {FETCH: OA.Accessor("read", 1, "fetch")}, runtime_start=RUNTIME)


def revision(name, **kwargs):
    return RD.Revision(name, data=image(**kwargs), facts=FACTS)


def verdicts(ref, other):
    comparison = RD.Comparison(ref, other, RD.DEFAULT_DEPTH)
    return {op: comparison.compare(ref.table[op], other.table[op]) for op in comparison.shared}


def facts_of(rev, func):
    return [fact for fact, _ in rev.features(func)]


def test_images():
    ref = revision("ref")
    check(sorted(ref.table) == [0x55, 0x56, 0x57], "dispatch table found")
    same = verdicts(ref, revision("copy"))
    check(all(record["verdict"] == "same" for record in same.values()), "identical builds compare same")

    changed = verdicts(ref, revision("other", compare=0x41))
    check(changed[0x55]["verdict"] == "differs" and changed[0x56]["verdict"] == "same",
          "a changed compare constant differs, the rest stays same")
    texts = {row["fact"] for row in changed[0x55]["only_reference"] + changed[0x55]["only_other"]}
    check({"cmp 64 (0x40)", "cmp 65 (0x41)"} <= texts, "the differing constants are reported")

    moved = verdicts(ref, revision("other", field=0x2004))
    check(all(record["verdict"] == "same" for record in moved.values()),
          "a context field that moved in both handlers is learned and maps")

    called = verdicts(ref, revision("other", tail_call=False))
    check(called[0x56]["verdict"] == "same", "a tail jump compares as the call it replaces")
    check(("bit", 1) in facts_of(ref, WORKER), "the tail-called worker's bit test is a fact")

    body = ref.image.body(H3).insns
    table_va = next(i for i in body if i.mnemonic == "jmp").operands[0].mem.disp
    check(not any(table_va <= i.address < table_va + 12 for i in body), "a jump table is not decoded as code")
    consts = {fact for fact in facts_of(ref, H3) if fact[0] == "const"}
    check({("const", 0x111), ("const", 0x222), ("const", 0x333)} <= consts, "every switch case is walked")

    check(("div", 28) in facts_of(ref, DIVF) and not any(f[0] == "shift" for f in facts_of(ref, DIVF)),
          "a magic-number division reads as div 28 without its shifts")
    check(("elements", 28) in facts_of(ref, ELEMF), "dividing a pointer difference reads as an element count")

    check(ref.string_at(RDATA) is None, "three bytes and a NUL that read as an image address are no string")
    check(ref.string_at(RDATA + 0x10) == "Font", "a short ASCII string is read")


if __name__ == "__main__":
    test_pure()
    try:
        import capstone  # noqa: F401
    except ImportError:
        print("  skip x86 tests: capstone is not installed")
    else:
        test_images()
    print(f"\n{'FAILED' if FAILS else 'OK'}: {len(FAILS)} failure(s)")
    sys.exit(1 if FAILS else 0)
