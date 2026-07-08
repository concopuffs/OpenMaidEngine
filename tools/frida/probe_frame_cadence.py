#!/usr/bin/env python3
"""Live frame-cadence probe (docs/engine-re.md "Frame cadence"). Pins the native cadence — execution
rate vs displayed-frame rate, and frame timing — so the frame-stepped-VM fix picks its mechanism from
data instead of by feel.

SAFE pattern (matches capture_gfx_objects.py, which runs without crashing): plain-JS hooks only, no
CModule; the only engine-code hook is the PROVEN operand-fetch helper `0x41b940` (fires per opcode,
ecx = context) used to (a) grab the context pointer once and (b) count execution rate. Frame timing
comes from SYSTEM-DLL hooks (user32 message pump — never engine code, never anti-tamper). Engine state
(coroutine PC, run-state flags, sleep timer) is READ-ONLY polled. Nothing patches engine code beyond the
one address our other scripts already prove is safe.
  (Lesson from the crash: a CModule hook on the hottest engine fn jumped into a bad callback pointer and
   killed the game instantly. Plain-JS hooks on the proven address are the reliable path here.)

JS samples counters + Ctrl-key state every 250 ms; Python buckets by Ctrl-held so ONE run captures both
the normal and the fast-forward cadence.

Run (game running, sitting in an ADV scene, e.g. SC0000 line 1):
    py -3.11 -X utf8 tools/frida/probe_frame_cadence.py [seconds] [proc]
Protocol: let it play NORMALLY the first half, then HOLD Ctrl the rest. Auto-buckets by Ctrl state.
Writes build/frida-frame-cadence.jsonl.
"""
import json
import statistics
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
OUT = REPO / "build" / "frida-frame-cadence.jsonl"

OPFETCH_OFF = 0x1b940   # operand-fetch helper (0x41b940); per-op, ecx=ctx. PROVEN-safe hook.
IDX_OFF     = 0x53d14   # current coroutine index
PC_BASE     = 0x53d2c   # per-coroutine record base; +idx*0x78 holds the PC pointer (deref = opcode)
PC_STRIDE   = 0x78
FLAGS_OFF   = 0xa0ce4   # interpreter run-state flags word
SLEEP_ACTIVE= 0x5f30c   # sleep timer active flag (ctx+0x5f304 + 8)

JS = r"""
const OPFETCH_OFF=%d, IDX_OFF=%d, PC_BASE=%d, PC_STRIDE=%d, FLAGS_OFF=%d, SLEEP_ACTIVE=%d;
const mod = Process.getModuleByName('AGE.EXE');
let ctx = null, ops = 0;

// PROVEN-safe engine hook (same address capture_gfx_objects.py uses): grab ctx once + count exec rate.
Interceptor.attach(mod.base.add(OPFETCH_OFF), {
    onEnter(args){ ops++; if (ctx === null) { ctx = this.context.ecx; send({kind:'ctx', ctx: ctx.toString()}); } }
});

// Frame/loop markers + timing sources — SYSTEM DLL exports only (never engine code).
const user32 = Process.getModuleByName('user32.dll');
const cnt = { peekA:0, peekW:0, getA:0, getW:0, tgt:0, gtc:0, qpc:0 };
function hook(mod, name, key){ let p=null; try{ p=mod.findExportByName(name);}catch(e){} if (p) Interceptor.attach(p, { onEnter(){ cnt[key]++; } }); return !!p; }
const have = { peekA:hook(user32,'PeekMessageA','peekA'), peekW:hook(user32,'PeekMessageW','peekW'),
               getA:hook(user32,'GetMessageA','getA'),   getW:hook(user32,'GetMessageW','getW') };
// timing sources — whichever fires ~60/sec is the frame clock (the loop reads it once per displayed frame)
let winmm=null; try { winmm = Process.getModuleByName('winmm.dll'); } catch(e){}
const k32 = Process.getModuleByName('kernel32.dll');
if (winmm) hook(winmm,'timeGetTime','tgt');
hook(k32,'GetTickCount','gtc');
hook(k32,'QueryPerformanceCounter','qpc');
const GetAsyncKeyState = new NativeFunction(user32.findExportByName('GetAsyncKeyState'), 'int16', ['int']);

send({kind:'ready', base: mod.base.toString(), have: have, winmm: !!winmm});

setInterval(() => {
  let idx=-1, pc=null, flags=null, sleeping=null;
  if (ctx) {
    try { idx = ctx.add(IDX_OFF).readS32(); } catch(e){}
    try { if (idx>=0 && idx<64) pc = ctx.add(PC_BASE + idx*PC_STRIDE).readPointer().toString(); } catch(e){}
    try { flags = ctx.add(FLAGS_OFF).readU32(); } catch(e){}
    try { sleeping = ctx.add(SLEEP_ACTIVE).readU32(); } catch(e){}
  }
  // fast-forward = physical Ctrl OR the engine's own skip bit (flags & 0x8000000)
  const ffKey = (GetAsyncKeyState(0x11) & 0x8000) !== 0;
  const ffBit = (flags !== null) && ((flags & 0x8000000) !== 0);
  send({ t: Date.now(), ops: ops, idx: idx, pc: pc, flags: flags, sleeping: sleeping,
         peekA: cnt.peekA, peekW: cnt.peekW, getA: cnt.getA, getW: cnt.getW,
         tgt: cnt.tgt, gtc: cnt.gtc, qpc: cnt.qpc,
         ff: ffKey || ffBit, ffKey: ffKey, ffBit: ffBit, hasctx: ctx !== null });
}, 250);
""" % (OPFETCH_OFF, IDX_OFF, PC_BASE, PC_STRIDE, FLAGS_OFF, SLEEP_ACTIVE)


