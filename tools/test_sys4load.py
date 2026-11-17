#!/usr/bin/env python3
"""Focused sys4load regressions. Run: py -3.11 -X utf8 tools/test_sys4load.py"""
import struct
from pathlib import Path

from sys4load import (RAW_BASE, Instruction, Sys4Script, _decode_string, decode_text, encode_text,
                      is_single_byte_unit, render_listing)


def test_semantic_annotation_uses_generated_registry() -> None:
    fields = (0,) * 13
    script = Sys4Script(
        Path("FRONTIER.BIN"), fields, (0x24,), magic="SYS4433 ",
        instructions=[Instruction(0, 0x24, "u00418A90", [])], code_end=1)

    listing = render_listing(script)

    assert "fade-surface-out-to-white" in listing
    assert "; op 0x24 investigated" in listing


def test_strings_keep_every_byte() -> None:
    # Sys4StringCodecTests.KeepsBytesOutsideJisX0208ByteForByte, the runtime's twin.
    raw = bytes.fromhex("8740 FA40 ED40 F040 EB40 8041 A042 FD43 8120 00")
    padded = raw + bytes(-len(raw) % 4)
    dwords = [x ^ 0xFFFFFFFF for x in struct.unpack(f"<{len(padded) // 4}I", padded)]
    text, ndwords = _decode_string(dwords, 0)
    assert [ord(c) for c in text] == [
        0x2460, 0x2170, RAW_BASE + 0xED40, 0xE000, RAW_BASE + 0xEB40, RAW_BASE + 0x8041, 0xF8F0, 0x42,
        RAW_BASE + 0xFD43, RAW_BASE + 0x8120]
    assert ndwords == 5
    assert encode_text(text) == raw[:-1]


def test_every_unit_round_trips_as_windows_encodes() -> None:
    for lead in range(0x80, 0x100):
        if is_single_byte_unit(lead):
            continue
        for trail in range(1, 0x100):
            unit = bytes([lead, trail])
            text = decode_text(unit)
            assert len(text) == 1 and encode_text(text) == unit, unit.hex()
    # A character with a NEC-selected IBM and an IBM code takes the IBM one, as Windows' code page does.
    assert decode_text(bytes.fromhex("FA5C")) == "纊"
    assert encode_text("纊") == bytes.fromhex("FA5C")
    assert decode_text(bytes.fromhex("ED40")) == chr(RAW_BASE + 0xED40)
    assert decode_text(b"A\x82") == "A" + chr(RAW_BASE + 0x82)


if __name__ == "__main__":
    test_semantic_annotation_uses_generated_registry()
    test_strings_keep_every_byte()
    test_every_unit_round_trips_as_windows_encodes()
    print("ok: sys4load semantic annotation, strings")
