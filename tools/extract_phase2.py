#!/usr/bin/env python3
"""Phase 2 batch extraction: disassembly + text corpora from every SYS4 script.

Outputs (all under build/, workspace-root relative):
  build/disasm/<NAME>.asm         full disassembly listing (one per script)
  build/text/<NAME>.strings.txt   all inline strings in that script
  build/text/dialogue.jsonl       show-text (0x6E) lines only — the translation corpus
  build/text/strings.jsonl        every inline string, tagged by the opcode that references it
  build/manifest.json             per-script stats (instructions, strings, dialogue, decode-clean)

Authoritative copies: a game-dir override shadows its extracted/DATA1 copy. Run:
  py -3.11 -X utf8 tools/extract_phase2.py
"""
from __future__ import annotations
import json
import os
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import paths
import sys4load

DATA1 = paths.DATA1
BUILD = paths.BUILD
DISASM = BUILD / "disasm"
TEXT = BUILD / "text"

SHOW_TEXT = 0x6E                        # dialogue opcode; type-2 arg = displayed line


def authoritative_scripts() -> dict[str, Path]:
    """name -> path, game-dir overrides winning over extracted/DATA1."""
    return paths.scripts()


def opcode_for_string_ref(scr, value_dword_index: int) -> tuple[int | None, str | None]:
    """Given the body index of a string's *value* operand, find the owning instruction."""
    for ins in scr.instructions:
        base = ins.offset + 1
        for a in range(len(ins.args)):
            if base + 2 * a + 1 == value_dword_index:
                return ins.opcode, ins.label
    return None, None


def main() -> int:
    for d in (DISASM, TEXT, BUILD / "data", BUILD / "scripts-json"):
        d.mkdir(parents=True, exist_ok=True)

    files = authoritative_scripts()
    manifest = []
    n_dialogue = n_strings = 0
    skipped = []

    with (TEXT / "dialogue.jsonl").open("w", encoding="utf-8") as dlg, \
         (TEXT / "strings.jsonl").open("w", encoding="utf-8") as allstr:
        for name, p in sorted(files.items()):
            try:
                scr = sys4load.load(p)
            except sys4load.Sys4Error as e:
                skipped.append((name, str(e)))
                continue

            # 1) disassembly listing
            (DISASM / f"{Path(name).stem}.asm").write_text(
                sys4load.render_listing(scr), encoding="utf-8")

            # 2) per-script strings file
            if scr.strings:
                lines = [f"0x{off:05x}\t{txt}" for off, (txt, _) in sorted(scr.strings.items())]
                (TEXT / f"{Path(name).stem}.strings.txt").write_text(
                    "\n".join(lines) + "\n", encoding="utf-8")

            # 3) combined corpora, tagged by referencing opcode
            for val_idx, off in sorted(scr.string_refs.items(), key=lambda kv: kv[1]):
                txt = scr.strings.get(off, (None,))[0]
                if txt is None:
                    continue
                op, label = opcode_for_string_ref(scr, val_idx)
                rec = {"file": name, "off": f"0x{off:x}",
                       "op": f"0x{op:x}" if op is not None else None,
                       "label": label, "text": txt}
                allstr.write(json.dumps(rec, ensure_ascii=False) + "\n")
                n_strings += 1
                if op == SHOW_TEXT:
                    dlg.write(json.dumps({"file": name, "off": f"0x{off:x}", "text": txt},
                                         ensure_ascii=False) + "\n")
                    n_dialogue += 1

            n, unk, trunc, clean = sys4load.decode_stats(scr)
            manifest.append({"file": name, "instructions": n, "strings": len(scr.strings),
                             "clean": clean, "unknown": unk, "truncated": trunc})

    (BUILD / "manifest.json").write_text(
        json.dumps({"scripts": len(manifest), "skipped": skipped,
                    "total_dialogue_lines": n_dialogue, "total_strings": n_strings,
                    "per_script": manifest}, ensure_ascii=False, indent=2), encoding="utf-8")

    clean = sum(1 for m in manifest if m["clean"])
    print(f"scripts processed: {len(manifest)}  (decode-clean: {clean}/{len(manifest)})")
    print(f"skipped (non-script magic): {len(skipped)}  -> {[s[0] for s in skipped]}")
    print(f"disasm listings: build/disasm/*.asm")
    print(f"dialogue lines (show-text): {n_dialogue}  -> build/text/dialogue.jsonl")
    print(f"all inline strings: {n_strings}  -> build/text/strings.jsonl")
    print(f"manifest: build/manifest.json")
    return 0


if __name__ == "__main__":
    sys.exit(main())
