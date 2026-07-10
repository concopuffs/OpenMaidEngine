#!/usr/bin/env python3
"""Capture native retained-object matrices for port comparison.

Hooks the already-reversed object composite/apply path with plain JavaScript:
  gfx_object_composite             AGE.EXE+0x7f650 (tracks current handle)
  gfx_object_apply_transform_channels AGE.EXE+0x72f00

For every changed matrix it records frame-time, integer base/anchor coordinates, sampled 4x4 matrix,
current/target scale, one-shot axis-angle rotation, translation, and cyclic-rotation state. The apply hook
sees the one-shot composition; the composite hook's leave captures the final matrix after cyclic rotation.

Run while the game is already at an ADV passage, then drive the relevant animation manually:
  py -3.11 -u -X utf8 tools/frida/capture_native_transforms.py [seconds] [pid|AGE.EXE] [--handle 0xHANDLE]

Writes build/native-transform-trace.jsonl. Read-only state capture; it does not patch values or alter speed.
"""
import json
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
OUT = REPO / "build" / "native-transform-trace.jsonl"

COMPOSITE_OFF = 0x7F650
APPLY_OFF = 0x72F00
OP21F_WORKER_OFF = 0x7EB70
OP223_WORKER_OFF = 0x7F440
OP234_WORKER_OFF = 0x7F060

JS = r"""
const COMPOSITE_OFF=%d, APPLY_OFF=%d, OP21F=%d, OP223=%d, OP234=%d, HANDLE_FILTER=%s;
const mod = Process.getModuleByName('AGE.EXE');
const activeHandle = new Map();
const last = new Map();
const lastFinal = new Map();

function s32(p, off) { return p.add(off).readS32(); }
function u32(p, off) { return p.add(off).readU32(); }
function f32(p, off) { return p.add(off).readFloat(); }
function mat(p, off) {
  const a = [];
  for (let i=0; i<16; i++) a.push(f32(p, off + i*4));
  return a;
}
function v3f(p, off) { return [f32(p,off), f32(p,off+4), f32(p,off+8)]; }
function v3i(p, off) { return [s32(p,off), s32(p,off+4), s32(p,off+8)]; }
function diag(p, off) { return [f32(p,off), f32(p,off+20), f32(p,off+40)]; }
function trans(p, off) { return [f32(p,off+48), f32(p,off+52), f32(p,off+56)]; }
function rounded(a) { return a.map(x => Math.round(x * 10000) / 10000); }

Interceptor.attach(mod.base.add(COMPOSITE_OFF), {
  onEnter(args) {
    const tid = Process.getCurrentThreadId();
    this.handle = args[0].toUInt32(); this.ctx = this.context.ecx;
    activeHandle.set(tid, this.handle);
  },
  onLeave() {
    activeHandle.delete(Process.getCurrentThreadId());
    if (HANDLE_FILTER !== null && this.handle !== HANDLE_FILTER) return;
    const m = rounded(mat(this.ctx,0xb574)), key=this.handle.toString(16), sig=JSON.stringify(m);
    if (lastFinal.get(key) !== sig) {
      lastFinal.set(key,sig);
      send({kind:'transform-final',t:Date.now(),handle:this.handle,frameTime:u32(this.ctx,0xb550),matrix:m});
    }
  }
});

function stackI(ctx,n) { return ctx.esp.add(4+n*4).readS32(); }
function stackF(ctx,n) { return ctx.esp.add(4+n*4).readFloat(); }
Interceptor.attach(mod.base.add(OP21F), { onEnter() { send({kind:'op',op:'0x21f',handle:stackI(this.context,0),
  delay:stackI(this.context,1),duration:stackI(this.context,2),axis:[stackF(this.context,3),stackF(this.context,4),stackF(this.context,5)],angle:stackF(this.context,6)}); } });
Interceptor.attach(mod.base.add(OP223), { onEnter() { send({kind:'op',op:'0x223',args:Array.from({length:8},(_,i)=>stackI(this.context,i))}); } });
Interceptor.attach(mod.base.add(OP234), { onEnter() { send({kind:'op',op:'0x234',handle:stackI(this.context,0),
  period:stackI(this.context,1),axis:[stackF(this.context,2),stackF(this.context,3),stackF(this.context,4)]}); } });

Interceptor.attach(mod.base.add(APPLY_OFF), {
  onEnter(args) {
    this.tid = Process.getCurrentThreadId();
    this.handle = activeHandle.has(this.tid) ? activeHandle.get(this.tid) : null;
    this.ctx = this.context.ecx;
    this.obj = args[0];
    this.outMatrix = args[1];
  },
  onLeave() {
    if (this.handle === null || (HANDLE_FILTER !== null && this.handle !== HANDLE_FILTER)) return;
    try {
      const m = rounded(mat(this.outMatrix, 0));
      const key = this.handle.toString(16);
      const sig = JSON.stringify(m);
      if (last.get(key) === sig) return;
      last.set(key, sig);
      send({
        kind:'transform', stage:'one-shot',
        t:Date.now(),
        handle:this.handle,
        frameTime:u32(this.ctx,0xb550),
        flags:u32(this.obj,0),
        slot:s32(this.obj,4),
        src:[s32(this.obj,8),s32(this.obj,12),s32(this.obj,16),s32(this.obj,20)],
        anchor:v3i(this.obj,0x18),
        base:v3i(this.obj,0x24),
        start:u32(this.obj,0x34),
        scaleDelay:s32(this.obj,0x3c),
        transDelay:s32(this.obj,0x44),
        scaleDuration:s32(this.obj,0x50),
        rotationDuration:s32(this.obj,0x54),
        transDuration:s32(this.obj,0x58),
        scaleCurrent:rounded(diag(this.obj,0x6c)),
        scaleTarget:rounded(diag(this.obj,0xac)),
        rotationCurrentAxis:rounded(v3f(this.obj,0x1ec)),
        rotationCurrentAngle:f32(this.obj,0x204),
        rotationTargetAxis:rounded(v3f(this.obj,0x1f8)),
        rotationTargetAngle:f32(this.obj,0x208),
        transCurrent:rounded(trans(this.obj,0x16c)),
        transTarget:rounded(trans(this.obj,0x1ac)),
        cycleStart:u32(this.obj,0x214),
        cyclePeriod:u32(this.obj,0x228),
        cycleAxis:rounded(v3f(this.obj,0x244)),
        matrix:m
      });
    } catch(e) {
      send({kind:'error', message:e.toString()});
    }
  }
});
send({kind:'ready', base:mod.base.toString(),
      composite:mod.base.add(COMPOSITE_OFF).toString(), apply:mod.base.add(APPLY_OFF).toString()});
""" % (COMPOSITE_OFF, APPLY_OFF, OP21F_WORKER_OFF, OP223_WORKER_OFF, OP234_WORKER_OFF, "%s")


