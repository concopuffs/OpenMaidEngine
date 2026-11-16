#!/usr/bin/env python3
"""M0 on Linux (docs/superpowers/plans/2026-11-15-launcher.md §2.2), run inside WSL's Ubuntu:

- question 1: each publish variant's folder size, .tar.gz size and time to the first frame (one first run, five more);
- question 2: the win-x64 program cross-built here, copied back for pe-resources.ps1 on Windows;
- question 3: where the native libraries are, beside the program or unpacked, and the default unpack folder;
- question 4: the glibc versions the program and its native libraries need, what ldd cannot resolve, the system
  libraries the window maps at run time (with their Ubuntu packages), and --check-like with no display;
- question 10: the peak memory of each publish, built clean with the release build's low-memory flags.

The project is copied to an ext4 folder first ($WORK, default ~/ome-launcher-m0): building over /mnt is slow, and a
player's install is on a native file system. Results go to the repository's build/launcher-m0/results-linux.json.
Needs the .NET 10 SDK at $DOTNET (default ~/.dotnet/dotnet). Python 3.8 compatible.
"""
from __future__ import annotations

import json
import os
import re
import shutil
import statistics
import subprocess
import sys
import tarfile
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
PROJECT = HERE.parent
REPO = PROJECT.parent.parent
OUT = REPO / "build" / "launcher-m0"
WORK = Path(os.environ.get("WORK", str(Path.home() / "ome-launcher-m0")))
DOTNET = Path(os.environ.get("DOTNET", str(Path.home() / ".dotnet" / "dotnet")))
RUNS = 5
ENV = dict(
    os.environ,
    DOTNET_ROOT=str(DOTNET.parent),
    DOTNET_CLI_TELEMETRY_OPTOUT="1",
    DOTNET_NOLOGO="1",
    AVALONIA_TELEMETRY_OPTOUT="1",
)
# The release build's flags against memory peaks (tools/build-linux-x64.sh, tools/validate.py).
LOW_MEMORY = ["--disable-build-servers", "-p:UseSharedCompilation=false", "-nodeReuse:false", "-m:1"]
COMMON = ["-c", "Release", "--self-contained", "true", "-p:DebugType=none", "-p:DebugSymbols=false"]
SINGLE = ["-p:PublishSingleFile=true"]
EXTRACT = SINGLE + ["-p:IncludeNativeLibrariesForSelfExtract=true"]
LINUX_VARIANTS = {
    "folder": [],
    "single": SINGLE,
    "single-extract": EXTRACT,
    "single-compress": EXTRACT + ["-p:EnableCompressionInSingleFile=true"],
    "single-r2r": SINGLE + ["-p:PublishReadyToRun=true"],
}
NATIVE = re.compile(r"libSkiaSharp|libHarfBuzzSharp|libAvaloniaNative|av_libGLESv2")


def log(message: str) -> None:
    print(message, flush=True)


def sync() -> Path:
    target = WORK / "launcher" / "Ome.Launcher.Spike"
    target.mkdir(parents=True, exist_ok=True)
    subprocess.run(
        ["rsync", "-a", "--delete", "--exclude", "bin", "--exclude", "obj", f"{PROJECT}/", f"{target}/"],
        check=True,
    )
    fonts = WORK / "godot" / "assets"
    fonts.mkdir(parents=True, exist_ok=True)
    shutil.copy2(REPO / "godot" / "assets" / "NotoSansJP-Regular.otf", fonts)
    return target


def session_rss_kib(session: int) -> int:
    """Sum of the resident memory of every process in a session (shared pages count once per process)."""
    page_kib = os.sysconf("SC_PAGE_SIZE") // 1024
    total = 0
    for entry in os.listdir("/proc"):
        if not entry.isdigit():
            continue
        try:
            with open(f"/proc/{entry}/stat") as f:
                stat = f.read()
            fields = stat[stat.rfind(")") + 2:].split()
            if int(fields[3]) != session:
                continue
            with open(f"/proc/{entry}/statm") as f:
                total += int(f.read().split()[1]) * page_kib
        except (OSError, ValueError, IndexError):
            continue
    return total


