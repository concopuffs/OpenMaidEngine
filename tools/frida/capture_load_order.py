#!/usr/bin/env python3
"""Recover the game's per-asset LOAD ORDER across ALL archives (docs/asset-resolution-re.md).

Assets are typed by name prefix and split across archives: DATA1 = CS/CP/CB/CA/BG/... (ADV
sprites, map sprites, battle & icon portraits, backgrounds), DATA2 = EV/EVM event CGs,
DATA5 = movies. Resolution is per-(archive, type): a set-texture(resId, slot) picks a type via
the slot, and resId indexes within that type. So we must watch EVERY DATA*.ALF, not just DATA2.

Hooks ReadFile on all DATA*.ALF; each asset load begins with header reads at its exact archive
offset, so exact-start reads give the ordered load list -> resolved to (archive, name, prefix,
file_number) via build/asset-index.json. Feed the result to tools/correlate_scope.py to align
with the VM's set-texture(resId) trace and learn the type/scope rule.

Flow: game running -> attach -> replay a scene -> Ctrl-C -> --analyze.
Output: build/frida-load-order.jsonl, build/frida-load-order-result.json.
"""
import json
import re
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
OUT = REPO / "build" / "frida-load-order.jsonl"
INDEX = REPO / "build" / "asset-index.json"


def prefix(name):
    m = re.match(r"([A-Za-z]+)", name)
    return m.group(1) if m else "?"


def index_by_archive():
    """{archive: {offset: (name, file_number, prefix)}} and sorted offset lists for containment."""
    idx = json.loads(INDEX.read_text(encoding="utf-8"))
    exact, arr = {}, {}
    for f in idx["files"]:
        a = f["archive"]
        exact.setdefault(a, {})[f["offset"]] = (f["name"], f["file_number"], prefix(f["name"]))
        arr.setdefault(a, []).append((f["offset"], f["size"], f["name"], f["file_number"]))
    for a in arr:
        arr[a].sort()
    return exact, arr


JS = r"""
const k32 = Process.getModuleByName('kernel32.dll');
const GetFinalPathNameByHandleW = new NativeFunction(
    k32.findExportByName('GetFinalPathNameByHandleW'), 'uint32', ['pointer','pointer','uint32','uint32']);
const SetFilePointer = new NativeFunction(
    k32.findExportByName('SetFilePointer'), 'uint32', ['pointer','int32','pointer','uint32']);
const NUL = ptr(0); const cache = {};
function pathOf(h){ const k=h.toString(); let v=cache[k]; if(v!==undefined) return v; let p=null;
  try{ const b=Memory.alloc(1040); const n=GetFinalPathNameByHandleW(h,b,519,0);
       if(n>0&&n<519) p=b.readUtf16String(); }catch(e){} cache[k]=p; return p; }
Interceptor.attach(k32.findExportByName('ReadFile'), {
  onEnter(args){
    const p = pathOf(args[0]); if(!p) return;
    const m = /DATA(\d)\.ALF$/i.exec(p); if(!m) return;
    const size = args[2].toInt32();
    const ov = args[4]; let off = -1;
    try{ off = ov.isNull()? SetFilePointer(args[0],0,NUL,1) : ov.add(8).readU32(); }catch(e){}
    if(off < 0) return;
    send({kind:'read', archive:'DATA'+m[1]+'.ALF', offset: off, size: size});
  }
});
send({ready:true});
"""


def analyze():
    if not OUT.exists():
        raise SystemExit(f"no log: {OUT}")
    import bisect
    exact, arr = index_by_archive()
    recs = [json.loads(l) for l in OUT.read_text(encoding="utf-8").splitlines() if l.strip()]
    reads = [r for r in recs if r.get("kind") == "read"]
    per_arc = {}
    for r in reads:
        per_arc[r["archive"]] = per_arc.get(r["archive"], 0) + 1
    print(f"{len(reads)} reads across archives: {per_arc}\n")

    def contain(a, off):
        v = arr.get(a)
        if not v:
            return None
        i = bisect.bisect_right(v, (off, float("inf"), "", 0)) - 1
        if i < 0:
            return None
        o, s, n, fn = v[i]
        return (n, fn) if off < o + s else None

    # exact-start order (unambiguous), plus containment first-touch (fuller, noisier)
    exact_order, contain_order, seen_e, seen_c = [], [], set(), set()
    for r in reads:
        a, off = r["archive"], r["offset"]
        if off in exact.get(a, {}):
            name, fn, pfx = exact[a][off]
            if name not in seen_e:
                seen_e.add(name); exact_order.append({"archive": a, "name": name, "file_number": fn, "prefix": pfx})
        hit = contain(a, off)
        if hit and hit[0] not in seen_c:
            seen_c.add(hit[0])
            contain_order.append({"archive": a, "name": hit[0], "file_number": hit[1], "prefix": prefix(hit[0])})

    print(f"=== exact-start load order ({len(exact_order)}) ===")
    for e in exact_order:
        print(f"  {e['archive'][:5]}  {e['prefix']:<4} {e['name']:<14} fn={e['file_number']} (resId 0x{e['file_number']:x})")
    print(f"\n=== containment first-touch ({len(contain_order)}, includes prefetch noise) ===")
    for e in contain_order:
        print(f"  {e['archive'][:5]}  {e['prefix']:<4} {e['name']:<14} fn={e['file_number']}")

    res = {"load_order": exact_order, "containment_order": contain_order,
           "reads_per_archive": per_arc}
    (REPO / "build" / "frida-load-order-result.json").write_text(
        json.dumps(res, ensure_ascii=False, indent=1), encoding="utf-8")
    print("\n-> build/frida-load-order-result.json")
    return 0


def capture(proc):
    import frida
    OUT.parent.mkdir(parents=True, exist_ok=True)
    log = open(OUT, "w", encoding="utf-8")
    exact, _ = index_by_archive()

    def on_message(msg, data):
        if msg.get("type") == "error":
            print("[frida-error]", msg.get("description")); return
        if msg.get("type") != "send":
            return
        pl = msg["payload"]
        if pl.get("ready"):
            print("[frida] ReadFile hook live on ALL DATA*.ALF — replay the scene now."); return
        log.write(json.dumps(pl, ensure_ascii=False) + "\n"); log.flush()
        if pl.get("kind") == "read":
            hit = exact.get(pl["archive"], {}).get(pl["offset"])
            if hit:
                print(f"LOAD {pl['archive'][:5]} {hit[2]:<4} {hit[0]}  fn={hit[1]}")

    target = int(proc) if str(proc).isdigit() else proc
    try:
        session = frida.attach(target)
    except frida.ProcessNotFoundError:
        procs = frida.get_local_device().enumerate_processes()
        print("AGE-like:", [(p.pid, p.name) for p in procs if "age" in p.name.lower()])
        return 2
    script = session.create_script(JS)
    script.on("message", on_message)
    script.load()
    print(f"[frida] attached to {proc}; logging all-archive reads -> {OUT}")
    try:
        while True:
            time.sleep(0.5)
    except KeyboardInterrupt:
        print("\n[frida] stopped. Now: py -3.11 -X utf8 tools/frida/capture_load_order.py --analyze")
    return 0


def main():
    if "--analyze" in sys.argv:
        return analyze()
    proc = next((a for a in sys.argv[1:] if not a.startswith("-")), "AGE.EXE")
    return capture(proc)


if __name__ == "__main__":
    sys.exit(main())
