#!/usr/bin/env python3
"""Capture the native graphics object-manager state, to RE the slot/geometry the engine assigns each
object (the ground truth behind the drift; docs/phase-a-slice-plan.md A2b).

Root cause recap: our VM stubs 0x215 (native gfx-object query) so every draw collapses onto slot 0.
The real per-object slot/geometry lives in a native object-record array inside the engine context.

Robust anchoring: individual gfx handler entry addresses (Kelebek) are approximate (mid-instruction),
so hooking them is unreliable. Instead we hook the ONE confirmed-firing address — the operand-fetch
helper at module +0x1b940 (thiscall; ecx = the engine context 'esi') — grab the context pointer once,
then POLL the object-record array directly: [esi + 0x53d64], stride 120 (0x78) bytes/object, command-
type field at record+0x24, current index at [esi + 0x53d14]. No dependence on fragile handler offsets.

Decode offline + correlate with `Age.Cli gfx SC0000.BIN` (objects load EV049AA/EV052*/BG030A/... with
slots 4..13 per the 0x3239 record table). Poll snapshots over time show how records change per scene.

Flow: attach while a scene is up (or replay the opening) -> polls ~2/s -> Ctrl-C -> build/gfx-objects.jsonl.
Usage: py -3.11 -u -X utf8 tools/frida/capture_gfx_objects.py [pid|AGE.EXE] [seconds]
"""
import json
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
OUT = REPO / "build" / "gfx-objects.jsonl"

OPFETCH_OFF = 0x1b940     # operand-fetch helper (call 0x41b940) — confirmed firing; ecx = context
REC_BASE = 0x53d64        # object-record array offset within the engine context (esi)
REC_STRIDE = 120          # bytes per object record
IDX_OFF = 0x53d14         # current object index
N_RECORDS = 20            # poll the first N object records

JS = r"""
const mod = Process.getModuleByName('AGE.EXE');
const OPFETCH = mod.base.add(%d);
const REC_BASE = %d, REC_STRIDE = %d, IDX_OFF = %d, N = %d;
let ctx = null;
const h = Interceptor.attach(OPFETCH, {
    onEnter(args){
        if (ctx === null){ ctx = this.context.ecx; send({kind:'ctx', esi: ctx.toString()}); }
    }
});
function poll(){
    if (ctx === null){ send({kind:'wait'}); return; }
    let idx = -1, recs = null;
    try { idx = ctx.add(IDX_OFF).readS32(); } catch(e){}
    try { recs = ctx.add(REC_BASE).readByteArray(N * REC_STRIDE); } catch(e){}
    send({kind:'snap', idx:idx}, recs);
}
setInterval(poll, 450);
send({kind:'ready', base: mod.base.toString(), opfetch: OPFETCH.toString()});
"""


def main():
    import frida
    args = [a for a in sys.argv[1:] if not a.startswith("-")]
    proc = args[0] if args else "AGE.EXE"
    secs = int(args[1]) if len(args) > 1 else 20
    OUT.parent.mkdir(parents=True, exist_ok=True)
    log = open(OUT, "w", encoding="utf-8")
    st = {"n": 0, "ctx": None}

    def on_message(msg, data):
        if msg.get("type") == "error":
            print("[frida-error]", msg.get("description")); return
        if msg.get("type") != "send":
            return
        pl = msg["payload"]
        k = pl.get("kind")
        if k == "ready":
            print(f"[frida] hooked operand-fetch @ {pl['opfetch']}; grabbing context + polling records.")
        elif k == "ctx":
            st["ctx"] = pl["esi"]; print(f"[frida] engine context esi = {pl['esi']}")
        elif k == "wait":
            pass
        elif k == "snap":
            rec = {"idx": pl["idx"], "esi": st["ctx"], "records_hex": data.hex() if data else None}
            log.write(json.dumps(rec) + "\n"); log.flush()
            st["n"] += 1
            # quick view: current idx + that record's first 40 bytes
            if data and 0 <= pl["idx"] < N_RECORDS:
                r = data[pl["idx"]*REC_STRIDE:(pl["idx"]+1)*REC_STRIDE]
                print(f"  snap#{st['n']} idx={pl['idx']} rec[:40]={r[:40].hex(' ')}")
            else:
                print(f"  snap#{st['n']} idx={pl['idx']} (no rec)")

    target = int(proc) if str(proc).isdigit() else proc
    try:
        session = frida.attach(target)
    except (frida.ProcessNotFoundError, frida.ServerNotRunningError):
        hits = [(p.pid, p.name) for p in frida.get_local_device().enumerate_processes()
                if p.name.upper().startswith("AGE")]
        print("[frida] AGE.EXE not found. AGE* processes:", hits or "(none)")
        return 2
    script = session.create_script(JS % (OPFETCH_OFF, REC_BASE, REC_STRIDE, IDX_OFF, N_RECORDS))
    script.on("message", on_message)
    script.load()
    print(f"[frida] attached to {proc}; polling {secs}s -> {OUT}")
    time.sleep(secs)
    print(f"\n[frida] done. {st['n']} snapshots -> {OUT}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
