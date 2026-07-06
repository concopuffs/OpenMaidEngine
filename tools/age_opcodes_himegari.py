"""Himegari-specific opcode INFERENCE layer (sits on top of age_opcodes.py).

`age_opcodes.py` is the verbatim Kelebek table (opcode number + argc, validated 481/481).
Many opcodes have only engine-address labels (`u004xxxx`). This module records what the
top-frequency unnamed opcodes *mean*, inferred from operand types, disassembly context,
and neighbouring named ops (see `vm-map/himegari-opcode-notes.md` for the evidence).

These are INFERENCES, not ground truth. Fields:
  name       short mnemonic for the disassembler
  category   marker | structural | computational | draw | audio | adv | control
  noop       True  = safe for the Godot VM v1 to skip (no state/visible effect expected)
             False = has an effect; must be implemented (or knowingly stubbed)
  confidence high | med | low
  method     how the inference was reached / how to confirm:
             structure | context | harness (confirm via dialogue diff in Phase 4) |
             frida (needs live capture) | unicorn (micro-exec)
  note       one-line rationale / caveat

Consumed by `sys4load.render_listing` (nicer disassembly) and, later, the Godot VM.
"""
from __future__ import annotations

INFERRED: dict[int, dict] = {
    # ---- statement / scope scaffolding: zero-arg, no operands, bracket statements ----
    0x1f4: dict(name="stmt-begin", category="marker", noop=True, confidence="high",
                method="structure", note="zero-arg; opens scripts, pairs with stmt-end 0x1f5"),
    0x1f5: dict(name="stmt-end", category="marker", noop=True, confidence="high",
                method="structure", note="zero-arg; precedes exit/next-stmt, pairs with 0x1f4"),
    0x1d5: dict(name="cond-block", category="marker", noop=True, confidence="high",
                method="context", note="zero-arg; ALWAYS follows jcc — marks conditional body entry"),
    0x1bc: dict(name="block-mark", category="marker", noop=True, confidence="high",
                method="context", note="zero-arg; follows jcc/mov, precedes mov/ret — block boundary"),
    0x1bf: dict(name="call-end", category="marker", noop=True, confidence="med",
                method="context", note="zero-arg; call->0x1bf->stmt-end — end-of-call-statement marker"),

    # ---- statement metadata carrying an id (tentative no-op, confirm via harness) ----
    0x21b: dict(name="line-id?", category="marker", noop=True, confidence="med",
                method="harness", note="1 imm; mov->0x21b->stmt-end; near save/load-messkip — likely line/stmt id, verify not msg-control"),
    0x1d2: dict(name="stmt-desc?", category="marker", noop=True, confidence="med",
                method="harness", note="2 imm; immediately after stmt-begin 0x1f4 — statement descriptor?"),
    0x258: dict(name="decl?", category="marker", noop=True, confidence="low",
                method="harness", note="2 imm; runs in a chain right after script-entry 0x259, enumerating ids — prologue declaration/registration?"),

    # ---- structural ----
    0x71: dict(name="label-def", category="structural", noop=True, confidence="high",
               method="structure", note="1 imm; count == T1 table size -> the label/anchor T1 indexes. v1 no-op; revisit if menu/callback dispatch looks up by id"),

    # ---- input / UI hotspot (u0041Bxxx/Cxxx widget module; see himegari-opcode-notes.md) ----
    0x90: dict(name="hotspot-branch", category="input", noop=False, confidence="high",
               method="structure", note="argc7: x y w h + 3 code targets (0xffffffff=unused). "
               "Cursor/input hotspot hit-test; branches per interaction (inferred hover-enter->flag=1 / "
               "hover-leave->flag=0 / click->run-action), FALLS THROUGH to pc+1 on no-match "
               "(design-confirmed: enc.len 15 lands on the next stmt). HEADLESS: fall through = correct "
               "no-input behaviour (proven safe by 279 CLEAN scenes). Occurs ONLY in a shared ADV-chrome "
               "subroutine, identical in all 301 ADV scripts (8/script = 5 imm-rect buttons @y=572 "
               "x=684..772 20x20 toggling G[0x6c9..0x6cd] + 3 local-operand keyed forms). Model live in A2; "
               "confirm target->state mapping via input capture/Frida."),
    0x97: dict(name="hotspot-reg?", category="input", noop=False, confidence="med",
               method="frida", note="argc5: v1 v2 1 1 <action-id imm>; NO code targets. Interleaves with "
               "0x90 in the ADV-chrome subroutine -> companion register-hotspot / set-widget-action "
               "(trailing imm = action id 0x0/0x7/0x8). u0041C150, same widget cluster as 0x90/0x91/0x92/0x95."),

    # ---- computational (exact behaviour via Unicorn micro-exec) ----
    0x215: dict(name="count?", category="computational", noop=False, confidence="med",
                method="unicorn", note="2 args -> writes global then result tested >0 (gre/lt) — count/search-returns-index helper"),
    0x1a2: dict(name="resolve-handle?", category="computational", noop=False, confidence="low",
                method="frida", note="1 local-ptr from lookup-array, then create-texture — resolves a looked-up resource/handle"),

    # ---- ADV / text-display (effectful; confirm via Frida) ----
    0x7a: dict(name="text-param?", category="adv", noop=False, confidence="med",
               method="frida", note="3 args (imm/computed/imm); sub computes a value then 0x7a then show-text — text speed/wait/window param"),

    # ---- draw / UI (0x420-0x421 graphics family; effectful; Frida) ----
    0x202: dict(name="draw-blit?", category="draw", noop=False, confidence="med",
                method="frida", note="5 args (coords/sizes); preceded by coord arithmetic, near draw ops"),
    0x203: dict(name="draw?", category="draw", noop=False, confidence="med",
                method="frida", note="4 args; chains with 0x202/draw-texture"),
    0x1f7: dict(name="ui-elem?", category="draw", noop=False, confidence="med",
                method="frida", note="2 args; 0x420 family, pairs with 0x1fa — create/begin a UI element"),
    0x1fa: dict(name="ui-clear?", category="draw", noop=False, confidence="med",
                method="frida", note="1 arg (element id); follows 0x1f7 — show/hide/clear UI element by id"),
    0x217: dict(name="gfx-geom?", category="draw", noop=False, confidence="low",
                method="frida", note="4 global-ints; part of a 0x217/0x218/0x21a geometry chain"),
    0x218: dict(name="gfx-geom?", category="draw", noop=False, confidence="low",
                method="frida", note="4 global-ints; chains with 0x21a/0x217"),
    0x21a: dict(name="gfx-geom?", category="draw", noop=False, confidence="low",
                method="frida", note="4 global-ints; chains with 0x218/0x217"),
    0x1ff: dict(name="draw?", category="draw", noop=False, confidence="low",
                method="frida", note="4 args (global+imms); follows 0x217, then call"),

    # ---- audio (0x41D family, near play-sound-effect 0xb4) ----
    0xb6: dict(name="snd-ctrl?", category="audio", noop=False, confidence="low",
               method="frida", note="1 imm; self-chains, 0x41D family near play-sound-effect/0xb5 — sound channel/volume/stop control"),
}

# Names ending in "?" are low/medium-confidence guesses; the disassembler shows them
# so listings read better than `u004xxxx`, but the VM must not treat non-noop ops as
# no-ops without Frida/Unicorn/harness confirmation (see `method`).
