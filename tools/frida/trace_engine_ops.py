#!/usr/bin/env python3
"""Engine op-path tracer for the differential offset-path oracle (docs/engine-re.md).

Attaches to the running game and, per executed op, reads the current coroutine's PC + codebase from the
engine ctx and emits (codebase, offset=(pc-codebase)/4) to build/engine-optrace.jsonl in execution order.
Diffed against the VM's trace (Age.Cli trace --trace-json) by tools/diff_optrace.py — the first divergence
is the opcode/branch we modeled wrong.

READ-ONLY (Interceptor + memory reads; no patching), following probe_frame_cadence.py / capture_gfx_objects.py.
Two selectable hooks (Task 1 recon gate decides which is safe):
  --hook tick     -> adv_interpreter_tick 0x410fb0  (one op per tick; clean 1:1 signal). CModule on this
                     path crashed the game before; PLAIN-JS here is untested -> the recon gate proves it.
  --hook operand  -> vm_operand_fetch 0x41b940      (PROVEN-safe, probe_frame_cadence.py uses it). Fires
                     per-operand, so we dedupe consecutive same-pc; misses zero-operand ops (markers/no-ops,
                     which don't branch) -> acceptable for a control-flow diff.

    py -3.11 -u -X utf8 tools/frida/trace_engine_ops.py [--hook tick|operand] [seconds] [proc]

Let the deterministic opening auto-advance to the first wait-for-input, then stop (Ctrl-C or the timeout).
"""
import json
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
OUT = REPO / "build" / "engine-optrace.jsonl"
LIVE = REPO / "build" / "tracer-live.flag"   # touched once hooks are installed; removed on exit

TICK_OFF    = 0x10fb0   # adv_interpreter_tick (0x410fb0); one op per tick, thiscall ecx=ctx
OPFETCH_OFF = 0x1b940   # vm_operand_fetch    (0x41b940); per-operand, thiscall ecx=ctx  (PROVEN-safe)
IDX_OFF     = 0x53d14   # cur_ctx_index
PC_BASE     = 0x53d2c   # frame_pc     ; +idx*0x78 = current instruction pointer
CB_BASE     = 0x53d28   # frame_codebase; +idx*0x78 = base pointer of the running script's code
PC_STRIDE   = 0x78

JS = r"""
const HOOK_OFF=%d, IDX_OFF=%d, PC_BASE=%d, CB_BASE=%d, PC_STRIDE=%d, DEDUPE=%d;
const mod = Process.getModuleByName('AGE.EXE');
let buf = [], total = 0, lastPc = -1;
const FLUSH_N = 2000;

function flush(){ if (buf.length){ send({kind:'batch', rows: buf}); buf = []; } }

Interceptor.attach(mod.base.add(HOOK_OFF), {
    onEnter(args){
        const ctx = this.context.ecx;                       // thiscall: ecx = engine ctx
        let idx, pc, cb;
        try {
            idx = ctx.add(IDX_OFF).readS32();
            if (idx < 0 || idx >= 64) return;
            pc = ctx.add(PC_BASE + idx*PC_STRIDE).readU32();
            cb = ctx.add(CB_BASE + idx*PC_STRIDE).readU32();
        } catch(e){ return; }
        if (DEDUPE && pc === lastPc) return;                // operand mode: one entry per instruction
        lastPc = pc;
        const off = (pc - cb) >>> 2;                        // byte delta / 4 = bytecode word index
        buf.push([cb >>> 0, off]);
        total++;
        if (buf.length >= FLUSH_N) flush();
    }
});

setInterval(flush, 200);                                    // flush the tail so nothing is lost on detach
send({kind:'ready', base: mod.base.toString(), hook: '0x'+HOOK_OFF.toString(16), total: 0});
rpc.exports = { stats(){ return total; }, flush(){ flush(); return total; } };
"""


def capture(hook, seconds, proc):
    import frida
    OUT.parent.mkdir(parents=True, exist_ok=True)
    hook_off = TICK_OFF if hook == "tick" else OPFETCH_OFF
    dedupe = 1 if hook == "operand" else 0
    js = JS % (hook_off, IDX_OFF, PC_BASE, CB_BASE, PC_STRIDE, dedupe)

    count = {"rows": 0}
    f = open(OUT, "w", encoding="utf-8")

    def on_message(msg, data):
        if msg.get("type") == "error":
            print("[frida-error]", msg.get("description")); return
        if msg.get("type") != "send":
            return
        pl = msg["payload"]
        if pl.get("kind") == "ready":
            print(f"[frida] hook live @ base {pl['base']} + {pl['hook']} (mode={hook})"); return
        if pl.get("kind") == "batch":
            for cb, off in pl["rows"]:
                f.write(json.dumps({"codebase": cb, "offset": off}) + "\n")
            count["rows"] += len(pl["rows"])

    target = int(proc) if str(proc).isdigit() else proc
    try:
        session = frida.attach(target)
    except frida.ProcessNotFoundError:
        procs = frida.get_local_device().enumerate_processes()
        print("[frida] not found. .exe processes:",
              [(p.pid, p.name) for p in procs if p.name.lower().endswith(".exe")])
        f.close(); return 2

    crashed = {"v": False}
    session.on("detached", lambda reason, *a: crashed.update(v=(reason != "application-requested")) or
               print(f"[frida] session detached: {reason}"))
    script = session.create_script(js)
    script.on("message", on_message)
    script.load()   # synchronous: the Interceptor is installed before this returns
    LIVE.write_text("live", encoding="utf-8")   # signal to the orchestrator: hooks are live, safe to trigger
    print(f"[frida] attached to {proc}; capturing {seconds}s ({hook} hook). HOOK LIVE — trigger now.")
    print("        >>> let the opening auto-advance to the first wait-for-input. <<<")
    try:
        for _ in range(seconds):
            time.sleep(1)
    except KeyboardInterrupt:
        print("[frida] stopped early (Ctrl-C).")
    try:
        script.exports_sync.flush()          # final flush of the tail
        time.sleep(0.3)
    except Exception:
        pass
    try:
        session.detach()
    except Exception:
        pass
    f.close()
    try:
        LIVE.unlink()
    except OSError:
        pass

    print(f"\n[trace] wrote {count['rows']} entries -> {OUT}")
    if crashed["v"]:
        print("[GATE] the hook DESTABILIZED the game (unexpected detach). If this was --hook tick, "
              "re-run with --hook operand (proven-safe).")
    if count["rows"] == 0:
        print("[GATE] no entries captured — the hook never fired (game idle / wrong hook / ecx!=ctx). "
              "Ensure the game is EXECUTING the opening while capturing.")
        return 3
    # quick shape: distinct codebases + top few by volume
    from collections import Counter
    c = Counter()
    with open(OUT, encoding="utf-8") as fh:
        for line in fh:
            c[json.loads(line)["codebase"]] += 1
    print(f"[trace] distinct codebases: {len(c)}")
    for cb, n in c.most_common(8):
        print(f"          0x{cb:08x}: {n} ops")
    return 0


def main():
    args = sys.argv[1:]
    hook = "tick"
    if "--hook" in args:
        hook = args[args.index("--hook") + 1]
    rest = [a for a in args if not a.startswith("-") and a not in ("tick", "operand")]
    seconds = int(rest[0]) if rest and rest[0].isdigit() else 20
    proc = next((a for a in rest if not a.isdigit()), "AGE.EXE")
    if hook not in ("tick", "operand"):
        print("usage: trace_engine_ops.py [--hook tick|operand] [seconds] [proc]"); return 1
    return capture(hook, seconds, proc)


if __name__ == "__main__":
    sys.exit(main())
