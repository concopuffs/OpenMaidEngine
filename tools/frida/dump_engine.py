#!/usr/bin/env python3
"""Dump the UNPACKED engine code from the live game so we can statically disassemble native op
handlers (docs/vm-mapping-plan.md appendix; docs/global-memory-re.md).

Why: both on-disk engine images are the SAME packed binary — SYS4AB.BIN decrypts (XOR-0xFF) to
AGE.EXE byte-for-byte, and AGE.EXE's code sections are entropy-8.00 packed garbage. The real handler
code exists only after the packer runs, in memory: AGE.EXE has some unpacked code in-place (e.g. the
AGF decoder at AGE.EXE+0x74f1f) AND the main VM interpreter runs from a large per-run heap r-x region
(~30 MB, nonstable base). So to RE a handler (e.g. 0x215 @ Kelebek VA 0x421160) we dump these regions
live, then disassemble offline and locate the handler via the opcode dispatch table.

Flow: launch the game (via `AGE Patch.exe`, JP locale) to the title -> attach -> this dumps -> exit.
Output: build/engine-dump/manifest.json + range_<base>.bin per dumped region (build/ is gitignored).

Usage: py -3.11 -u -X utf8 tools/frida/dump_engine.py [pid|AGE.EXE]
"""
import json
import sys
import time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import paths

REPO = paths.REPO
OUTDIR = paths.BUILD / "engine-dump"

# Dump every r-x range (code), plus the AGE.EXE module image in full. rw- ranges are only listed in
# the manifest (native object-manager state; snapshot later if a handler needs it). CHUNK keeps each
# frida message small; 2 MB is comfortably under the default limits.
CHUNK = 2 * 1024 * 1024

JS = r"""
const CHUNK = %d;

function ranges(prot){ return Process.enumerateRanges(prot).map(r => ({
    base: r.base.toString(), size: r.size, prot: r.protection,
    file: r.file ? r.file.path : null })); }

function dump(baseStr, size, tag){
    const base = ptr(baseStr);
    for (let off = 0; off < size; off += CHUNK){
        const n = Math.min(CHUNK, size - off);
        let buf;
        try { buf = base.add(off).readByteArray(n); }      // frida 17: method on the pointer
        catch(e){ send({kind:'gap', base:baseStr, off:off, n:n, err:''+e}); continue; }
        if (buf === null){ send({kind:'gap', base:baseStr, off:off, n:n, err:'null'}); continue; }
        send({kind:'chunk', base:baseStr, off:off, n:n, tag:tag}, buf);
    }
    send({kind:'done', base:baseStr, size:size, tag:tag});
}

const mod = Process.getModuleByName('AGE.EXE');
const rx = ranges('r-x');
const rw = ranges('rw-');

// landmark check: the AGF decoder is documented at AGE.EXE+0x74f1f (stable, unpacked in-place)
let landmark = null;
try { landmark = mod.base.add(0x74f1f).readByteArray(16); } catch(e){}

send({kind:'manifest',
      age_base: mod.base.toString(), age_size: mod.size,
      rx: rx, rw: rw}, landmark);

// dump targets: the AGE.EXE module image, plus every ANONYMOUS r-x range >= 1 MB (the unpacked heap
// code). Skip file-backed r-x ranges — those are Windows system DLLs, not the engine.
dump(mod.base.toString(), mod.size, 'age-module');
for (const r of rx){
    if (r.base === mod.base.toString()) continue;      // module already covered
    if (r.file) continue;                              // skip system DLLs
    if (r.size >= 1*1024*1024) dump(r.base, r.size, 'rx-heap');
}
send({kind:'alldone'});
"""


def capture(proc):
    import frida
    OUTDIR.mkdir(parents=True, exist_ok=True)
    files = {}            # base -> open file handle
    manifest = {}
    done = {"flag": False}

    def fh(base):
        if base not in files:
            files[base] = open(OUTDIR / f"range_{int(base, 16):08x}.bin", "wb")
        return files[base]

    def on_message(msg, data):
        if msg.get("type") == "error":
            print("[frida-error]", msg.get("description")); return
        if msg.get("type") != "send":
            return
        pl = msg["payload"]
        k = pl.get("kind")
        if k == "manifest":
            manifest.update(pl)
            lm = data.hex(" ") if data else "(unreadable)"
            print(f"[frida] AGE.EXE base={pl['age_base']} size=0x{pl['age_size']:x}")
            print(f"[frida] landmark AGE.EXE+0x74f1f = {lm}")
            print(f"[frida] r-x ranges: {len(pl['rx'])}  rw- ranges: {len(pl['rw'])}")
            for r in pl["rx"]:
                mark = "  <-- dump" if (r["size"] >= 1 << 20 or r["base"] == pl["age_base"]) else ""
                print(f"    r-x {r['base']} size=0x{r['size']:x} {r.get('file') or ''}{mark}")
        elif k == "chunk":
            f = fh(pl["base"]); f.seek(pl["off"]); f.write(data)
        elif k == "gap":
            print(f"    [gap] {pl['base']}+0x{pl['off']:x} n=0x{pl['n']:x} {pl['err']}")
        elif k == "done":
            print(f"[frida] dumped {pl['tag']} {pl['base']} (0x{pl['size']:x} bytes)")
        elif k == "alldone":
            done["flag"] = True

    target = int(proc) if str(proc).isdigit() else proc
    try:
        session = frida.attach(target)
    except (frida.ProcessNotFoundError, frida.ServerNotRunningError):
        procs = frida.get_local_device().enumerate_processes()
        hits = [(p.pid, p.name) for p in procs if "age" in p.name.lower()]
        print("[frida] AGE.EXE not found. Running AGE-like processes:", hits or "(none)")
        print("        Launch the game (AGE Patch.exe, JP locale) to the title, then re-run.")
        return 2

    script = session.create_script(JS % CHUNK)
    script.on("message", on_message)
    script.load()
    print(f"[frida] attached to {proc}; dumping engine code -> {OUTDIR}")
    try:
        for _ in range(600):                      # up to ~60 s; exits early on alldone
            if done["flag"]:
                break
            time.sleep(0.1)
    except KeyboardInterrupt:
        pass
    for f in files.values():
        f.close()
    if manifest:
        (OUTDIR / "manifest.json").write_text(
            json.dumps({k: v for k, v in manifest.items() if k != "type"},
                       ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"\n[frida] wrote {len(files)} range file(s) + manifest.json to {OUTDIR}")
    print("        Next: disassemble (capstone) and locate the 0x215 handler via the dispatch table.")
    return 0 if done["flag"] else 1


if __name__ == "__main__":
    proc = next((a for a in sys.argv[1:] if not a.startswith("-")), "AGE.EXE")
    sys.exit(capture(proc))
