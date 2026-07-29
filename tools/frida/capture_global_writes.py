#!/usr/bin/env python3
"""Capture the engine's global-int writes → a scene-entry state snapshot (docs/engine-re.md
"Differential offset-path oracle" → the pre-scene-state seed).

Hooks the named helper vm_operand_write@0x425fb0 (thiscall: ecx=ctx, [esp+4]=operand index,
[esp+8]=value). The helper receives the PLAINTEXT value before the engine encodes it into the
obfuscated global store (rotate+XOR with the per-session cookie at ctx+0x55120) — which is exactly
why the shelved flat-int32 scans (global-memory-re.md) found nothing, and why hooking the WRITER is
the clean path: no de-obfuscation needed. For each call we replicate the helper's own address math:

    curCtx = *(ctx+0x53d14);  framePc = *(ctx+0x53d2c+curCtx*0x78)
    opnd   = framePc + operand_index*8;  type = *(opnd-4);  index = *(opnd)   ; value = [esp+8]

and log (codebase, index, value) for type 3 = global-int (type 4=float / 6=string / 9,10,12=locals
skipped for v1). Codebase is recorded so a scene's OWN writes can be excluded to get the true
pre-scene boundary.

    py -3.11 -u -X utf8 tools/frida/capture_global_writes.py --spawn  [seconds]   # complete (from boot)
    py -3.11 -u -X utf8 tools/frida/capture_global_writes.py --attach [seconds]   # partial (from now)

--spawn launches AGE.EXE under Frida so hooks are live before instruction 0 (captures TITLE etc.);
--attach hooks the running game (misses everything before attach). Writes build/global-writes.jsonl
(raw, ordered) + build/scene-entry-state.json (last-write-wins GameSession snapshot, loadable via
`Age.Cli ... --state`).
"""
import json
import sys
import time
from collections import OrderedDict
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
GAME_DIR = REPO.parent / "Himegari_Game"
AGE_EXE = GAME_DIR / "AGE.EXE"
RAW = REPO / "build" / "global-writes.jsonl"
SNAP = REPO / "build" / "scene-entry-state.json"
LIVE = REPO / "build" / "tracer-live.flag"

WRITE_OFF = 0x25fb0     # vm_operand_write (0x425fb0)
IDX_OFF   = 0x53d14     # cur_ctx_index
PC_BASE   = 0x53d2c     # frame_pc
CB_BASE   = 0x53d28     # frame_codebase
STRIDE    = 0x78

# vm_operand_write's SEH prologue (from build/engine-dump): push -1; push 0x56a836; mov eax,fs:[0].
# AGE.EXE is PACKED and unpacks in-place, so at --spawn time these bytes are still packed and Frida
# CANNOT intercept ("unable to intercept function at 00425FB0"). We poll until the real prologue appears
# (unpack done) THEN attach — for --attach (already unpacked) the very first poll matches, a no-op cost.
SIG = "0x6a,0xff,0x68,0x36,0xa8,0x56,0x00,0x64,0xa1,0x00,0x00,0x00,0x00"

JS = r"""
const WOFF=%d, IDX_OFF=%d, PC_BASE=%d, CB_BASE=%d, STRIDE=%d;
const SIG=[%s];
const mod = Process.getModuleByName('AGE.EXE');
let buf=[], total=0, calls=0, installed=false;
function flush(){ if(buf.length){ send({kind:'batch', rows:buf}); buf=[]; } }
function install(){
    if (installed) return true;
    let ok=false;
    try{
        const b=new Uint8Array(mod.base.add(WOFF).readByteArray(SIG.length));
        ok = SIG.every((v,i)=>b[i]===v);
    }catch(e){ ok=false; }
    if (!ok) return false;
    Interceptor.attach(mod.base.add(WOFF), {
        onEnter(args){
            calls++;
            const ctx=this.context.ecx, sp=this.context.esp;
            try{
                const opndIdx = sp.add(4).readU32();      // param_2 = operand index
                const value   = sp.add(8).readS32();      // param_3 = plaintext value (signed int)
                const curCtx  = ctx.add(IDX_OFF).readS32();
                if (curCtx<0 || curCtx>=64) return;
                const framePc = ctx.add(PC_BASE + curCtx*STRIDE).readPointer();
                const cb      = ctx.add(CB_BASE + curCtx*STRIDE).readU32();
                const opnd    = framePc.add(opndIdx*8);
                const type    = opnd.sub(4).readU32();
                if (type !== 3) return;                    // v1: global-int only
                const index   = opnd.readU32();
                buf.push([cb>>>0, index>>>0, value]);
                total++;
                if (buf.length>=4000) flush();
            }catch(e){ return; }
        }
    });
    installed = true;
    send({kind:'ready', base: mod.base.toString()});
    return true;
}
if (!install()) { const t=setInterval(()=>{ if(install()) clearInterval(t); }, 10); }
setInterval(flush, 200);
setInterval(()=>{ if (installed) send({kind:'stats', calls:calls, total:total}); }, 3000);
rpc.exports = { flush(){ flush(); return total; }, stats(){ return total; } };
""" % (WRITE_OFF, IDX_OFF, PC_BASE, CB_BASE, STRIDE, SIG)


