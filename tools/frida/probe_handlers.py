#!/usr/bin/env python3
"""Probe: does the engine execute opcode handlers from the in-place module image (0x400000) or the
per-run heap copy (0x62411000)? Determines where to hook for the gfx-object-manager RE.

Hooks (module-relative offsets):
  +0x74f1f  AGF decoder  (KNOWN stable control — fires on every AGF/texture load)
  +0x1b940  operand-fetch helper (call 0x41b940 in handlers — fires per operand)
  +0x21090  the 0x212..0x215 gfx-command family entry
Reports per-hook fire counts each second. Drive the game (start a new game / click the opening,
which loads CGs) to generate graphics activity.

Usage: py -3.11 -u -X utf8 tools/frida/probe_handlers.py [pid|AGE.EXE]
"""
import sys
import time

JS = r"""
const mod = Process.getModuleByName('AGE.EXE');
const targets = { 'agf-decoder@+74f1f': 0x74f1f, 'operand-fetch@+1b940': 0x1b940, 'gfx-family@+21090': 0x21090 };
const counts = {};
for (const [name, off] of Object.entries(targets)){
    counts[name] = 0;
    try {
        Interceptor.attach(mod.base.add(off), {
            onEnter(args){ counts[name]++; }
        });
        send({kind:'hooked', name:name, addr: mod.base.add(off).toString()});
    } catch(e){ send({kind:'hookfail', name:name, err:''+e}); }
}
setInterval(() => send({kind:'counts', counts: counts}), 1000);
send({kind:'ready', base: mod.base.toString()});
"""


def main():
    import frida
    proc = next((a for a in sys.argv[1:] if not a.startswith("-")), "AGE.EXE")

    def on_message(msg, data):
        if msg.get("type") == "error":
            print("[frida-error]", msg.get("description")); return
        if msg.get("type") != "send":
            return
        pl = msg["payload"]
        k = pl.get("kind")
        if k == "ready":
            print(f"[frida] AGE.EXE base={pl['base']} — drive the game (new game / click opening) now.")
        elif k == "hooked":
            print(f"[frida] hooked {pl['name']} @ {pl['addr']}")
        elif k == "hookfail":
            print(f"[frida] HOOK FAILED {pl['name']}: {pl['err']}")
        elif k == "counts":
            print("  " + "  ".join(f"{n}={c}" for n, c in pl["counts"].items()))

    target = int(proc) if str(proc).isdigit() else proc
    try:
        session = frida.attach(target)
    except (frida.ProcessNotFoundError, frida.ServerNotRunningError):
        hits = [(p.pid, p.name) for p in frida.get_local_device().enumerate_processes()
                if p.name.upper().startswith("AGE")]
        print("[frida] AGE.EXE not found. AGE* processes:", hits or "(none)")
        return 2
    script = session.create_script(JS)
    script.on("message", on_message)
    script.load()
    try:
        time.sleep(30)
    except KeyboardInterrupt:
        pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
