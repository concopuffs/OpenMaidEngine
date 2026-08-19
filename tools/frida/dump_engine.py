#!/usr/bin/env python3
"""Dump one profile's unpacked AGE engine image from a live process for native RE.

The installed executable is packed; native opcode handlers become readable only after AGE has
unpacked itself in memory. Launch the selected game to its title screen, then attach by PID. Common
``--profile``/``--game-root`` options are consumed by ``tools/paths.py`` before this parser runs.

Output is profile-scoped and provenance-bearing:
``build/games/<profile>/engine-dump/{manifest.json,range_<base>.bin}``.

Usage:
  py -3.11 -u -X utf8 tools/frida/dump_engine.py --profile kamidori --game-root PATH PID
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import paths

OUTDIR = paths.BUILD / "engine-dump"
CHUNK = 2 * 1024 * 1024

# A landmark is only a convenience check after its RVA has been proven for that exact executable.
# Absence is the correct default for a new profile; capture itself never depends on a landmark.
PROFILE_LANDMARK_RVAS = {
    "himegari": 0x74F1F,
}

JS = r"""
const CHUNK = %d;
const LANDMARK_RVA = %d;

function ranges(prot){ return Process.enumerateRanges(prot).map(r => ({
    base: r.base.toString(), size: r.size, prot: r.protection,
    file: r.file ? r.file.path : null })); }

function moduleRows(){ return Process.enumerateModules().map(m => ({
    name:m.name, path:m.path, base:m.base.toString(), size:m.size })); }

function isInsideModule(range, modules){
    const start = ptr(range.base);
    const end = start.add(range.size);
    return modules.some(m => start.compare(ptr(m.base)) >= 0
        && end.compare(ptr(m.base).add(m.size)) <= 0);
}

function dump(baseStr, size, tag){
    const base = ptr(baseStr);
    for (let off = 0; off < size; off += CHUNK){
        const n = Math.min(CHUNK, size - off);
        let buf;
        try { buf = base.add(off).readByteArray(n); }
        catch(e){ send({kind:'gap', base:baseStr, off:off, n:n, tag:tag, err:''+e}); continue; }
        if (buf === null){
            send({kind:'gap', base:baseStr, off:off, n:n, tag:tag, err:'null'});
            continue;
        }
        send({kind:'chunk', base:baseStr, off:off, n:n, tag:tag}, buf);
    }
    send({kind:'done', base:baseStr, size:size, tag:tag});
}

const mod = Process.getModuleByName('AGE.EXE');
const rx = ranges('r-x');
const rw = ranges('rw-');
const modules = moduleRows();
let landmark = null;
if (LANDMARK_RVA >= 0 && LANDMARK_RVA + 16 <= mod.size){
    try { landmark = mod.base.add(LANDMARK_RVA).readByteArray(16); } catch(e){}
}

send({kind:'manifest', age_base:mod.base.toString(), age_size:mod.size,
      age_path:mod.path, landmark_rva:LANDMARK_RVA >= 0 ? LANDMARK_RVA : null,
      rx:rx, rw:rw, modules:modules}, landmark);

