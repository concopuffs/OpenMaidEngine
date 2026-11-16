"""M0 question 9 (docs/superpowers/plans/2026-11-15-launcher.md §2.2): a Windows shortcut whose arguments are
`--rendering-driver d3d12 -- …` starts the v0.6.0 engine on Direct3D 12. The shortcut is made with WScript.Shell and
opened as Explorer opens it; Godot's own log (user://logs/godot.log) names the driver it started.

The data root gets the game profile's quiet settings, and the window goes to the bottom of the z-order.
Usage (from age-reimpl/): py -3.11 -X utf8 launcher/Ome.Launcher.Spike/measure/shortcut-d3d12.py
"""
from __future__ import annotations

import json
import os
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import importlib.util  # noqa: E402

spec = importlib.util.spec_from_file_location("release_engine", HERE / "release-engine.py")
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)
core, paths, M0, ENGINE = release.core, release.paths, release.M0, release.ENGINE

GODOT_LOGS = Path(os.environ["APPDATA"]) / "Godot" / "app_userdata" / "Open Maid Engine" / "logs"


def make_shortcut(link: Path, arguments: str) -> None:
    script = (
        "$s = (New-Object -ComObject WScript.Shell).CreateShortcut($env:OME_LINK); "
        "$s.TargetPath = $env:OME_TARGET; $s.Arguments = $env:OME_ARGS; "
        "$s.WorkingDirectory = (Split-Path $env:OME_TARGET); $s.WindowStyle = 7; $s.Save()"
    )
    env = dict(os.environ, OME_LINK=str(link), OME_TARGET=str(ENGINE), OME_ARGS=arguments)
    subprocess.run(["powershell", "-NoProfile", "-Command", script], env=env, check=True)


def main() -> None:
    root = release.make_root(M0 / "roots" / "q9")
    link = M0 / "q9" / "OME-d3d12.lnk"
    link.parent.mkdir(parents=True, exist_ok=True)
    arguments = (f'--rendering-driver d3d12 -- --game-root "{paths.CONTEXT.game_root}" --data-root "{root}" '
                 f'--desktop-dir "{root / "Desktop"}" --windowed')
    make_shortcut(link, arguments)
    started = time.time()
    os.startfile(str(link))  # noqa: S606 - opened as Explorer opens it
    deadline = time.monotonic() + 20
    pids: set[int] = set()
    while not pids and time.monotonic() < deadline:
        pids = release.engine_pids()
        time.sleep(0.2)
    if not pids:
        raise SystemExit("the engine did not start from the shortcut")
    for pid in pids:
        core.send_windows_to_bottom(pid, 20.0)
    time.sleep(8)
    # Godot's file log is not flushed when the process is killed, so the driver is read from what the running
    # process loaded: d3d12.dll for Direct3D 12, vulkan-1.dll for Vulkan.
    modules = subprocess.run(
        ["powershell", "-NoProfile", "-Command",
         f"(Get-Process -Id {','.join(map(str, pids))}).Modules | ForEach-Object {{ $_.ModuleName }}"],
        capture_output=True, text=True).stdout.split()
    graphics = sorted({m for m in modules if m.lower() in ("d3d12.dll", "d3d12core.dll", "dxgi.dll", "vulkan-1.dll",
                                                           "opengl32.dll", "dxil.dll", "dxcompiler.dll")})
    for pid in pids:
        subprocess.run(["taskkill", "/PID", str(pid), "/F"], capture_output=True)
    logs = sorted((p for p in GODOT_LOGS.glob("godot*.log") if p.stat().st_mtime >= started - 1),
                  key=lambda p: p.stat().st_mtime)
    text = logs[-1].read_text(encoding="utf-8", errors="replace").splitlines() if logs else []
    driver = [line for line in text if "D3D12" in line or "Vulkan" in line or "OpenGL" in line]
    result = {"shortcut_arguments": arguments, "graphics_modules": graphics,
              "log": str(logs[-1]) if logs else None, "log_lines": len(text), "driver_lines": driver[:5]}
    (M0 / "results-shortcut-d3d12.json").write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n",
                                                   encoding="utf-8")
    print(json.dumps(result, indent=2, ensure_ascii=False))


if __name__ == "__main__":
    main()
