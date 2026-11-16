"""M0 questions 6 and 7 on Windows (docs/superpowers/plans/2026-11-15-launcher.md §2.2), against the v0.6.0 release:

- q6: a real start of the release engine on the game install, captured by the spike (a .NET parent): which lines
  reach it, on which stream, and when;
- q7: two release engines on one --data-root, both captured: what each logs, and which files of the root each
  start wrote. Nothing in v0.6.0 has the run lock; this is the baseline it changes.

Each data root gets the game profile's quiet settings, as the driver's sandboxes do, and every engine window goes to
the bottom of the z-order. Usage (from age-reimpl/): py -3.11 -X utf8 launcher/Ome.Launcher.Spike/measure/release-engine.py
"""
from __future__ import annotations

import json
import shutil
import subprocess
import sys
import threading
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPO / "tools"))
import drive_core as core  # noqa: E402
import paths  # noqa: E402

M0 = REPO / "build" / "launcher-m0"
SPIKE = M0 / "publish" / "win-x64-folder" / "Ome.Launcher.Spike.exe"
ENGINE = M0 / "releases" / "v0.6.0" / "windows" / "OME-windows-x64" / "OME.exe"


def make_root(root: Path) -> Path:
    shutil.rmtree(root, ignore_errors=True)
    root.mkdir(parents=True)
    profile = core.load_profile()
    ini = root / profile.settings_file
    ini.write_bytes(core.update_ini("", {**profile.settings, **profile.quiet}).encode("cp932"))
    (root / "Desktop").mkdir()
    return root


def captured(log: Path, timeout: int, root: Path) -> subprocess.Popen:
    command = [str(SPIKE), "--capture", "--log", str(log), "--timeout", str(timeout), "--", str(ENGINE),
               "--", "--game-root", str(paths.CONTEXT.game_root), "--data-root", str(root),
               "--desktop-dir", str(root / "Desktop"), "--windowed"]
    return subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)


def engine_pids() -> set[int]:
    out = subprocess.run(["powershell", "-NoProfile", "-Command",
                          f"Get-Process OME -ErrorAction SilentlyContinue | Where-Object {{ $_.Path -eq '{ENGINE}' }} | "
                          "ForEach-Object { $_.Id }"], capture_output=True, text=True).stdout
    return {int(line) for line in out.split()}


def keep_at_bottom(stop: threading.Event) -> None:
    """Every engine window this run starts goes to the bottom, as the driver's do."""
    seen: set[int] = set()
    while not stop.is_set():
        for pid in engine_pids() - seen:
            seen.add(pid)
            threading.Thread(target=core.send_windows_to_bottom, args=(pid, 20.0), daemon=True).start()
        stop.wait(0.5)


def tree(root: Path) -> dict:
    return core.snapshot_tree(root)


def changed(before: dict, after: dict) -> list[str]:
    return sorted(path for path, facts in after.items() if before.get(path) != facts)


def main() -> None:
    stop = threading.Event()
    threading.Thread(target=keep_at_bottom, args=(stop,), daemon=True).start()
    results: dict = {}
    try:
        # q6: one real start, killed after 25 s.
        root = make_root(M0 / "roots" / "q6")
        before = tree(root)
        first = captured(M0 / "q6-win-real.log", 25, root)
        results["q6"] = {"capture": first.communicate()[0].strip(), "wrote": changed(before, tree(root))}

        # q7: a second start on the same root while the first runs.
        root = make_root(M0 / "roots" / "q7")
        empty = tree(root)
        a = captured(M0 / "q7-win-first.log", 45, root)
        time.sleep(15)
        mid = tree(root)
        b = captured(M0 / "q7-win-second.log", 20, root)
        b_out = b.communicate()[0].strip()
        after_b = tree(root)
        a_out = a.communicate()[0].strip()
        results["q7"] = {
            "first": a_out,
            "second": b_out,
            "first_wrote_before_second": changed(empty, mid),
            "changed_while_both_ran": changed(mid, after_b),
            "changed_after_second_ended": changed(after_b, tree(root)),
        }
    finally:
        stop.set()
    (M0 / "results-release-engine.json").write_text(json.dumps(results, indent=2, ensure_ascii=False) + "\n",
                                                   encoding="utf-8")
    print(json.dumps(results, indent=2, ensure_ascii=False))


if __name__ == "__main__":
    main()
