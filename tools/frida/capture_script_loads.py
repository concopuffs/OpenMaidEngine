#!/usr/bin/env python3
"""Capture native SYS4 script loads after attaching at the title screen.

Hooks ``script_frame_load_resource@0x40e980`` after AGE.EXE unpacks.  Its third
argument is the packed SYS4 resource id; base-table ids resolve directly through
``build/asset-index.json``.  The resulting ordered log is the Phase-B0 answer key
for boot, title, New Game, and top-level scene handoffs.

    py -3.11 -u -X utf8 tools/frida/capture_script_loads.py 30
    py -3.11 -X utf8 tools/frida/capture_script_loads.py --analyze

The probe is read-only.  Launch the original normally, wait for TITLE, then
attach this probe before selecting New Game.  A Frida-gated process-start trial
triggered the installed game's Protection Error 45, so this tool deliberately
does not offer spawn mode.
"""
from __future__ import annotations

import json
import sys
import time
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
import paths  # noqa: E402

OUT = paths.BUILD / "script-loads.jsonl"

LOAD_OFF = 0x0E980
IDX_OFF = 0x53D14
RESOURCE_BASE = 0x53D64
FRAME_STRIDE = 0x78

# push ebp; lea ebp,[esp-0x134]; sub esp,0x134
SIG = "0x55,0x8d,0xac,0x24,0xcc,0xfe,0xff,0xff,0x81,0xec,0x34,0x01,0x00,0x00"

JS = r"""
const LOAD_OFF=%d, IDX_OFF=%d, RESOURCE_BASE=%d, FRAME_STRIDE=%d;
const SIG=[%s];
const mod=Process.getModuleByName('AGE.EXE');
const started=Date.now();
let installed=false, rows=[], total=0;
function flush(){ if(rows.length){ send({kind:'batch', rows:rows}); rows=[]; } }
function install(){
  if(installed) return true;
  let ok=false;
  try {
    const b=new Uint8Array(mod.base.add(LOAD_OFF).readByteArray(SIG.length));
    ok=SIG.every((v,i)=>b[i]===v);
  } catch(e) { ok=false; }
  if(!ok) return false;
  Interceptor.attach(mod.base.add(LOAD_OFF), {
    onEnter(args){
      const ctx=this.context.ecx, sp=this.context.esp;
      try {
        const resource=sp.add(8).readU32();
        const depth=ctx.add(IDX_OFF).readS32();
        let parent=0xffffffff;
        if(depth>0 && depth<64)
          parent=ctx.add(RESOURCE_BASE+(depth-1)*FRAME_STRIDE).readU32();
        rows.push([Date.now()-started, depth, resource>>>0, parent>>>0]);
        total++;
        if(rows.length>=256) flush();
      } catch(e) {}
    }
  });
  installed=true;
  send({kind:'ready', base:mod.base.toString()});
  return true;
}
if(!install()) { const timer=setInterval(()=>{ if(install()) clearInterval(timer); },1); }
setInterval(flush,100);
rpc.exports={ flush(){ flush(); return total; } };
""" % (LOAD_OFF, IDX_OFF, RESOURCE_BASE, FRAME_STRIDE, SIG)


def resource_names(index_path: Path = paths.BUILD / "asset-index.json") -> dict[int, str]:
    doc = json.loads(index_path.read_text(encoding="utf-8"))
    return {int(row["raw_index"]): row["name"] for row in doc["files"]}


def resolve_resource(resource_id: int, names: dict[int, str]) -> str:
    """Resolve base SYS4 ids; retain packed append ids without guessing a table."""
    if resource_id >> 24:
        return f"packed:0x{resource_id:08x}"
    return names.get(resource_id, f"unknown:0x{resource_id:x}")


def analyze(out_path: Path = OUT) -> int:
    if not out_path.exists():
        print(f"[!] no capture: {out_path}")
        return 1
    names = resource_names()
    rows = [json.loads(line) for line in out_path.read_text(encoding="utf-8").splitlines() if line.strip()]
    print(f"=== native SYS4 script loads ({len(rows)}) ===")
    for i, row in enumerate(rows):
        rid = int(row["resource_id"])
        parent = int(row["parent_resource_id"])
        parent_name = "root" if parent == 0xFFFFFFFF else resolve_resource(parent, names)
        print(f"{i:3d} {row['elapsed_ms']:7d} ms  depth={row['depth']:2d}  "
              f"0x{rid:04x} {resolve_resource(rid, names):<18} <- {parent_name}")
    if rows and resolve_resource(int(rows[0]["resource_id"]), names).upper() != "SYSTEM4.BIN":
        print("\n[note] The post-unpack hook did not observe the root SYSTEM4 load; "
              "the list begins at the first later load it could intercept.")
    return 0


def capture(seconds: int) -> int:
    import frida

    names = resource_names()
    count = 0

    try:
        session = frida.attach("AGE.EXE")
    except frida.ProcessNotFoundError:
        print("[frida] AGE.EXE is not running; launch it normally and stop at TITLE first.")
        return 2

    OUT.parent.mkdir(parents=True, exist_ok=True)
    log = OUT.open("w", encoding="utf-8")

    def on_message(message, data):
        nonlocal count
        if message.get("type") == "error":
            print("[frida-error]", message.get("description"))
            return
        if message.get("type") != "send":
            return
        payload = message["payload"]
        if payload.get("kind") == "ready":
            print(f"[frida] script loader hook installed @ {payload['base']} + 0x{LOAD_OFF:x}")
            return
        if payload.get("kind") != "batch":
            return
        for elapsed, depth, resource, parent in payload["rows"]:
            row = {"elapsed_ms": elapsed, "depth": depth, "resource_id": resource,
                   "parent_resource_id": parent}
            log.write(json.dumps(row) + "\n")
            count += 1
            print(f"LOAD depth={depth:2d} 0x{resource:04x} {resolve_resource(resource, names)}")
        log.flush()

    script = session.create_script(JS)
    script.on("message", on_message)
    script.load()
    print(f"[frida] attached; capture {seconds}s. Select New Game now.")

    try:
        for _ in range(seconds):
            time.sleep(1)
    except KeyboardInterrupt:
        print("[frida] stopped early.")
    try:
        script.exports_sync.flush()
        time.sleep(0.2)
    except Exception:
        pass
    try:
        session.detach()
    except Exception:
        pass
    log.close()
    print(f"\n[capture] {count} script loads -> {OUT}")
    return analyze()


def selftest() -> int:
    names = {0: "SYSTEM4.BIN", 0x22: "SC0000.BIN"}
    assert resolve_resource(0, names) == "SYSTEM4.BIN"
    assert resolve_resource(0x22, names) == "SC0000.BIN"
    assert resolve_resource(0x123, names) == "unknown:0x123"
    assert resolve_resource(0x01000022, names) == "packed:0x01000022"
    print("capture_script_loads selftest: OK")
    return 0


def main() -> int:
    args = sys.argv[1:]
    if "--selftest" in args:
        return selftest()
    if "--analyze" in args:
        return analyze()
    seconds = next((int(arg) for arg in args if arg.isdigit()), 60)
    return capture(seconds)


if __name__ == "__main__":
    raise SystemExit(main())
