# DO NOT EDIT -- generated from vm-map/opcodes.toml by tools/opcodes_build.py --build

"""Inferred Himegari opcode semantics (generated). sys4load reads INFERRED[op]['name']."""
from __future__ import annotations

INFERRED: dict[int, dict] = {
    0x71: dict(name='label-def', category='structural', noop=True, confidence='high', source='investigation', summary='1 imm; count == T1 table size -> the label/anchor T1 indexes. v1 no-op; revisit if menu/callback dispatch looks up by id'),
    0x7a: dict(name='text-param?', category='adv', noop=False, confidence='med', source='inference', summary='3 args (imm/computed/imm); sub computes a value then 0x7a then show-text — text speed/wait/window param'),
    0x90: dict(name='hotspot-branch', category='input', noop=True, confidence='high', source='investigation', summary='cursor/input hotspot hit-test: rect (x,y,w,h) -> 3-way branch on interaction, else fall through to pc+1'),
    0x97: dict(name='hotspot-reg?', category='input', noop=True, confidence='med', source='inference', summary='companion register-hotspot / set-widget-action (argc5: v1 v2 1 1 <action-id>; NO code targets)'),
    0xb6: dict(name='snd-ctrl?', category='audio', noop=False, confidence='low', source='inference', summary='1 imm; self-chains, 0x41D family near play-sound-effect/0xb5 — sound channel/volume/stop control'),
    0x1a2: dict(name='resolve-handle?', category='compute', noop=False, confidence='low', source='inference', summary='1 local-ptr from lookup-array, then create-texture — resolves a looked-up resource/handle'),
    0x1bc: dict(name='block-mark', category='marker', noop=True, confidence='high', source='inference', summary='zero-arg; follows jcc/mov, precedes mov/ret — block boundary'),
    0x1bf: dict(name='call-end', category='marker', noop=True, confidence='med', source='inference', summary='zero-arg; call->0x1bf->stmt-end — end-of-call-statement marker'),
    0x1d2: dict(name='stmt-desc?', category='marker', noop=True, confidence='med', source='harness', summary='2 imm; immediately after stmt-begin 0x1f4 — statement descriptor?'),
    0x1d5: dict(name='cond-block', category='marker', noop=True, confidence='high', source='inference', summary='zero-arg; ALWAYS follows jcc — marks conditional body entry'),
    0x1f4: dict(name='stmt-begin', category='marker', noop=True, confidence='high', source='investigation', summary='zero-arg; opens scripts, pairs with stmt-end 0x1f5'),
    0x1f5: dict(name='stmt-end', category='marker', noop=True, confidence='high', source='investigation', summary='zero-arg; precedes exit/next-stmt, pairs with 0x1f4'),
    0x1f7: dict(name='ui-elem?', category='draw', noop=False, confidence='med', source='inference', summary='2 args; 0x420 family, pairs with 0x1fa — create/begin a UI element'),
    0x1fa: dict(name='ui-clear?', category='draw', noop=False, confidence='med', source='inference', summary='1 arg (element id); follows 0x1f7 — show/hide/clear UI element by id'),
    0x1ff: dict(name='draw?', category='draw', noop=False, confidence='low', source='inference', summary='4 args (global+imms); follows 0x217, then call'),
    0x202: dict(name='draw-blit?', category='draw', noop=False, confidence='med', source='inference', summary='5 args (coords/sizes); preceded by coord arithmetic, near draw ops'),
    0x203: dict(name='draw?', category='draw', noop=False, confidence='med', source='inference', summary='4 args; chains with 0x202/draw-texture'),
    0x215: dict(name='query-gfx-object?', category='draw', noop=False, confidence='med', source='investigation', summary="0x215 (out)(handle_id) — queries the native graphics-object manager by element handle-id (the value in 0x62455[idx], often +1/+2 for a sub-element); writes the object's slot/status into `out`, sign-tested (gre/lt 0) to drive label_12649's slot-select branch and set the working slot G[0x62452]. KEYSTONE for per-object slot selection — stubbing it collapses every draw onto slot 0, so the anchor-preserve geometry reads foreign-sized textures → cumulative bg/sprite drift (see docs/phase-a-slice-plan.md A2b-Geometry). Reads native object-manager state (NOT VM-computable). Exact return semantics: RE via unicorn (native handler @0x421160)."),
    0x217: dict(name='gfx-geom?', category='draw', noop=False, confidence='low', source='inference', summary='4 global-ints; part of a 0x217/0x218/0x21a geometry chain'),
    0x218: dict(name='gfx-geom?', category='draw', noop=False, confidence='low', source='inference', summary='4 global-ints; chains with 0x21a/0x217'),
    0x21a: dict(name='gfx-geom?', category='draw', noop=False, confidence='low', source='inference', summary='4 global-ints; chains with 0x218/0x217'),
    0x21b: dict(name='line-id?', category='marker', noop=True, confidence='med', source='harness', summary='1 imm; mov->0x21b->stmt-end; near save/load-messkip — likely line/stmt id, verify not msg-control'),
    0x258: dict(name='decl?', category='marker', noop=True, confidence='low', source='harness', summary='2 imm; runs in a chain right after script-entry 0x259, enumerating ids — prologue declaration/registration?'),
}
