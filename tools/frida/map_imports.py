#!/usr/bin/env python3
"""Map the packer's resolved import pointers (RVA -> dll!Func) from the LIVE game, so the
dynamically-resolved Win32 APIs can be named at their call sites in the /v2 Ghidra image.

Why: AGE.EXE ships a zeroed IAT resolved via GetProcAddress at load (pe-sieve /imp fails, see
docs/engine-re.md). But the resolved pointers sit in memory. This tool builds {runtime_addr ->
dll!Func} from the live process's module exports, then scans the 0x400000 module for aligned DWORDs
holding those addresses -> RVA -> name. RVAs into the fixed-base 0x400000 module are ASLR-stable, so
labels computed live apply to the earlier dump (docs/superpowers/specs/2026-07-09-frida-import-map-design.md).

Read-only Frida (enumerateModules/enumerateExports + memory reads) — the safe plain-JS pattern
(mirrors dump_engine.py); no spawn, no GetProcAddress hook, no patching.

Usage:
    py -3.11 -u -X utf8 tools/frida/map_imports.py --recon   # report clustering (THE GATE); writes nothing
    py -3.11 -u -X utf8 tools/frida/map_imports.py           # write build/import-map.json (Task 2)
"""
import struct
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import paths

REPO = paths.REPO
BUILD = paths.BUILD
MODULE = "AGE.EXE"
DUMP_SIZE = 0x260000          # /v2 dump covers 0x400000..0x660000; RVA >= this is out-of-dump
CHUNK = 2 * 1024 * 1024       # keep each frida message small (matches dump_engine.py)


# ---- pure logic (unit-tested; no frida needed) ---------------------------------------------------

def build_export_index(exports):
    """[{address:int, name, module}] -> {int addr: 'module!name'} (first name wins; skip null)."""
    idx = {}
    for e in exports:
        a = int(e["address"])
        if a and a not in idx:
            idx[a] = f"{e['module']}!{e['name']}"
    return idx


def scan_pointer_matches(mem, base_va, index):
    """Aligned little-endian DWORD scan; return [(rva, value, name)] for values in index."""
    out = []
    n = len(mem) & ~3
    for off in range(0, n, 4):
        v = struct.unpack_from("<I", mem, off)[0]
        name = index.get(v)
        if name is not None:
            out.append((off, v, name))
    return out


def cluster_runs(rvas, stride=4):
    """Group sorted RVAs into contiguous aligned runs -> [(start_rva, count)]."""
    runs = []
    for r in sorted(rvas):
        if runs and r == runs[-1][0] + runs[-1][1] * stride:
            s, c = runs[-1]
            runs[-1] = (s, c + 1)
        else:
            runs.append((r, 1))
    return runs


# ---- live collection (frida) ---------------------------------------------------------------------

JS = r"""
const CHUNK = %d;
const MODULE = '%s';

// exports of every loaded module, one message per module (bounded size)
Process.enumerateModules().forEach(function (m) {
    var ex = m.enumerateExports().map(function (e) {
        return {address: e.address.toString(), name: e.name};
    });
    send({kind: 'exports', module: m.name, exports: ex});
});

// the target module image, chunked (scan happens in Python against the tested pure logic)
var mod = Process.getModuleByName(MODULE);
send({kind: 'module', base: mod.base.toString(), size: mod.size});
for (var off = 0; off < mod.size; off += CHUNK) {
    var n = Math.min(CHUNK, mod.size - off);
    var buf = null;
    try { buf = mod.base.add(off).readByteArray(n); } catch (e) {}
    if (buf === null) { send({kind: 'gap', off: off, n: n}); continue; }
    send({kind: 'chunk', off: off, n: n}, buf);
}
send({kind: 'alldone'});
"""


def collect():
    """Attach to the live game; return (exports_list, module_base_va, module_bytes)."""
    import frida
    exports = []
    state = {"base": None, "size": 0, "done": False}
    mem = bytearray()

    def on_message(msg, data):
        if msg.get("type") == "error":
            print("[frida-error]", msg.get("description"))
            return
        if msg.get("type") != "send":
            return
        pl = msg["payload"]
        k = pl.get("kind")
        if k == "exports":
            for e in pl["exports"]:
                exports.append({"address": int(e["address"], 16), "name": e["name"], "module": pl["module"]})
        elif k == "module":
            state["base"] = int(pl["base"], 16)
            state["size"] = pl["size"]
            if len(mem) < pl["size"]:
                mem.extend(b"\x00" * (pl["size"] - len(mem)))
        elif k == "chunk":
            mem[pl["off"]:pl["off"] + pl["n"]] = data
        elif k == "gap":
            print(f"    [gap] +0x{pl['off']:x} n=0x{pl['n']:x}")
        elif k == "alldone":
            state["done"] = True

    try:
        session = frida.attach(MODULE)
    except Exception:
        dev = frida.get_local_device()
        hits = [p for p in dev.enumerate_processes() if "age" in p.name.lower()]
        if not hits:
            print("[frida] AGE.EXE not found — launch the game to the title, then re-run.")
            sys.exit(2)
        session = dev.attach(hits[0].pid)

    script = session.create_script(JS % (CHUNK, MODULE))
    script.on("message", on_message)
    script.load()
    import time
    for _ in range(600):                 # up to ~60s; exits early on alldone
        if state["done"]:
            break
        time.sleep(0.1)
    session.detach()
    return exports, state["base"], bytes(mem[:state["size"]])


def recon():
    exports, base, mem = collect()
    idx = build_export_index(exports)
    matches = scan_pointer_matches(mem, base, idx)
    rvas = [r for r, _, _ in matches]
    runs = cluster_runs(rvas)
    big = [(s, c) for s, c in runs if c >= 3]
    inrange = [r for r in rvas if r < DUMP_SIZE]
    print(f"exports mapped = {len(idx)}   module bytes = 0x{len(mem):x}")
    print(f"pointer matches = {len(matches)}   in-range (<0x{DUMP_SIZE:x}) = {len(inrange)}")
    print(f"contiguous runs (>=3): {[hex(s) + ':' + str(c) for s, c in big]}")
    print(f"singletons = {sum(1 for _, c in runs if c == 1)}")
    if big and inrange:
        print("GATE-PASS: module-resident import table present and in dump range -> proceed to Task 2.")
    else:
        print("GATE-FAIL: no in-module import table (pointers likely heap-resident) -> STOP, document.")


if __name__ == "__main__":
    if "--recon" in sys.argv[1:]:
        recon()
    else:
        from map_imports_full import build   # Task 2 writer
        build()
