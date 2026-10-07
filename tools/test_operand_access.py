#!/usr/bin/env python3
"""Tests for tools/operand_access.py. Run: py -3.11 -X utf8 tools/test_operand_access.py

Registry folding/apply tests are pure. The x86 tests synthesize a tiny flat image (constructor,
handler and accessor stubs) and need capstone; without it they are reported as skipped.
"""
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import operand_access as A

FAILS = []


def check(cond, msg):
    print(("  ok  " if cond else " FAIL ") + msg)
    if not cond:
        FAILS.append(msg)


# --------------------------------------------------------------------------- synthetic image
BASE = A.IMAGE_BASE
FETCH, WRITE = 0x401000, 0x401010
HANDLER, CONSTRUCTOR, DEFAULT = 0x401100, 0x401400, 0x401500
TABLE = 0x100


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


def synthetic_image() -> bytes:
    image = bytearray(0x2000)

    def place(asm):
        image[asm.va - BASE:asm.va - BASE + len(asm.code)] = asm.code

    place(Asm(FETCH).emit(b"\xC2\x04\x00"))           # ret 4
    place(Asm(WRITE).emit(b"\xC2\x08\x00"))           # ret 8
    place(Asm(DEFAULT).emit(b"\xC3"))
    h = Asm(HANDLER)
    h.emit(b"\x6A\x02", b"\x8B\xCE").call(FETCH)      # fetch(2)
    h.emit(b"\x83\xF8\x40", b"\x6A\x02", b"\x8B\xCE")  # cmp eax,0x40; push 2 hoisted above jle
    jle_at = len(h.code)
    h.emit(b"\x7E\x00").call(FETCH)                   # fall-through: fetch(2)
    jmp_at = len(h.code)
    h.emit(b"\xEB\x00")
    target_l1 = h.here()
    h.call(FETCH)                                     # branch target: fetch(2) via the jle source
    target_l2 = h.here()
    h.code[jle_at + 1] = target_l1 - (HANDLER + jle_at + 2)
    h.code[jmp_at + 1] = target_l2 - (HANDLER + jmp_at + 2)
    h.emit(b"\x50", b"\x6A\x01", b"\x8B\xCE").call(WRITE)            # write(1, eax)
    h.emit(b"\xBF\x03\x00\x00\x00", b"\x57", b"\x8B\xCE").call(FETCH)  # mov edi,3; fetch(edi)
    h.emit(b"\xC3")
    place(h)
    c = Asm(CONSTRUCTOR)
    c.emit(b"\xB8", struct.pack("<I", DEFAULT))                         # mov eax, default
    c.emit(b"\x8D\xBE", struct.pack("<I", TABLE))                       # lea edi,[esi+table]
    c.emit(b"\xB9\x00\x04\x00\x00", b"\xF3\xAB")                        # mov ecx,0x400; rep stosd
    c.emit(b"\xC7\x86", struct.pack("<II", TABLE + 0x55 * 4, HANDLER))  # table[0x55] = handler
    c.emit(b"\xC7\x86", struct.pack("<II", TABLE + 0x10 * 4, DEFAULT))  # default override ignored
    c.emit(b"\xC3")
    place(c)
    return bytes(image)


FACTS = A.EngineFacts(CONSTRUCTOR, TABLE, {
    FETCH: A.Accessor("read", 1, "fetch"),
    WRITE: A.Accessor("write", 1, "write"),
})


def test_synthetic_x86():
    try:
        import capstone  # noqa: F401
    except ImportError:
        print("  skip x86 extraction tests (capstone not installed)")
        return
    image = A.Image(synthetic_image())
    table = A.dispatch_table(image, FACTS)
    check(table == {0x55: HANDLER}, "dispatch table reads overrides and drops the stosd default")
    found = A.accesses(image, FACTS.accessors, HANDLER)
    reads = sorted(r["arg"] for r in found if r["access"] == "read")
    check(reads == [2, 2, 2, 3], "fetch indices: hoisted push, branch-target push, register index")
    check([r["arg"] for r in found if r["access"] == "write"] == [1], "write index is the first stack argument")
    by_arg, unresolved = A.contracts(found, 3)
    check(by_arg == {1: ["write"], 2: ["read"], 3: ["read"]}, "contracts fold by operand index")
    check(unresolved == [], "every synthetic call resolves")


# --------------------------------------------------------------------------- registry folding/apply
def test_contracts_reject_out_of_range():
    by_arg, unresolved = A.contracts([{"arg": 4, "access": "read"}, {"arg": None, "access": "write"},
                                      {"arg": 1, "access": "write"}, {"arg": 1, "access": "read"}], 3)
    check(by_arg == {1: ["read", "write"]}, "kinds sort in vocabulary order")
    check(len(unresolved) == 2, "indices outside argc and unknown indices stay unresolved")


REGISTRY = """[[opcode]]
op = 0x55
label = "mov"
argc = 2

[opcode.semantics]
name = "mov"

[[opcode.semantics.args]]
i = 1
role = "dst"
observed_types = ["l-int"]
access = ["stale"]

[[opcode]]
op = 0x22e
label = "rot"
argc = 2

[opcode.semantics]
name = "rot"
"""

PLAN = {
    0x55: {1: {"SYS4422": ["write"], "SYS4433": ["write"]},
           2: {"SYS4422": ["read"], "SYS4433": ["read"]}},
    0x22e: {1: {"SYS4422": ["read"], "SYS4433": ["read"]},
            2: {"SYS4422": ["read"], "SYS4433": ["read-float"]}},
}


def test_apply_to_registry():
    out = A.apply_to_registry(REGISTRY, PLAN, {0x55: {2: {0x0, 0x9}}, 0x22e: {1: {0x0}, 2: {0x9}}})
    check('access = ["stale"]' not in out, "owned access lines are replaced")
    check('observed_types = ["l-int"]\naccess = ["write"]\n' in out, "existing entry gains its contract")
    check('i = 2\nrole = ""\nobserved_types = ["imm", "l-int"]\naccess = ["read"]' in out,
          "a missing operand entry is appended with census types")
    check('access_by_abi = { SYS4422 = ["read"], SYS4433 = ["read-float"] }' in out,
          "revision-specific contracts use access_by_abi")
    check(out.index("op = 0x55") < out.index('observed_types = ["imm", "l-int"]') < out.index("op = 0x22e"),
          "appended entries stay inside their opcode record")
    check(A.apply_to_registry(out, PLAN, {}) == out, "apply is idempotent")
    import tomllib
    data = tomllib.loads(out)
    check([len(o["semantics"]["args"]) for o in data["opcode"]] == [2, 2], "result is valid TOML")


def main():
    test_synthetic_x86()
    test_contracts_reject_out_of_range()
    test_apply_to_registry()
    print("FAILURES:", len(FAILS))
    return 1 if FAILS else 0


if __name__ == "__main__":
    sys.exit(main())