def main():
    import frida

    args = sys.argv[1:]
    seconds = 20
    proc = "AGE.EXE"
    handle = None
    positional = []
    i = 0
    while i < len(args):
        if args[i] == "--handle" and i + 1 < len(args):
            handle = int(args[i + 1], 0)
            i += 2
        else:
            positional.append(args[i])
            i += 1
    if positional and positional[0].isdigit():
        seconds = int(positional.pop(0))
    if positional:
        proc = positional[0]

    handle_js = "null" if handle is None else str(handle)
    source = JS % handle_js
    OUT.parent.mkdir(parents=True, exist_ok=True)
    rows = []

    def on_message(msg, data):
        if msg.get("type") == "error":
            print("[frida-error]", msg.get("description"))
            return
        if msg.get("type") != "send":
            return
        payload = msg["payload"]
        kind = payload.get("kind")
        if kind == "ready":
            print(f"[frida] native transform hooks live: composite={payload['composite']} apply={payload['apply']}")
        elif kind in ("transform", "transform-final"):
            rows.append(payload)
            print(f"  t={payload['frameTime']:>10} handle=0x{payload['handle']:x} "
                  f"stage={payload.get('stage', 'final')}")
        elif kind == "op":
            rows.append(payload)
            print(f"  {payload['op']} {payload}")
        elif kind == "error":
            print("[capture-error]", payload.get("message"))

    target = int(proc) if str(proc).isdigit() else proc
    try:
        session = frida.attach(target)
    except frida.ProcessNotFoundError:
        print("[frida] AGE.EXE not found; start the native game and enter the target ADV passage first.")
        return 2
    script = session.create_script(source)
    script.on("message", on_message)
    script.load()
    print(f"[frida] capturing {seconds}s; drive the target animation now.")
    try:
        time.sleep(seconds)
    except KeyboardInterrupt:
        pass
    try:
        session.detach()
    except Exception:
        pass
    with OUT.open("w", encoding="utf-8") as f:
        for row in rows:
            f.write(json.dumps(row, ensure_ascii=False) + "\n")
    print(f"[frida] wrote {len(rows)} changed matrices -> {OUT}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
