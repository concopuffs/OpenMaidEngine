#!/usr/bin/env python3
"""Phase 1 of the native resId->filename crack: LOCATE the resource-load / set-texture
handler in the running game by back-tracing every asset-open (see docs/asset-resolution-re.md
step 2, option a).

Idea: to issue a ReadFile at an asset's exact archive offset the game must have *already*
resolved resId -> (archive, offset) inside its native load-by-id / set-texture (op 0x1f9)
handler. So at each asset-start read, a stack backtrace passes straight through that handler.
We backtrace only on reads whose offset EXACTLY equals a DATA2 asset offset (from
build/asset-index.json), resolve the offset -> asset name, and log module-relative frames.
Frames are AGE.EXE-relative (base subtracted) so they're stable across runs despite ASLR.

Offline, `aggregate` ranks the AGE.EXE return addresses that recur across the MOST distinct
assets: the load-by-id / set-texture handler is the frame common to every asset-open. That
address (module+offset) becomes the hook target for phase 2 (read the resId argument ->
definitive resId->name table). No game state is modified.

Flow (see tools/frida/README.md):
  1. Launch the game (via `AGE Patch.exe`) to the title.
  2. py -3.11 -u -X utf8 tools/frida/locate_resource_load.py
  3. Start a new game so the SC0000 opening auto-plays; let a dozen CGs load.
  4. Ctrl-C. Then: py -3.11 -X utf8 tools/frida/locate_resource_load.py --aggregate

Output: build/frida-resource-bt.jsonl  ({name, offset, size, frames:[...]} per asset-open).
"""
import json
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]           # age-reimpl/
OUT = REPO / "build" / "frida-resource-bt.jsonl"
INDEX = REPO / "build" / "asset-index.json"


def load_data2_offsets():
    """{offset -> name} for DATA2.ALF from the asset index (asset-start detector + resolver)."""
    idx = json.loads(INDEX.read_text(encoding="utf-8"))
    return {f["offset"]: f["name"] for f in idx["files"] if f["archive"] == "DATA2.ALF"}


def aggregate():
    """Rank AGE.EXE-relative frames by how many DISTINCT assets they appear under."""
    if not OUT.exists():
        raise SystemExit(f"no capture log: {OUT} (run the capture first)")
    from collections import defaultdict
    assets_per_frame = defaultdict(set)   # frame-string -> set(asset names)
    depth_of_frame = defaultdict(list)    # frame-string -> [stack depths]
    n = 0
    for ln in OUT.read_text(encoding="utf-8").splitlines():
        if not ln.strip():
            continue
        rec = json.loads(ln)
        n += 1
        for depth, fr in enumerate(rec.get("frames", [])):
            if fr.startswith("AGE.EXE+"):          # ignore kernel32/ntdll/CRT frames
                assets_per_frame[fr].add(rec["name"])
                depth_of_frame[fr].append(depth)
    distinct_assets = {rec["name"] for rec in
                       (json.loads(l) for l in OUT.read_text(encoding="utf-8").splitlines() if l.strip())}
    print(f"{n} asset-open events over {len(distinct_assets)} distinct assets")
    print("AGE.EXE frames ranked by distinct-asset coverage (handler = covers ~all):")
    ranked = sorted(assets_per_frame.items(), key=lambda kv: (-len(kv[1]), fr_depth(depth_of_frame[kv[0]])))
    for fr, assets in ranked[:25]:
        d = depth_of_frame[fr]
        print(f"  {fr:<22} assets={len(assets):<3} avg_depth={sum(d)/len(d):.1f}")
    print("\nThe handler is the frame covering the most distinct assets at a shallow, stable depth.")
    print("Hook it in phase 2 to read the resId argument.")
    return 0


def fr_depth(depths):
    return sum(depths) / len(depths)