def run(mode, seconds, exe):
    import frida
    RAW.parent.mkdir(parents=True, exist_ok=True)
    raw = open(RAW, "w", encoding="utf-8")
    n = {"rows": 0}

    def on_message(msg, data):
        if msg.get("type") == "error":
            print("[frida-error]", msg.get("description")); return
        if msg.get("type") != "send":
            return
        pl = msg["payload"]
        if pl.get("kind") == "ready":
            LIVE.write_text("live", encoding="utf-8")   # hook is really installed (post-unpack) now
            print(f"[frida] hook INSTALLED @ {pl['base']} + 0x{WRITE_OFF:x} (vm_operand_write)"); return
        if pl.get("kind") == "stats":
            print(f"[frida] hook fired {pl['calls']}x → {pl['total']} global-int writes captured"); return
        if pl.get("kind") == "batch":
            for cb, idx, val in pl["rows"]:
                raw.write(json.dumps({"codebase": cb, "index": idx, "value": val}) + "\n")
            n["rows"] += len(pl["rows"])

    device = frida.get_local_device()
    if mode == "spawn":
        print(f"[frida] spawning {exe}\n        cwd={GAME_DIR}")
        pid = device.spawn([str(exe)], cwd=str(GAME_DIR))
        try:
            session = device.attach(pid)
            script = session.create_script(JS)
            script.on("message", on_message)
            script.load()                   # script loaded; hook installs itself once the code unpacks
            device.resume(pid)              # MUST reach this, else the game stays suspended (no window)
        except Exception:
            try:
                device.kill(pid)            # don't leave a suspended orphan if setup failed
            except Exception:
                pass
            raise
        print(f"[frida] spawned pid {pid}, resumed. Waiting for unpack, then the hook self-installs.")
        print(f"        Capturing {seconds}s — let it boot to TITLE, then click New Game.")
    else:
        try:
            session = frida.attach("AGE.EXE")
        except frida.ProcessNotFoundError:
            print("[frida] AGE.EXE not running."); raw.close(); return 2
        script = session.create_script(JS)
        script.on("message", on_message)
        script.load()
        print(f"[frida] attached to AGE.EXE. Capturing {seconds}s (partial — misses pre-attach writes).")

    try:
        for _ in range(seconds):
            time.sleep(1)
    except KeyboardInterrupt:
        print("[frida] stopped early (Ctrl-C).")
    try:
        script.exports_sync.flush(); time.sleep(0.3)
    except Exception:
        pass
    try:
        session.detach()
    except Exception:
        pass
    raw.close()
    try:
        LIVE.unlink()
    except OSError:
        pass

    # Build the snapshot (last-write-wins) + a per-codebase breakdown so a scene's own writes are visible.
    globals_lastwin = OrderedDict()
    per_cb = {}
    with open(RAW, encoding="utf-8") as f:
        for line in f:
            r = json.loads(line)
            globals_lastwin[r["index"]] = r["value"]
            per_cb.setdefault(r["codebase"], set()).add(r["index"])
    snap = {"Globals": {str(k): v for k, v in globals_lastwin.items()}, "Strings": {}}
    SNAP.write_text(json.dumps(snap), encoding="utf-8")

    print(f"\n[capture] {n['rows']} global-int writes; {len(globals_lastwin)} distinct globals")
    print(f"[capture] raw   -> {RAW}")
    print(f"[capture] snap  -> {SNAP}  (GameSession shape, load via --state)")
    print(f"[capture] codebases writing globals: {len(per_cb)}")
    for cb, idxs in sorted(per_cb.items(), key=lambda kv: -len(kv[1]))[:8]:
        print(f"          0x{cb:08x}: {len(idxs)} distinct globals")
    # spot-check a couple of the flags the oracle flagged
    for probe in (0x6c1, 0x6d3, 0x62451):
        if probe in globals_lastwin:
            print(f"[capture] G[0x{probe:x}] = {globals_lastwin[probe]}")
    return 0


def main():
    a = sys.argv[1:]
    mode = "spawn" if "--spawn" in a else "attach"
    exe = next((a[i + 1] for i, x in enumerate(a) if x == "--exe" and i + 1 < len(a)), AGE_EXE)
    secs = next((int(x) for x in a if x.isdigit()), 60)
    return run(mode, secs, exe)


if __name__ == "__main__":
    sys.exit(main())
