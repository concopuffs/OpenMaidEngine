#!/usr/bin/env python3
"""Focused sys4load rendering regressions. Run: py -3.11 -X utf8 tools/test_sys4load.py"""
from pathlib import Path

from sys4load import Instruction, Sys4Script, render_listing


def test_semantic_annotation_uses_generated_registry() -> None:
    fields = (0,) * 13
    script = Sys4Script(
        Path("FRONTIER.BIN"), fields, (0x24,), magic="SYS4433 ",
        instructions=[Instruction(0, 0x24, "u00418A90", [])], code_end=1)

    listing = render_listing(script)

    assert "fade-surface-out-to-white" in listing
    assert "; op 0x24 investigated" in listing


if __name__ == "__main__":
    test_semantic_annotation_uses_generated_registry()
    print("ok: sys4load semantic annotation")