JS_TEMPLATE = r"""
const OFFSETS = new Set(__OFFSETS__);        // exact DATA2 asset-start offsets
const k32 = Process.getModuleByName('kernel32.dll');
const GetFinalPathNameByHandleW = new NativeFunction(
    k32.findExportByName('GetFinalPathNameByHandleW'), 'uint32', ['pointer','pointer','uint32','uint32']);
const SetFilePointer = new NativeFunction(
    k32.findExportByName('SetFilePointer'), 'uint32', ['pointer','int32','pointer','uint32']);
const NUL = ptr(0);
const cache = {};
function pathOf(h) {
  const key = h.toString();
  let v = cache[key]; if (v !== undefined) return v;
  let p = null;
  try { const buf = Memory.alloc(1040);
        const n = GetFinalPathNameByHandleW(h, buf, 519, 0);
        if (n > 0 && n < 519) p = buf.readUtf16String(); } catch (e) {}
  cache[key] = p; return p;
}
function frameStr(addr) {
  const m = Process.findModuleByAddress(addr);
  if (!m) return addr.toString();
  return m.name + '+0x' + addr.sub(m.base).toString(16);
}
const rf = k32.findExportByName('ReadFile');
Interceptor.attach(rf, {
  onEnter(args) {
    const p = pathOf(args[0]);
    if (!p || !/data2\.alf$/i.test(p)) return;
    const size = args[2].toInt32();
    if (size > 4096) return;                 // header reads only (asset-start burst)
    const ov = args[4];
    let off = -1;
    try { off = ov.isNull() ? SetFilePointer(args[0], 0, NUL, 1) : ov.add(8).readU32(); } catch (e) {}
    if (!OFFSETS.has(off)) return;           // only exact asset-start offsets
    let frames = [];
    try {
      frames = Thread.backtrace(this.context, Backtracer.ACCURATE).map(frameStr);
    } catch (e) {
      try { frames = Thread.backtrace(this.context, Backtracer.FUZZY).map(frameStr); } catch (e2) {}
    }
    send({offset: off, size: size, frames: frames});
  }
});
send({ready: true});
"""


def capture(proc):
    import frida
    offsets = load_data2_offsets()
    js = JS_TEMPLATE.replace("__OFFSETS__", json.dumps(sorted(offsets.keys())))
    OUT.parent.mkdir(parents=True, exist_ok=True)
    log = open(OUT, "w", encoding="utf-8")
    seen = set()

    def on_message(msg, data):
        if msg.get("type") == "error":
            print("[frida-error]", msg.get("description")); return
        if msg.get("type") != "send":
            return
        pl = msg["payload"]
        if pl.get("ready"):
            print("[frida] backtrace hook live — start a new game; let the opening load CGs.")
            return
        name = offsets.get(pl["offset"], "?")
        rec = {"name": name, "offset": pl["offset"], "size": pl["size"], "frames": pl["frames"]}
        log.write(json.dumps(rec, ensure_ascii=False) + "\n"); log.flush()
        if name not in seen:
            seen.add(name)
            age = [f for f in pl["frames"] if f.startswith("AGE.EXE+")]
            print(f"OPEN {name}  ({len(pl['frames'])} frames, {len(age)} in AGE.EXE)")
            for f in pl["frames"][:8]:
                print("      " + f)

    target = int(proc) if proc.isdigit() else proc
    try:
        session = frida.attach(target)
    except frida.ProcessNotFoundError:
        procs = frida.get_local_device().enumerate_processes()   # frida 17.x: device method
        print(f"process '{proc}' not found. AGE-like:",
              [(p.pid, p.name) for p in procs if "age" in p.name.lower()])
        return 2
    script = session.create_script(js)
    script.on("message", on_message)
    script.load()
    print(f"[frida] attached to {proc}; {len(offsets)} DATA2 asset offsets loaded; log -> {OUT}")
    try:
        while True:
            time.sleep(0.5)
    except KeyboardInterrupt:
        print("\n[frida] stopped. Now: py -3.11 -X utf8 tools/frida/locate_resource_load.py --aggregate")
    return 0


def main():
    if "--aggregate" in sys.argv:
        return aggregate()
    proc = next((a for a in sys.argv[1:] if not a.startswith("-")), "AGE.EXE")
    return capture(proc)


if __name__ == "__main__":
    sys.exit(main())
