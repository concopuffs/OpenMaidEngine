#!/usr/bin/env python3
"""Locate the game's INT-GLOBAL array in memory by a known-value signature scan, so we can
read VM globals live (e.g. G[0x62424] = the CG resId) -- see docs/asset-resolution-re.md step 2.

The `*INIT` scripts write thousands of known constants to known global-int addresses at boot:
`mov (global-int ADDR) IMM`. Our VM addresses globals as a flat int array, so at runtime the
game holds `int_globals[ADDR]` at `B + ADDR*4` for some base B. We build a signature of those
(ADDR, value) pairs, find a long CONTIGUOUS run as a rare multi-dword anchor, `Memory.scan` for
it, and verify each candidate B against many scattered pairs. Unique high-match B = the array.

This unlocks runtime global observation generally (the roadmap's VM-validation cornerstone):
read `resId` (G[0x62424]) directly at each CG load, and watch scope-selector globals.

  build:  py -3.11 -X utf8 tools/frida/find_globals_base.py --build-sig
  scan:   py -3.11 -u -X utf8 tools/frida/find_globals_base.py <pid>
"""
import collections
import json
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import paths

REPO = paths.REPO
SIG = paths.BUILD / "globals-signature.json"
sys.path.insert(0, str(REPO / "tools"))

MOV, T_GINT, T_IMM = 0x55, 3, 0
INIT_SCRIPTS = ["EBINIT", "ITINIT", "SKINIT", "CGINIT"]


