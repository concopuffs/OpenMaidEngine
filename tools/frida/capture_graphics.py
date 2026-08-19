#!/usr/bin/env python3
"""Attach Frida to the running game and log READS from the graphics archives (DATA2/DATA5*.ALF)
with offset+size, in order — so we can correlate them with the engine's set-texture sequence.

The game opens the .ALF archives at startup (before we can attach) and reads CGs from the
persistent handles, so CreateFile never fires during play; we hook ReadFile and resolve each
handle -> path via GetFinalPathNameByHandleW (cached), capturing the read offset.

Flow: game running -> run this (attach) -> replay SC0000's opening -> stop this. Log lands in
build/frida-reads.log as `path<TAB>offset<TAB>size`.

Usage: py -3.11 -u -X utf8 tools/frida_capture_graphics.py [process_name]   (default AGE.EXE)
"""
import frida, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import paths

proc = sys.argv[1] if len(sys.argv) > 1 else "AGE.EXE"
OUT = paths.BUILD / "frida-reads.log"
OUT.parent.mkdir(parents=True, exist_ok=True)
log = open(OUT, "w", encoding="utf-8")

JS = r"""
const k32 = Process.getModuleByName('kernel32.dll');
const GetFinalPathNameByHandleW = new NativeFunction(
    k32.findExportByName('GetFinalPathNameByHandleW'), 'uint32', ['pointer','pointer','uint32','uint32']);
const SetFilePointer = new NativeFunction(
    k32.findExportByName('SetFilePointer'), 'uint32', ['pointer','int32','pointer','uint32']);
const NUL = ptr(0);
const cache = {};

function pathOf(h) {
  const key = h.toString();
  let v = cache[key];
  if (v !== undefined) return v;
  let p = null;
  try {
    const buf = Memory.alloc(1040);
    const n = GetFinalPathNameByHandleW(h, buf, 519, 0);
    if (n > 0 && n < 519) p = buf.readUtf16String();
  } catch (e) {}
  cache[key] = p;
  return p;
}

const rf = k32.findExportByName('ReadFile');
Interceptor.attach(rf, {
  onEnter(args) {
    const p = pathOf(args[0]);
    if (!p || !/(data2|data5)\.alf$|\.agf$/i.test(p)) return;   // graphics archives only
    const size = args[2].toInt32();
    const ov = args[4];
    let off = -1;
    try { off = ov.isNull() ? SetFilePointer(args[0], 0, NUL, 1) : ov.add(8).readU32(); } catch (e) {}
    send({path: p, offset: off, size: size});
  }
});
send({ready: true});
"""

def on_message(msg, data):
    if msg.get("type") != "send":
        if msg.get("type") == "error":
            print("[frida-error]", msg.get("description"))
        return
    pl = msg["payload"]
    if pl.get("ready"):
        print("[frida] ReadFile hook live — replay the opening now.")
        return
    line = f"{pl['path']}\t{pl['offset']}\t{pl['size']}"
    print("READ", line)
    log.write(line + "\n"); log.flush()

try:
    session = frida.attach(proc)
except frida.ProcessNotFoundError:
    print(f"process '{proc}' not found. AGE-like:",
          [(p.pid, p.name) for p in frida.enumerate_processes() if "age" in p.name.lower()])
    sys.exit(2)

script = session.create_script(JS)
script.on("message", on_message)
script.load()
print(f"[frida] attached to {proc}; logging archive reads to {OUT}")
try:
    while True:
        time.sleep(0.5)
except KeyboardInterrupt:
    pass