def publish(project: Path, rid: str, name: str, extra: list) -> dict:
    """A clean publish (no obj/bin), measured: the peak of the whole process tree and /usr/bin/time's maximum."""
    for folder in ("obj", "bin"):
        shutil.rmtree(project / folder, ignore_errors=True)
    out = WORK / "publish" / f"{rid}-{name}"
    shutil.rmtree(out, ignore_errors=True)
    publish_log = WORK / "logs" / f"publish-{rid}-{name}.log"
    publish_log.parent.mkdir(parents=True, exist_ok=True)
    command = ["/usr/bin/time", "-v", str(DOTNET), "publish", "-r", rid, "-o", str(out)] + COMMON + LOW_MEMORY + extra
    started = time.monotonic()
    with open(publish_log, "w") as handle:
        process = subprocess.Popen(
            command, cwd=project, env=ENV, stdout=handle, stderr=subprocess.STDOUT, start_new_session=True
        )
        peak = 0
        while process.poll() is None:
            peak = max(peak, session_rss_kib(process.pid))
            time.sleep(0.05)
    elapsed = time.monotonic() - started
    text = publish_log.read_text(errors="replace")
    if process.returncode != 0:
        raise SystemExit(f"publish {rid}-{name} failed; see {publish_log}")
    match = re.search(r"Maximum resident set size \(kbytes\): (\d+)", text)
    warnings = sorted(set(re.findall(r"warning (NETSDK\d+|NU\d+|IL\d+|CS\d+): ([^\[]+)", text)))
    return {
        "out": str(out),
        "seconds": round(elapsed, 1),
        "peak_tree_rss_kib": peak,
        "time_max_rss_kib": int(match.group(1)) if match else None,
        "warnings": [f"{code}: {message.strip()}" for code, message in warnings],
    }


def folder_facts(out: Path) -> dict:
    files = [p for p in out.rglob("*") if p.is_file()]
    archive = out.parent / f"{out.name}.tar.gz"
    with tarfile.open(archive, "w:gz") as tar:
        tar.add(out, arcname=out.name)
    return {
        "files": len(files),
        "folder_bytes": sum(p.stat().st_size for p in files),
        "program_bytes": (out / "Ome.Launcher.Spike").stat().st_size if (out / "Ome.Launcher.Spike").exists() else None,
        "tar_gz_bytes": archive.stat().st_size,
        "natives_beside": sorted(str(p.relative_to(out)) for p in files if NATIVE.search(p.name)),
    }


def run_window(program: Path, tag: str, env: dict) -> tuple:
    frame = WORK / "logs" / f"ff-{tag}.txt"
    modules = WORK / "logs" / f"modules-{tag}.txt"
    if frame.exists():
        frame.unlink()
    result = subprocess.run(
        [str(program), "--first-frame-file", str(frame), "--modules-file", str(modules), "--exit-after-first-frame"],
        env=env, capture_output=True, text=True, timeout=120,
    )
    if result.returncode != 0 or not frame.exists():
        raise SystemExit(f"window run {tag} failed ({result.returncode}): {result.stderr[-2000:]}")
    return int(frame.read_text().split()[0]), modules.read_text().splitlines()


def glibc_needs(path: Path) -> str | None:
    text = subprocess.run(["readelf", "-V", str(path)], capture_output=True, text=True).stdout
    versions = re.findall(r"Name: GLIBC_(\d+)\.(\d+)(?:\.(\d+))?", text)
    if not versions:
        return None
    top = max(tuple(int(part) if part else 0 for part in version) for version in versions)
    return f"{top[0]}.{top[1]}" + (f".{top[2]}" if top[2] else "")


def unresolved(path: Path) -> list:
    text = subprocess.run(["ldd", str(path)], capture_output=True, text=True).stdout
    return [line.strip() for line in text.splitlines() if "not found" in line]