// Dump the complete AGE module plus large anonymous executable ranges. The latter are retained as
// evidence because packers/revisions can move generated or unpacked code outside the module.
dump(mod.base.toString(), mod.size, 'age-module');
for (const r of rx){
    if (isInsideModule(r, modules)) continue;
    if (r.file) continue;
    if (r.size >= 1*1024*1024) dump(r.base, r.size, 'rx-anonymous');
}
send({kind:'alldone'});
"""


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def read_catalog_identity(path: Path) -> dict[str, str]:
    header = path.read_bytes()[:264]
    if len(header) < 264:
        raise ValueError(f"{path}: SYS4INI header is truncated")

    def cstring(data: bytes) -> str:
        return data.split(b"\0", 1)[0].decode("cp932", errors="replace").rstrip(" ")

    return {
        "catalog_revision": cstring(header[:8]),
        "title": cstring(header[8:264]),
    }


def profile_provenance() -> dict[str, object]:
    manifest_path = paths.PROFILE_MANIFEST_DIR / f"{paths.CONTEXT.profile_id}.json"
    profile = json.loads(manifest_path.read_text(encoding="utf-8"))
    executable = paths.AGE_EXE
    if not executable.is_file():
        raise ValueError(f"selected profile executable does not exist: {executable}")
    identity = read_catalog_identity(paths.GAME_DIR / "SYS4INI.BIN")
    return {
        "profile_id": paths.CONTEXT.profile_id,
        "sys_frontend_id": profile["sysFrontendId"],
        "engine_abi_id": profile["engineAbiId"],
        **identity,
        "game_root": str(paths.GAME_DIR),
        "packed_executable": {
            "name": executable.name,
            "size": executable.stat().st_size,
            "sha256": sha256_file(executable),
        },
    }


def build_manifest(
    provenance: dict[str, object],
    runtime: dict[str, object],
    dumped_ranges: list[dict[str, object]],
    target: str,
    captured_at_utc: str,
) -> dict[str, object]:
    return {
        "schema_version": 2,
        "capture_tool": "tools/frida/dump_engine.py",
        "captured_at_utc": captured_at_utc,
        "attach_target": target,
        "profile": provenance,
        "runtime_module": {
            "path": runtime.get("age_path"),
            "image_base": runtime.get("age_base"),
            "size": runtime.get("age_size"),
            "landmark_rva": runtime.get("landmark_rva"),
            "landmark_bytes": runtime.get("landmark_bytes"),
        },
        "dumped_ranges": dumped_ranges,
        "loaded_modules": runtime.get("modules", []),
        "executable_ranges": runtime.get("rx", []),
        "writable_ranges": runtime.get("rw", []),
    }


def capture(proc: str, *, overwrite: bool = False) -> int:
    import frida

    provenance = profile_provenance()
    OUTDIR.mkdir(parents=True, exist_ok=True)
    existing = [OUTDIR / "manifest.json", *OUTDIR.glob("range_*.bin")]
    existing = [path for path in existing if path.exists()]
    if existing and not overwrite:
        print(f"[frida] refusing to overwrite {OUTDIR}; use --overwrite for a deliberate recapture")
        return 2
    if overwrite:
        for path in existing:
            path.unlink()

    files: dict[str, object] = {}
    range_records: dict[str, dict[str, object]] = {}
    runtime: dict[str, object] = {}
    finished = {"flag": False}
    captured_at = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")

    def range_file(base: str):
        if base not in files:
            files[base] = (OUTDIR / f"range_{int(base, 16):08x}.bin").open("wb")
        return files[base]

    def record(base: str, tag: str) -> dict[str, object]:
        return range_records.setdefault(base, {
            "base": base,
            "tag": tag,
            "file": f"range_{int(base, 16):08x}.bin",
            "declared_size": None,
            "captured_bytes": 0,
            "gaps": [],
        })

    def on_message(message, data):
        if message.get("type") == "error":
            print("[frida-error]", message.get("description"))
            return
        if message.get("type") != "send":
            return
        payload = message["payload"]
        kind = payload.get("kind")
        if kind == "manifest":
            runtime.update(payload)
            runtime.pop("kind", None)
            runtime["landmark_bytes"] = data.hex() if data else None
            landmark = runtime["landmark_bytes"] or "not configured"
            print(f"[frida] AGE.EXE base={payload['age_base']} size=0x{payload['age_size']:x}")
            print(f"[frida] profile={provenance['profile_id']} abi={provenance['engine_abi_id']} "
                  f"landmark={landmark}")
            print(f"[frida] r-x ranges: {len(payload['rx'])}  rw- ranges: {len(payload['rw'])}")
        elif kind == "chunk":
            stream = range_file(payload["base"])
            stream.seek(payload["off"])
            stream.write(data)
            record(payload["base"], payload["tag"])["captured_bytes"] += payload["n"]
        elif kind == "gap":
            entry = record(payload["base"], payload["tag"])
            entry["gaps"].append({
                "offset": payload["off"], "size": payload["n"], "error": payload["err"],
            })
            print(f"    [gap] {payload['base']}+0x{payload['off']:x} "
                  f"n=0x{payload['n']:x} {payload['err']}")
        elif kind == "done":
            entry = record(payload["base"], payload["tag"])
            entry["declared_size"] = payload["size"]
            stream = range_file(payload["base"])
            stream.truncate(payload["size"])
            print(f"[frida] dumped {payload['tag']} {payload['base']} (0x{payload['size']:x} bytes)")
        elif kind == "alldone":
            finished["flag"] = True

    target: int | str = int(proc) if proc.isdigit() else proc
    try:
        session = frida.attach(target)
    except (frida.ProcessNotFoundError, frida.ServerNotRunningError):
        processes = frida.get_local_device().enumerate_processes()
        hits = [(process.pid, process.name) for process in processes if "age" in process.name.lower()]
        print("[frida] AGE.EXE not found. Running AGE-like processes:", hits or "(none)")
        print("        Launch the selected game to the title screen, then attach by PID.")
        return 2

    landmark_rva = PROFILE_LANDMARK_RVAS.get(paths.CONTEXT.profile_id, -1)
    script = session.create_script(JS % (CHUNK, landmark_rva))
    script.on("message", on_message)
    script.load()
    print(f"[frida] attached to {proc}; dumping engine code -> {OUTDIR}")
    try:
        for _ in range(600):
            if finished["flag"]:
                break
            time.sleep(0.1)
    except KeyboardInterrupt:
        pass
    finally:
        for stream in files.values():
            stream.close()
        session.detach()

    dumped_ranges = []
    for base, entry in sorted(range_records.items(), key=lambda item: int(item[0], 16)):
        dump_path = OUTDIR / str(entry["file"])
        entry["file_size"] = dump_path.stat().st_size
        entry["sha256"] = sha256_file(dump_path)
        dumped_ranges.append(entry)
    if runtime:
        manifest = build_manifest(provenance, runtime, dumped_ranges, str(proc), captured_at)
        (OUTDIR / "manifest.json").write_text(
            json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    print(f"\n[frida] wrote {len(dumped_ranges)} range file(s) + manifest.json to {OUTDIR}")
    if any(entry["gaps"] for entry in dumped_ranges):
        print("[frida] capture contains unreadable gaps; inspect manifest.json before import")
        return 1
    return 0 if finished["flag"] else 1


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("target", nargs="?", default="AGE.EXE", help="live AGE process PID or name")
    parser.add_argument(
        "--overwrite", action="store_true",
        help="replace an existing dump in the selected profile's disposable build directory",
    )
    arguments = parser.parse_args()
    try:
        return capture(arguments.target, overwrite=arguments.overwrite)
    except (OSError, ValueError, KeyError, json.JSONDecodeError) as error:
        parser.error(str(error))


if __name__ == "__main__":
    raise SystemExit(main())