def capture(seconds, proc):
    import frida
    OUT.parent.mkdir(parents=True, exist_ok=True)
    samples = []

    def on_message(msg, data):
        if msg.get("type") == "error":
            print("[frida-error]", msg.get("description")); return
        if msg.get("type") != "send":
            return
        pl = msg["payload"]
        if pl.get("kind") == "ready":
            print(f"[frida] hooks live @ base {pl['base']}; message-api present: {pl['have']}"); return
        if pl.get("kind") == "ctx":
            print(f"[frida] captured engine ctx = {pl['ctx']}"); return
        samples.append(pl)

    target = int(proc) if str(proc).isdigit() else proc
    try:
        session = frida.attach(target)
    except frida.ProcessNotFoundError:
        procs = frida.get_local_device().enumerate_processes()
        print("[frida] not found. .exe processes:",
              [(p.pid, p.name) for p in procs if p.name.lower().endswith(".exe")])
        return 2
    script = session.create_script(JS)
    script.on("message", on_message)
    script.load()
    print(f"[frida] attached to {proc}; capturing {seconds}s.")
    print("        >>> Play NORMALLY the first half, then HOLD Ctrl the rest. <<<")
    try:
        for i in range(seconds):
            time.sleep(1)
            if i == seconds // 2:
                print("        --- halfway: start holding Ctrl now ---")
    except KeyboardInterrupt:
        pass
    try:
        session.detach()
    except Exception:
        pass
    with open(OUT, "w", encoding="utf-8") as f:
        for s in samples:
            f.write(json.dumps(s) + "\n")
    report(samples)
    return 0


def report(samples):
    if len(samples) < 3:
        print(f"[!] only {len(samples)} samples."); return
    if not samples[-1].get("hasctx"):
        print("[!!] never captured ctx — the operand-fetch hook did not fire (game idle, or not executing).")
    keys = ("ops", "peekA", "getA", "tgt", "gtc", "qpc")
    rows = []
    for a, b in zip(samples, samples[1:]):
        dt = (b["t"] - a["t"]) / 1000.0
        if dt <= 0:
            continue
        r = {"dt": dt, "ff": b.get("ff", False), "flags": b.get("flags"),
             "pc_moved": a.get("pc") != b.get("pc")}
        for k in keys:
            r[k] = (b.get(k, 0) - a.get(k, 0)) / dt
        rows.append(r)
    tot_ops = samples[-1]["ops"] - samples[0]["ops"]
    print(f"\n=== frame-cadence report ({len(rows)} intervals, {tot_ops} operand-fetches) ===")
    # candidate frame signals: prefer a timing source that sits ~30-120/sec (loop reads clock once/frame)
    for label, want in (("NORMAL", False), ("FAST-FWD (Ctrl / skip-bit)", True)):
        b = [r for r in rows if r["ff"] == want]
        # for cadence, use only ACTIVE intervals (ops>0) so parked time doesn't dilute the numbers
        act = [r for r in b if r["ops"] > 0]
        if not b:
            print(f"\n  {label}: no samples"); continue
        def m(rs, k): return statistics.mean(r[k] for r in rs) if rs else 0.0
        cand = {"timeGetTime": m(act, "tgt"), "GetTickCount": m(act, "gtc"),
                "QueryPerfCounter": m(act, "qpc"), "PeekMessageA": m(act, "peekA"), "GetMessageA": m(act, "getA")}
        frame_name, frame_rate = None, 0.0
        for nm, v in cand.items():
            if 20 <= v <= 200:
                frame_name, frame_rate = nm, v; break
        ops_active = m(act, "ops")
        peak = max((r["ops"] for r in b), default=0)
        print(f"\n  {label}  [{len(b)} intervals, {len(act)} active]")
        print(f"    operand-fetches/sec (active avg / peak): {ops_active:.0f} / {peak:.0f}")
        print("    timing-source rates (active avg/sec): " +
              ", ".join(f"{nm}={v:.1f}" for nm, v in cand.items()))
        if frame_name:
            print(f"    -> frame signal ~ {frame_name} @ {frame_rate:.1f}/sec ; "
                  f"exec/frame = {ops_active/frame_rate:.1f} (peak {peak/frame_rate:.1f})")
        else:
            print("    -> no timing source in 20-200/sec band; frame rate still unresolved.")
        flg = [f"0x{r['flags']:x}" for r in b if isinstance(r["flags"], int)]
        print(f"    flags seen: {sorted(set(flg))}")
    print("\n  Stable exec/frame => fixed op-budget cadence; peak >> active-avg with parking => run-until-yield.")
    print("  FAST-FWD vs NORMAL exec/frame shows how the ADV governor scales the slice.")


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("-")]
    seconds = int(args[0]) if args and args[0].isdigit() else 30
    proc = next((a for a in args if not a.isdigit()), "AGE.EXE")
    return capture(seconds, proc)


if __name__ == "__main__":
    sys.exit(main())
