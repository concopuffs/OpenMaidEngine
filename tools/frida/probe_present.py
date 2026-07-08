#!/usr/bin/env python3
"""Pin the displayed-frame rate by hooking the present path (docs/engine-re.md "Frame cadence").

The frame-cadence probe showed the engine spin-waits on timeGetTime (~118k/sec) and busy-pumps
PeekMessageA (~12k/sec), so no message/timing API fires once per frame. The true per-frame signal is
the present — a DirectDraw surface Blt/Flip (a COM vtable method, not an export) or, if windowed-GDI,
a gdi32 blit. Both live in SYSTEM DLLs, so hooking them is anti-tamper-safe (unlike engine code).

Approach: grab ctx via the proven operand-fetch hook (0x41b940), scan the engine context for pointers
whose vtable lands in ddraw.dll (= surface COM objects), and hook Blt(vtbl[5]) / BltFast(vtbl[7]) /
Flip(vtbl[11]) on each distinct vtable found. Also hook gdi32 blits (BitBlt/StretchBlt/StretchDIBits/
SetDIBitsToDevice). Report each candidate's call rate; the one that sits at a steady ~display rate
(30-120/sec) is the frame signal.

Run (game running, sitting in / actively playing an ADV scene):
    py -3.11 -X utf8 tools/frida/probe_present.py [seconds]
Just play normally (no Ctrl needed — frame rate is mode-independent). Writes build/frida-present.jsonl.
"""
import json
import statistics
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
OUT = REPO / "build" / "frida-present.jsonl"

OPFETCH_OFF = 0x1b940   # operand-fetch (0x41b940), ecx=ctx — proven-safe engine hook to grab ctx
CTX_SCAN    = 0x200000  # bytes of the engine context to scan for the d3d9 device pointer

JS = r"""
const OPFETCH_OFF = %d, CTX_SCAN = %d;
const mod = Process.getModuleByName('AGE.EXE');
let ctx = null, scanned = false;
const cnt = {};   // label -> count
function bump(label){ cnt[label] = (cnt[label]||0) + 1; }

// IDirect3DDevice9 vtable slot indices (0-based): Present=17, BeginScene=41, EndScene=42.
// (Present/EndScene fire once per displayed frame -> the frame clock.)
const SLOTS = { 17: 'd3d9.Present', 42: 'd3d9.EndScene', 41: 'd3d9.BeginScene' };

function scanForSurfaces(){
    let d3d = null; try { d3d = Process.getModuleByName('d3d9.dll'); } catch(e){}
    if (!d3d) { send({kind:'note', msg:'d3d9.dll not loaded'}); return; }
    const lo = d3d.base, hi = d3d.base.add(d3d.size);
    const inD3D = (p) => p.compare(lo) >= 0 && p.compare(hi) < 0;
    const vtables = new Set();
    for (let off = 0; off < CTX_SCAN; off += 4) {
        let obj; try { obj = ctx.add(off).readPointer(); } catch(e){ continue; }
        if (obj.isNull() || inD3D(obj)) continue;           // want a heap COM object whose *obj is a d3d9 vtable
        let vt; try { vt = obj.readPointer(); } catch(e){ continue; }
        if (!inD3D(vt)) continue;
        vtables.add(vt.toString());
    }
    // Identify the DEVICE: its vtable has ~119 methods all in d3d9; the factory/textures far fewer.
    let best = null, bestN = 0;
    for (const vs of vtables) {
        const vt = ptr(vs);
        let n = 0;
        for (let s = 0; s < 120; s++) {
            let fn; try { fn = vt.add(s*4).readPointer(); } catch(e){ break; }
            if (inD3D(fn)) n++; else if (s > 3) break;       // stop at first non-d3d slot past IUnknown
        }
        if (n > bestN) { bestN = n; best = vt; }
    }
    send({kind:'note', msg:'d3d9 vtables in ctx: ' + vtables.size + '; largest method run = ' + bestN
                          + (bestN >= 60 ? ' (device found)' : ' (no device-sized vtable)')});
    if (best && bestN >= 60) {
        for (const slot in SLOTS) {
            let fn; try { fn = best.add(parseInt(slot)*4).readPointer(); } catch(e){ continue; }
            if (!inD3D(fn)) continue;
            const label = SLOTS[slot];
            try { Interceptor.attach(fn, { onEnter(){ bump(label); } }); send({kind:'note', msg:'hooked '+label+' @ '+fn}); } catch(e){}
        }
    }
}

Interceptor.attach(mod.base.add(OPFETCH_OFF), {
    onEnter(){ if (ctx === null) { ctx = this.context.ecx; send({kind:'ctx', ctx: ctx.toString()});
                                    if (!scanned) { scanned = true; scanForSurfaces(); } } }
});

// GDI blit fallback (system exports, safe)
const gdi = Process.getModuleByName('gdi32.dll');
for (const nm of ['BitBlt','StretchBlt','StretchDIBits','SetDIBitsToDevice']) {
    let p=null; try{ p = gdi.findExportByName(nm); }catch(e){}
    if (p) Interceptor.attach(p, { onEnter(){ bump('gdi.'+nm); } });
}
const user32 = Process.getModuleByName('user32.dll');
for (const nm of ['UpdateWindow']) {
    let p=null; try{ p = user32.findExportByName(nm); }catch(e){}
    if (p) Interceptor.attach(p, { onEnter(){ bump('user32.'+nm); } });
}

let ddrawInfo = null;
try { const dd = Process.getModuleByName('ddraw.dll'); ddrawInfo = dd.base.toString() + ' size ' + dd.size; } catch(e){}
const gfxMods = Process.enumerateModules().filter(m => /ddraw|d3d|dinput|dsound|d8thunk/i.test(m.name)).map(m => m.name);
send({kind:'ready', base: mod.base.toString(), ddraw: ddrawInfo, gfxMods: gfxMods});
setInterval(() => { send({ kind:'tick', t: Date.now(), cnt: cnt, hasctx: ctx!==null }); }, 250);
""" % (OPFETCH_OFF, CTX_SCAN)


