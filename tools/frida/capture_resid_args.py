#!/usr/bin/env python3
"""Phase 2 of the native resId->filename crack (see docs/asset-resolution-re.md step 2a).

Phase 1 (locate_resource_load.py) found the stable asset-load call chain in AGE.EXE:
    AGE.EXE+0x16d5d7  ->  AGE.EXE+0x74f1f  ->  AGE.EXE+0x397b  ->  ReadFile
This hooks the two upper frames and, at entry, dumps their arguments (raw dwords + any
ASCII a pointer argument targets), tagged with thread id. It also keeps the ReadFile ->
offset -> asset-name resolver. Interleaving the two streams lets us pair each load call
with the asset it produced and find which argument carries the resId (== 37 for EV052CA,
39 EV052DA, 43 EV052DC, 46 EV052DB) or a pointer to the SYS4INI entry (name / file_number).
Once identified, that argument IS the resId->name mapping at the source. Read-only.

Flow: game running -> this attaches -> replay the opening CGs -> Ctrl-C -> --analyze.
Output: build/frida-resid-args.jsonl (ordered events).
"""
import json
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
OUT = REPO / "build" / "frida-resid-args.jsonl"
INDEX = REPO / "build" / "asset-index.json"
HANDLER_OFFSETS = [0x74f1f]     # phase 1 chain; 0x16d5d7 is a return addr (not a callable entry)


def data2_offsets():
    idx = json.loads(INDEX.read_text(encoding="utf-8"))
    return {f["offset"]: (f["name"], f["file_number"])
            for f in idx["files"] if f["archive"] == "DATA2.ALF"}


JS_TEMPLATE = r"""
const OFFSETS = new Set(__OFFSETS__);
const HANDLERS = __HANDLERS__;
const PS = Process.pointerSize;
const age = Process.getModuleByName('AGE.EXE');

// --- load-handler arg dumps ---
function scanAscii(base, len) {          // printable ASCII runs (>=4) as "hexoff:text"
  const out = [];
  try {
    const u = new Uint8Array(base.readByteArray(len));
    let start = -1;
    for (let i = 0; i <= u.length; i++) {
      const c = i < u.length ? u[i] : 0;
      if (c >= 0x20 && c < 0x7f) { if (start < 0) start = i; }
      else { if (start >= 0 && i - start >= 4)
               out.push(start.toString(16) + ':' + String.fromCharCode.apply(null, u.subarray(start, i)));
             start = -1; }
    }
  } catch (e) {}
  return out;
}
HANDLERS.forEach(function(off) {
  const addr = age.base.add(off);
  Interceptor.attach(addr, {
    onEnter(args) {
      const sp = this.context.sp, ebp = this.context.ebp;
      const raw = [], cargs = [];
      for (let i = 1; i <= 8; i++) {           // [sp+PS*i] = this fn's arg i
        try { raw.push(sp.add(PS * i).readPointer().toString()); } catch (e) { raw.push('0x0'); }
      }
      for (let i = 2; i <= 9; i++) {           // [ebp+PS*i] = CALLER's args (ebp is caller's at entry)
        try { cargs.push(ebp.add(PS * i).readPointer().toString()); } catch (e) { cargs.push('0x0'); }
      }
      // scan the loader context object (arg1) and any pointer arg's target for ASCII (filenames?)
      const ctx = scanAscii(ptr(raw[0]), 0x400);
      const argStr = {};
      raw.concat(cargs).forEach(function(v, k) {
        const s = scanAscii(ptr(v), 0x48);
        if (s.length) argStr[k] = s;
      });
      send({kind: 'call', off: '0x' + off.toString(16), tid: this.threadId,
            ret: '0x' + this.returnAddress.sub(age.base).toString(16),
            args: raw, cargs: cargs, ctx: ctx, argStr: argStr});
    }
  });
});

// --- ReadFile -> asset-start resolver (ground-truth name per load) ---
const k32 = Process.getModuleByName('kernel32.dll');
const GetFinalPathNameByHandleW = new NativeFunction(
    k32.findExportByName('GetFinalPathNameByHandleW'), 'uint32', ['pointer','pointer','uint32','uint32']);
const SetFilePointer = new NativeFunction(
    k32.findExportByName('SetFilePointer'), 'uint32', ['pointer','int32','pointer','uint32']);
const NUL = ptr(0); const cache = {};
function pathOf(h) { const key = h.toString(); let v = cache[key]; if (v !== undefined) return v;
  let p = null; try { const buf = Memory.alloc(1040);
    const n = GetFinalPathNameByHandleW(h, buf, 519, 0);
    if (n > 0 && n < 519) p = buf.readUtf16String(); } catch (e) {} cache[key] = p; return p; }
Interceptor.attach(k32.findExportByName('ReadFile'), {
  onEnter(args) {
    const p = pathOf(args[0]); if (!p || !/data2\.alf$/i.test(p)) return;
    const size = args[2].toInt32(); if (size > 4096) return;
    const ov = args[4]; let off = -1;
    try { off = ov.isNull() ? SetFilePointer(args[0], 0, NUL, 1) : ov.add(8).readU32(); } catch (e) {}
    if (!OFFSETS.has(off)) return;
    send({kind: 'read', tid: this.threadId, offset: off});
  }
});
send({ready: true});
"""


