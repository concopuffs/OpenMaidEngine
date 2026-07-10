#!/usr/bin/env python3
"""Capture native retained-object matrices for port comparison.

Hooks the already-reversed object composite/apply path with plain JavaScript:
  gfx_object_composite             AGE.EXE+0x7f650 (tracks current handle)
  gfx_object_apply_transform_channels AGE.EXE+0x72f00

For every changed matrix it records frame-time, base position, anchor, sampled 4x4 matrix, current/target
scale and translation channels, and their timing fields. The apply hook sees the exact native composition
after T(-anchor) * scale * middle * translation * T(anchor), before the later viewport matrices.

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

JS = r"""
const COMPOSITE_OFF=%d, APPLY_OFF=%d, HANDLE_FILTER=%s;
const mod = Process.getModuleByName('AGE.EXE');
const activeHandle = new Map();
const last = new Map();

function s32(p, off) { return p.add(off).readS32(); }
function u32(p, off) { return p.add(off).readU32(); }
function f32(p, off) { return p.add(off).readFloat(); }
function mat(p, off) {
  const a = [];
  for (let i=0; i<16; i++) a.push(f32(p, off + i*4));
  return a;
}
function v3(p, off) { return [f32(p,off), f32(p,off+4), f32(p,off+8)]; }
function diag(p, off) { return [f32(p,off), f32(p,off+20), f32(p,off+40)]; }
function trans(p, off) { return [f32(p,off+48), f32(p,off+52), f32(p,off+56)]; }
function rounded(a) { return a.map(x => Math.round(x * 10000) / 10000); }

Interceptor.attach(mod.base.add(COMPOSITE_OFF), {
  onEnter(args) {
    const tid = Process.getCurrentThreadId();
    activeHandle.set(tid, args[0].toUInt32());
  },
  onLeave() { activeHandle.delete(Process.getCurrentThreadId()); }
});

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
        kind:'transform',
        t:Date.now(),
        handle:this.handle,
        frameTime:u32(this.ctx,0xb550),
        flags:u32(this.obj,0),
        slot:s32(this.obj,4),
        src:[s32(this.obj,8),s32(this.obj,12),s32(this.obj,16),s32(this.obj,20)],
        anchor:v3(this.obj,0x18),
        base:v3(this.obj,0x24),
        start:u32(this.obj,0x34),
        scaleDelay:s32(this.obj,0x3c),
        transDelay:s32(this.obj,0x44),
        scaleDuration:s32(this.obj,0x50),
        transDuration:s32(this.obj,0x58),
        scaleCurrent:rounded(diag(this.obj,0x6c)),
        scaleTarget:rounded(diag(this.obj,0xac)),
        transCurrent:rounded(trans(this.obj,0x16c)),
        transTarget:rounded(trans(this.obj,0x1ac)),
        matrix:m
      });
    } catch(e) {
      send({kind:'error', message:e.toString()});
    }
  }
});
send({kind:'ready', base:mod.base.toString(),
      composite:mod.base.add(COMPOSITE_OFF).toString(), apply:mod.base.add(APPLY_OFF).toString()});
""" % (COMPOSITE_OFF, APPLY_OFF, "%s")


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
        elif kind == "transform":
            rows.append(payload)
            print(f"  t={payload['frameTime']:>10} handle=0x{payload['handle']:x} "
                  f"base={payload['base'][:2]} anchor={payload['anchor'][:2]} "
                  f"scale={payload['scaleCurrent'][:2]} trans={payload['transCurrent'][:2]}")
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