def capture(seconds, proc):
    import frida
    OUT.parent.mkdir(parents=True, exist_ok=True)
    ticks = []

    def on_message(msg, data):
        if msg.get("type") == "error":
            print("[frida-error]", msg.get("description")); return
        if msg.get("type") != "send":
            return
        pl = msg["payload"]
        k = pl.get("kind")
        if k == "ready":
            print(f"[frida] hooks live @ {pl['base']}; ddraw.dll: {pl.get('ddraw') or 'NOT loaded'}; "
                  f"gfx modules: {pl.get('gfxMods')}"); return
        if k == "ctx":     print(f"[frida] ctx = {pl['ctx']}"); return
        if k == "note":    print(f"[frida] {pl['msg']}"); return
        if k == "tick":    ticks.append(pl)

    target = int(proc) if str(proc).isdigit() else proc
    try:
        session = frida.attach(target)
    except frida.ProcessNotFoundError:
        print("[frida] AGE.EXE not found."); return 2
    script = session.create_script(JS)
    script.on("message", on_message)
    script.load()
    print(f"[frida] attached; capturing {seconds}s — just play normally.")
    try:
        for _ in range(seconds):
            time.sleep(1)
    except KeyboardInterrupt:
        pass
    try:
        session.detach()
    except Exception:
        pass
    with open(OUT, "w", encoding="utf-8") as f:
        for t in ticks:
            f.write(json.dumps(t) + "\n")
    report(ticks)
    return 0


def report(ticks):
    if len(ticks) < 3:
        print(f"[!] only {len(ticks)} ticks."); return
    labels = sorted({k for t in ticks for k in t.get("cnt", {})})
    if not labels:
        print("[!] no present-candidate hooks fired — no d3d9 device found in ctx and no GDI blits."); return
    print(f"\n=== present-rate report ({len(ticks)} ticks) ===")
    rates = {}
    for lab in labels:
        rr = []
        for a, b in zip(ticks, ticks[1:]):
            dt = (b["t"] - a["t"]) / 1000.0
            if dt <= 0:
                continue
            rr.append((b["cnt"].get(lab, 0) - a["cnt"].get(lab, 0)) / dt)
        active = [x for x in rr if x > 0]
        rates[lab] = (statistics.mean(active) if active else 0.0, max(rr, default=0), len(active))
    for lab in sorted(labels, key=lambda l: -rates[l][0]):
        avg, peak, n = rates[lab]
        flag = "  <-- frame signal?" if 20 <= avg <= 130 else ""
        print(f"    {lab:22s} active-avg {avg:8.1f}/sec  peak {peak:8.1f}  ({n} active){flag}")
    print("\n  The candidate at a steady ~display rate (≈60/sec, maybe ~100) is the present = frame clock.")
    print("  Combine with the cadence probe's ops/sec to get ops-per-frame.")


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("-")]
    seconds = int(args[0]) if args and args[0].isdigit() else 12
    return capture(seconds, "AGE.EXE")


if __name__ == "__main__":
    sys.exit(main())
