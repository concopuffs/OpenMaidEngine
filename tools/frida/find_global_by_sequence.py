#!/usr/bin/env python3
"""Locate the VM int-global G[0x62424] (the CG resId) in memory by a differential value scan --
the confirmed-anchor bootstrap for runtime global observation (docs/asset-resolution-re.md step 2).

G[0x62424] is unambiguously a VM global-int (`mov (global-int 0x62424) ...`), and because
resId == SYS4INI file_number, each DATA2 asset-start ReadFile tells us the EXACT value it holds
at that instant (the asset's file_number). So we self-drive a Cheat-Engine-style scan: on the
first CG load, scan the heap for int32 == fn; on each later load, keep only candidates that now
equal the new fn. The monotonic distinct opening sequence (35,37,39,43,46,122,129,...) collapses
the set to G[0x62424] in a few steps -- no native-copy contamination, no manual timing.

Once found, its address anchors the VM int-global store; from there we derive the address->memory
mapping and read any global live.

Flow: title screen -> attach -> start new game -> advance the opening SLOWLY (one CG at a time).
"""
import json
import sys
import time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import paths

REPO = paths.REPO
INDEX = paths.BUILD / "asset-index.json"


BG_MIN_SIZE = 500_000        # backgrounds are big AGFs (~1MB); portraits/sprites are far smaller


def data2_off2fn():
    """{offset: file_number} for BACKGROUND-sized DATA2 assets only (excludes sprite/portrait
    loads, which churn G[0x62424]/other resId globals between background changes)."""
    idx = json.loads(INDEX.read_text(encoding="utf-8"))
    return {f["offset"]: f["file_number"] for f in idx["files"]
            if f["archive"] == "DATA2.ALF" and f["size"] >= BG_MIN_SIZE}


JS_TEMPLATE = r"""
const OFF2FN = __OFF2FN__;                 // {offset: file_number == resId}
const k32 = Process.getModuleByName('kernel32.dll');
const GetFinalPathNameByHandleW = new NativeFunction(
    k32.findExportByName('GetFinalPathNameByHandleW'), 'uint32', ['pointer','pointer','uint32','uint32']);
const SetFilePointer = new NativeFunction(
    k32.findExportByName('SetFilePointer'), 'uint32', ['pointer','int32','pointer','uint32']);
const NUL = ptr(0); const cache = {};
function pathOf(h){ const k=h.toString(); let v=cache[k]; if(v!==undefined) return v; let p=null;
  try{ const b=Memory.alloc(1040); const n=GetFinalPathNameByHandleW(h,b,519,0);
       if(n>0&&n<519) p=b.readUtf16String(); }catch(e){} cache[k]=p; return p; }

function u32le(v){const b=[v&0xff,(v>>>8)&0xff,(v>>>16)&0xff,(v>>>24)&0xff];
  return b.map(x=>('0'+x.toString(16)).slice(-2)).join(' ');}

const CAP = 600000;
function scanValue(v){
  const out=[]; const pat=u32le(v);
  const ranges=Process.enumerateRanges('rw-');
  for(const r of ranges){
    let m; try{ m=Memory.scanSync(r.base, r.size, pat); }catch(e){ continue; }
    for(const x of m){ out.push(x.address); if(out.length>=CAP) return out; }
  }
  return out;
}

function isStack(a){                          // heuristic: self-referential / return-addr neighbourhood
  const r=Process.findRangeByAddress(a);
  return r && r.size < 0x200000;              // small rw- region = likely a stack
}
let cands=null, lastOff=-1, step=0;
function keepEq(v){ cands=cands.filter(a=>{ try{ return a.readU32()===(v>>>0); }catch(e){ return false; } }); }

Interceptor.attach(k32.findExportByName('ReadFile'), {
  onEnter(args){
    const p=pathOf(args[0]); if(!p || !/data2\.alf$/i.test(p)) return;
    const size=args[2].toInt32(); if(size>4096) return;
    const ov=args[4]; let off=-1;
    try{ off = ov.isNull()? SetFilePointer(args[0],0,NUL,1) : ov.add(8).readU32(); }catch(e){}
    if(!(off in OFF2FN)) return;
    if(off===lastOff) return; lastOff=off;
    const fn=OFF2FN[off];
    if(cands===null){ cands=scanValue(fn); }
    else { keepEq(fn); }
    step++;
    send({step:step, phase:'load', fn:fn, count:cands.length});
    // STABILITY FILTER: 900ms later (during the pause before the next click) the global still
    // holds fn, but transient stack copies have been overwritten -> drop them.
    setTimeout(function(){
      if(cands===null) return;
      keepEq(fn);
      send({step:step, phase:'stable', fn:fn, count:cands.length,
            addrs: cands.length<=12 ? cands.map(a=>({a:a.toString(), stack:isStack(a)})) : []});
    }, 1400);
  }
});
send({ready:true});
"""


def main():
    import frida
    args = [a for a in sys.argv[1:] if not a.startswith("-")]
    proc = args[0] if args else "AGE.EXE"
    js = JS_TEMPLATE.replace("__OFF2FN__", json.dumps(data2_off2fn()))

    def on_message(msg, data):
        if msg.get("type") == "error":
            print("[frida-error]", msg.get("description")); return
        if msg.get("type") != "send":
            return
        pl = msg["payload"]
        if pl.get("ready"):
            print("[frida] scan hook live — advance the opening one CG at a time, pausing ~1.5s each.")
            return
        ph = pl.get("phase")
        print(f"[step {pl['step']} {ph:>6}] resId={pl['fn']} (0x{pl['fn']:x})  candidates={pl['count']}")
        if ph == "stable" and pl.get("addrs"):
            for e in pl["addrs"]:
                print(f"     {e['a']}  {'(stack)' if e['stack'] else '<== STABLE global candidate'}")
            stable = [e for e in pl["addrs"] if not e["stack"]]
            if 0 < len(stable) <= 3:
                print("  >>> stable non-stack survivors — likely G[0x62424].")

    dev = frida.get_local_device()
    target = int(proc) if str(proc).isdigit() else proc
    try:
        session = frida.attach(target)
    except frida.ProcessNotFoundError:
        print("AGE-like:", [(p.pid, p.name) for p in dev.enumerate_processes() if "age" in p.name.lower()])
        return 2
    script = session.create_script(js)
    script.on("message", on_message)
    script.load()
    print(f"[frida] attached to {proc}; narrowing G[0x62424] by the resId sequence.")
    try:
        while True:
            time.sleep(0.5)
    except KeyboardInterrupt:
        pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