def build_signature():
    import paths, sys4load
    pairs = collections.defaultdict(collections.Counter)
    for name in INIT_SCRIPTS:
        p = paths.GAME_DIR / f"{name}.BIN"
        if not p.exists():
            p = paths.DATA1 / f"{name}.BIN"
        if not p.exists():
            continue
        for ins in sys4load.load(p).instructions:
            if (ins.opcode == MOV and len(ins.args) >= 2
                    and ins.args[0][0] == T_GINT and ins.args[1][0] == T_IMM):
                pairs[ins.args[0][1]][ins.args[1][1]] += 1
    # stable, distinctive, single-write addresses
    single = {a: next(iter(vc)) for a, vc in pairs.items()
              if len(vc) == 1 and vc.most_common(1)[0][1] == 1
              and 8 < next(iter(vc)) < 0x7fffffff}

    # longest contiguous run (addr, addr+1, ...) -> rare multi-dword anchor
    addrs = sorted(single)
    best = (None, 0)
    i = 0
    while i < len(addrs):
        j = i
        while j + 1 < len(addrs) and addrs[j + 1] == addrs[j] + 1:
            j += 1
        if j - i + 1 > best[1]:
            best = (addrs[i], j - i + 1)
        i = j + 1
    anchor_addr, anchor_len = best
    anchor = [(anchor_addr + k, single[anchor_addr + k]) for k in range(anchor_len)]
    # scattered verification pairs spread across the address range
    spread = addrs[::max(1, len(addrs) // 120)][:120]
    verify = [(a, single[a]) for a in spread]

    SIG.parent.mkdir(parents=True, exist_ok=True)
    SIG.write_text(json.dumps({"anchor_addr": anchor_addr, "anchor": anchor, "verify": verify},
                              ensure_ascii=False), encoding="utf-8")
    print(f"signature: {len(single)} distinctive pairs; "
          f"anchor run @0x{anchor_addr:x} len {anchor_len} ({anchor_len*4} bytes); "
          f"{len(verify)} verify pairs -> {SIG.relative_to(REPO)}")
    print("anchor values:", [v for _, v in anchor[:12]])
    return 0


JS_TEMPLATE = r"""
const ANCHOR_ADDR = __ANCHOR_ADDR__;
const ANCHOR_VALS = __ANCHOR_VALS__;     // consecutive int32 values at ANCHOR_ADDR..
const VERIFY = __VERIFY__;               // [[addr,val],...]

// build the anchor byte pattern (little-endian int32 each)
function u32le(v){ const b=[v&0xff,(v>>>8)&0xff,(v>>>16)&0xff,(v>>>24)&0xff];
  return b.map(x=>('0'+x.toString(16)).slice(-2)).join(' '); }
const pattern = ANCHOR_VALS.map(u32le).join(' ');

function verifyBase(B){
  let ok=0, tot=0;
  for(const pv of VERIFY){
    tot++;
    try { if(B.add(pv[0]*4).readU32() === (pv[1]>>>0)) ok++; } catch(e){}
  }
  return {ok:ok, tot:tot};
}

const ranges = Process.enumerateRanges('rw-').filter(r=>r.size >= 1024*1024);
let best=null;
// robustness ladder: long anchor is rare but fragile to any changed value; short prefixes
// catch it if a value moved. Each candidate is confirmed by the 120 scattered verify pairs.
const LENS = [ANCHOR_VALS.length, 64, 16, 4].filter((v,i,a)=>v<=ANCHOR_VALS.length && a.indexOf(v)===i);
for(const L of LENS){
  const pat = ANCHOR_VALS.slice(0,L).map(u32le).join(' ');
  let hits=0;
  ranges.forEach(function(r){
    let matches; try { matches = Memory.scanSync(r.base, r.size, pat); } catch(e){ return; }
    if(matches.length > 8000) return;            // too common at this length, skip
    hits += matches.length;
    matches.forEach(function(m){
      const B = m.address.sub(ANCHOR_ADDR*4);
      const v = verifyBase(B);
      if(v.ok >= 30 && (!best || v.ok>best.ok))
        best = {base: B.toString(), ok:v.ok, tot:v.tot, anchor_at: m.address.toString(), anchor_len:L};
    });
  });
  send({phase:'scan', anchor_len:L, hits:hits, found: !!best});
  if(best) break;
}
if(best){
  // read G[0x62424] (the CG resId) as a sanity value
  let resid=null; try{ resid = ptr(best.base).add(0x62424*4).readU32(); }catch(e){}
  best.G_62424 = resid;
  send({phase:'found', best:best});
} else {
  send({phase:'notfound'});
}
"""


def scan(pid):
    import frida
    if not SIG.exists():
        raise SystemExit("no signature; run --build-sig first")
    sig = json.loads(SIG.read_text(encoding="utf-8"))
    js = (JS_TEMPLATE
          .replace("__ANCHOR_ADDR__", str(sig["anchor_addr"]))
          .replace("__ANCHOR_VALS__", json.dumps([v for _, v in sig["anchor"]]))
          .replace("__VERIFY__", json.dumps(sig["verify"])))
    out = {}

    def on_message(msg, data):
        if msg.get("type") == "error":
            print("[frida-error]", msg.get("description")); return
        if msg.get("type") != "send":
            return
        pl = msg["payload"]
        ph = pl.get("phase")
        if ph == "scan":
            print(f"[scan] anchor prefix {pl['anchor_len']} dwords: {pl['hits']} raw hits"
                  + ("  -> base confirmed" if pl.get("found") else ""))
        elif ph == "found":
            b = pl["best"]
            out["base"] = b["base"]
            print(f"[FOUND] int-global base = {b['base']}  (verify {b['ok']}/{b['tot']} pairs, "
                  f"anchor @ {b['anchor_at']})")
            print(f"        G[0x62424] (CG resId right now) = {b['G_62424']} (0x{b['G_62424']:x})"
                  if b.get("G_62424") is not None else "        G[0x62424] unreadable")
        elif ph == "notfound":
            print("[scan] anchor pattern not found in any heap range (globals not resident, "
                  "wrong element size, or array not yet populated)")

    dev = frida.get_local_device()
    target = int(pid) if str(pid).isdigit() else pid
    try:
        session = frida.attach(target)
    except frida.ProcessNotFoundError:
        print("AGE-like:", [(p.pid, p.name) for p in dev.enumerate_processes() if "age" in p.name.lower()])
        return 2
    script = session.create_script(js)
    script.on("message", on_message)
    script.load()
    if "base" in out:
        print(f"\nint-global base found: {out['base']}. Next: hook the loader and read "
              f"G[0x62424] there for definitive (resId,name) pairs.")
    return 0


def main():
    if "--build-sig" in sys.argv:
        return build_signature()
    pid = next((a for a in sys.argv[1:] if not a.startswith("-")), "AGE.EXE")
    return scan(pid)


if __name__ == "__main__":
    sys.exit(main())