def package_of(library: str) -> str:
    result = subprocess.run(["dpkg", "-S", library], capture_output=True, text=True)
    return result.stdout.split(":")[0] if result.returncode == 0 else "(no package)"


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    project = sync()
    icon = WORK / "spike.ico"
    subprocess.run([sys.executable, str(HERE / "make-icon.py"), str(icon)], check=True)
    log("warm-up restore (fills the NuGet cache, as the release job's cache would)")
    for rid in ("linux-x64", "win-x64"):
        subprocess.run([str(DOTNET), "restore", "-r", rid], cwd=project, env=ENV, check=True, capture_output=True)

    results: dict = {"dotnet": subprocess.run([str(DOTNET), "--version"], env=ENV, capture_output=True, text=True).stdout.strip()}
    extract_base = WORK / "extract-base"
    linux: dict = {}
    for name, extra in LINUX_VARIANTS.items():
        built = publish(project, "linux-x64", name, extra)
        out = Path(built["out"])
        facts = folder_facts(out)
        program = out / "Ome.Launcher.Spike"
        shutil.rmtree(extract_base, ignore_errors=True)
        env = dict(os.environ, DOTNET_BUNDLE_EXTRACT_BASE_DIR=str(extract_base))
        env.pop("DOTNET_ROOT", None)
        times = []
        modules: list = []
        for run in range(RUNS + 1):
            ms, modules = run_window(program, f"linux-x64-{name}", env)
            times.append(ms)
        extracted = sorted(str(p.relative_to(extract_base)) for p in extract_base.rglob("*") if p.is_file()) if extract_base.exists() else []
        default_extract: list = []
        if extracted:
            plain = dict(os.environ)
            plain.pop("DOTNET_BUNDLE_EXTRACT_BASE_DIR", None)
            plain.pop("DOTNET_ROOT", None)
            run_window(program, f"linux-x64-{name}-default", plain)
            for base in (Path(os.environ.get("TMPDIR", "/tmp")) / ".net", Path("/var/tmp/.net"), Path.home() / ".net"):
                if base.exists():
                    default_extract += sorted(str(p) for p in base.rglob("*") if p.is_file() and "Ome.Launcher.Spike" in str(p))
        warm = sorted(times[1:])
        linux[name] = dict(
            built,
            **facts,
            natives_loaded=[m for m in modules if NATIVE.search(m)],
            extracted_files=extracted,
            default_extract=default_extract,
            first_run_ms=times[0],
            warm_ms=times[1:],
            warm_median_ms=int(statistics.median(warm)),
        )
        if name == "folder":
            system = sorted(
                m for m in modules
                if not m.startswith(str(out)) and not m.startswith(str(extract_base)) and ".so" in m
            )
            linux["folder"]["system_libraries"] = [f"{m} [{package_of(m)}]" for m in system]
            elf = [program] + sorted(out.glob("*.so"))
            linux["folder"]["glibc_needs"] = {p.name: glibc_needs(p) for p in elf}
            linux["folder"]["ldd_not_found"] = {p.name: unresolved(p) for p in elf if unresolved(p)}
        log(
            f"linux-x64 {name:<16} files={facts['files']:>3} folder={facts['folder_bytes'] / 2**20:7.1f} MiB "
            f"tar.gz={facts['tar_gz_bytes'] / 2**20:6.1f} MiB first={times[0]:>5} ms warm-median={linux[name]['warm_median_ms']:>5} ms "
            f"publish-peak={built['peak_tree_rss_kib'] / 1024:6.0f} MiB (time -v {built['time_max_rss_kib'] / 1024:6.0f} MiB)"
        )
    results["linux-x64"] = linux

    log("--check-like with no display")
    nodisplay = {k: v for k, v in os.environ.items() if k not in ("DISPLAY", "WAYLAND_DISPLAY", "DOTNET_ROOT")}
    checks = {}
    for name in ("folder", "single-extract"):
        program = Path(linux[name]["out"]) / "Ome.Launcher.Spike"
        run = subprocess.run([str(program), "--check-like"], env=nodisplay, capture_output=True, text=True, timeout=60)
        checks[name] = {"exit": run.returncode, "stdout": run.stdout.splitlines(), "stderr": run.stderr.splitlines()}
    results["check_like_no_display"] = checks

    log("win-x64 cross-built here (question 2)")
    windows: dict = {}
    for name, extra in (
        ("single-icon", SINGLE + [f"-p:SpikeIcon={icon}"]),
        ("folder-icon", [f"-p:SpikeIcon={icon}"]),
        ("single-r2r-icon", SINGLE + ["-p:PublishReadyToRun=true", f"-p:SpikeIcon={icon}"]),
    ):
        built = publish(project, "win-x64", name, extra)
        destination = OUT / "publish" / f"win-x64-from-linux-{name}"
        shutil.rmtree(destination, ignore_errors=True)
        shutil.copytree(built["out"], destination)
        windows[name] = dict(built, copied_to=str(destination))
        log(f"win-x64 {name:<16} publish-peak={built['peak_tree_rss_kib'] / 1024:6.0f} MiB warnings={built['warnings']}")
    results["win-x64-from-linux"] = windows

    (OUT / "results-linux.json").write_text(json.dumps(results, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    log(f"wrote {OUT / 'results-linux.json'}")


if __name__ == "__main__":
    main()