def analyze():
    if not OUT.exists():
        raise SystemExit(f"no log: {OUT}")
    import re
    recs = [json.loads(l) for l in OUT.read_text(encoding="utf-8").splitlines() if l.strip()]
    calls = [r for r in recs if r["kind"] == "call"]
    reads = [r for r in recs if r["kind"] == "read"]
    print(f"{len(calls)} decode calls, {len(reads)} reads\n")

    # every ASCII string the hook surfaced (ctx object + pointer-arg targets)
    def strings_of(c):
        out = list(c.get("ctx", []))
        for v in c.get("argStr", {}).values():
            out.extend(v)
        return [s.split(":", 1)[1] for s in out]

    seen = {}
    for c in calls:
        for s in strings_of(c):
            seen[s] = seen.get(s, 0) + 1
    evlike = {s: n for s, n in seen.items() if re.search(r"EVM?\d|\.AGF|AGF", s, re.I)}
    print(f"distinct ASCII strings surfaced: {len(seen)}")
    print(f"asset-name-like strings ({len(evlike)}):")
    for s, n in sorted(evlike.items(), key=lambda kv: -kv[1])[:60]:
        print(f"   x{n:<4} {s!r}")
    if not evlike:
        print("   (none — filename not in the context object; try the caller frame / go one level up)")
        print("\n   top non-EV strings (for orientation):")
        for s, n in sorted(seen.items(), key=lambda kv: -kv[1])[:20]:
            print(f"   x{n:<4} {s!r}")
    return 0


def capture(proc):
    import frida
    offs = data2_offsets()
    js = (JS_TEMPLATE
          .replace("__OFFSETS__", json.dumps(sorted(offs.keys())))
          .replace("__HANDLERS__", json.dumps(HANDLER_OFFSETS)))
    OUT.parent.mkdir(parents=True, exist_ok=True)
    log = open(OUT, "w", encoding="utf-8")

    def on_message(msg, data):
        if msg.get("type") == "error":
            print("[frida-error]", msg.get("description")); return
        if msg.get("type") != "send":
            return
        pl = msg["payload"]
        if pl.get("ready"):
            print("[frida] resId-arg hooks live — replay the opening CGs now."); return
        log.write(json.dumps(pl, ensure_ascii=False) + "\n"); log.flush()
        if pl["kind"] == "read":
            name, fn = offs.get(pl["offset"], ("?", -1))
            print(f"READ {name} (fn={fn})")
        else:
            import re
            names = [s.split(":", 1)[1] for s in pl.get("ctx", [])
                     if re.search(r"EVM?\d|\.AGF", s.split(":", 1)[1], re.I)]
            if names:
                print(f"  call ret=AGE.EXE+{pl['ret']} names={names}")

    target = int(proc) if proc.isdigit() else proc
    try:
        session = frida.attach(target)
    except frida.ProcessNotFoundError:
        procs = frida.get_local_device().enumerate_processes()
        print(f"'{proc}' not found. AGE-like:", [(p.pid, p.name) for p in procs if "age" in p.name.lower()])
        return 2
    script = session.create_script(js)
    script.on("message", on_message)
    script.load()
    print(f"[frida] attached to {proc}; hooks at AGE.EXE+{[hex(h) for h in HANDLER_OFFSETS]}; log -> {OUT}")
    try:
        while True:
            time.sleep(0.5)
    except KeyboardInterrupt:
        print("\n[frida] stopped. Now: py -3.11 -X utf8 tools/frida/capture_resid_args.py --analyze")
    return 0


def main():
    if "--analyze" in sys.argv:
        return analyze()
    proc = next((a for a in sys.argv[1:] if not a.startswith("-")), "AGE.EXE")
    return capture(proc)


if __name__ == "__main__":
    sys.exit(main())
