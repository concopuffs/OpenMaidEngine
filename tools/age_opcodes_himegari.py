# DO NOT EDIT -- generated from vm-map/opcodes.toml by tools/opcodes_build.py --build

"""Inferred Himegari opcode semantics (generated). sys4load reads INFERRED[op]['name']."""
from __future__ import annotations

INFERRED: dict[int, dict] = {
    0x71: dict(name='label-def', category='structural', noop=True, confidence='high', source='investigation', summary='1 imm; count == T1 table size -> the label/anchor T1 indexes. v1 no-op; revisit if menu/callback dispatch looks up by id'),
    0x7a: dict(name='text-param?', category='adv', noop=False, confidence='med', source='inference', summary='3 args (imm/computed/imm); sub computes a value then 0x7a then show-text — text speed/wait/window param'),
    0x90: dict(name='hotspot-branch', category='input', noop=True, confidence='high', source='investigation', summary='cursor/input hotspot hit-test: rect (x,y,w,h) -> 3-way branch on interaction, else fall through to pc+1'),
    0x97: dict(name='hotspot-reg?', category='input', noop=True, confidence='med', source='inference', summary='companion register-hotspot / set-widget-action (argc5: v1 v2 1 1 <action-id>; NO code targets)'),
    0xb6: dict(name='snd-ctrl?', category='audio', noop=False, confidence='low', source='inference', summary='1 imm; self-chains, 0x41D family near play-sound-effect/0xb5 — sound channel/volume/stop control'),
    0x1bc: dict(name='block-mark', category='marker', noop=True, confidence='high', source='inference', summary='zero-arg; follows jcc/mov, precedes mov/ret — block boundary'),
    0x1bf: dict(name='call-end', category='marker', noop=True, confidence='med', source='inference', summary='zero-arg; call->0x1bf->stmt-end — end-of-call-statement marker'),
    0x1d2: dict(name='stmt-desc?', category='marker', noop=True, confidence='med', source='harness', summary='2 imm; immediately after stmt-begin 0x1f4 — statement descriptor?'),
    0x1d5: dict(name='cond-block', category='marker', noop=True, confidence='high', source='inference', summary='zero-arg; ALWAYS follows jcc — marks conditional body entry'),
    0x1f4: dict(name='stmt-begin', category='marker', noop=True, confidence='high', source='investigation', summary='zero-arg; opens scripts, pairs with stmt-end 0x1f5'),
    0x1f5: dict(name='stmt-end', category='marker', noop=True, confidence='high', source='investigation', summary='zero-arg; precedes exit/next-stmt, pairs with 0x1f4'),
    0x21b: dict(name='line-id?', category='marker', noop=True, confidence='med', source='harness', summary='1 imm; mov->0x21b->stmt-end; near save/load-messkip — likely line/stmt id, verify not msg-control'),
    0x258: dict(name='decl?', category='marker', noop=True, confidence='low', source='harness', summary='2 imm; runs in a chain right after script-entry 0x259, enumerating ids — prologue declaration/registration?'),
    0x259: dict(name='script-entry', category='marker', noop=True, confidence='low', source='harness', summary='zero-arg; the first instruction of a script (offset 0), opens the decl chain that 0x258 continues — script/prologue entry marker, structural'),
}
