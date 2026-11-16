#!/usr/bin/env python3
"""The rest of M0 on Linux (docs/superpowers/plans/2026-11-15-launcher.md §2.2), run inside WSL's Ubuntu after
publish-linux.py (whose linux-x64 folder publish it uses):

- question 8: the run lock's exclusive open across two processes, with a readiness file and no sleep, on an ext4
  folder and on a /mnt (Windows drive) folder; a killed holder's file; the write time;
- question 6: the v0.6.0 Linux engine captured by the spike (a .NET parent): `--headless -- --package-smoke`, and a
  real start on the game install under WSLg, killed at a timeout. Its data root (build/launcher-m0/roots/q6-linux,
  with the game's quiet settings) is made beforehand on Windows by release-engine.py's make_root.
- ReadyToRun's publish memory with crossgen2's parallelism limited (question 10's follow-up).

Results: build/launcher-m0/results-linux-rest.json. Python 3.8 compatible.
"""
from __future__ import annotations

import importlib.util
import json
import os
import shutil
import signal
import subprocess
import tarfile
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("publish_linux", HERE / "publish-linux.py")
publish_linux = importlib.util.module_from_spec(spec)
spec.loader.exec_module(publish_linux)
WORK, OUT, REPO = publish_linux.WORK, publish_linux.OUT, publish_linux.REPO
SPIKE = WORK / "publish" / "linux-x64-folder" / "Ome.Launcher.Spike"
GAME = REPO.parent / "Himegari_Game"


def lock_case(folder: Path) -> list:
    shutil.rmtree(folder, ignore_errors=True)
    folder.mkdir(parents=True)
    lock, ready = folder / "run.lock", folder / "ready"

    def probe(label: str) -> dict:
        run = subprocess.run([str(SPIKE), "--lock", str(lock)], capture_output=True, text=True)
        return {"step": label, "exit": run.returncode, "output": run.stdout.strip()}

    steps = [probe("first open, nothing holds it")]
    holder = subprocess.Popen([str(SPIKE), "--lock", str(lock), "--hold-until-killed", "--ready", str(ready)],
                              stdout=subprocess.PIPE, text=True)
    deadline = time.monotonic() + 20
    while not ready.exists():
        if time.monotonic() > deadline or holder.poll() is not None:
            raise SystemExit(f"the holder in {folder} did not become ready")
        time.sleep(0.02)
    steps.append(probe("second open while the holder runs"))
    other = {"step": "other access while held"}
    try:
        lock.read_bytes()
        other["read"] = "ok"
    except OSError as error:
        other["read"] = str(error)
    steps.append(other)
    holder.send_signal(signal.SIGKILL)
    holder.wait()
    steps.append({"step": "holder killed", "holder_output": holder.stdout.read().strip(), "file_left": lock.exists()})
    steps.append(probe("open after the holder was killed"))
    return steps


def capture(log: Path, timeout: int, *command: str) -> str:
    run = subprocess.run([str(SPIKE), "--capture", "--log", str(log), "--timeout", str(timeout), "--", *command],
                         capture_output=True, text=True)
    return run.stdout.strip()


def main() -> None:
    results: dict = {}
    results["q8"] = {
        "ext4": lock_case(WORK / "lock-ext4"),
        "mnt": lock_case(OUT / "lock-linux-mnt"),
    }

    engine_root = WORK / "releases" / "v0.6.0"
    if not (engine_root / "OME-linux-x64").exists():
        engine_root.mkdir(parents=True, exist_ok=True)
        with tarfile.open(OUT / "releases" / "v0.6.0" / "OME-linux-x64.tar.gz") as archive:
            archive.extractall(engine_root)
    engine = engine_root / "OME-linux-x64" / "OME"
    data_root = OUT / "roots" / "q6-linux"
    results["q6"] = {
        "engine": str(engine),
        "smoke": capture(OUT / "q6-linux-smoke.log", 60, str(engine), "--headless", "--", "--package-smoke"),
        "real_start": capture(OUT / "q6-linux-real.log", 25, str(engine), "--", "--game-root", str(GAME),
                              "--data-root", str(data_root), "--desktop-dir", str(data_root / "Desktop"),
                              "--windowed"),
    }

    project = publish_linux.sync()
    r2r: dict = {}
    for parallelism in ("1", "2"):
        built = publish_linux.publish(project, "linux-x64", f"single-r2r-par{parallelism}", publish_linux.SINGLE + [
            "-p:PublishReadyToRun=true", f"-p:PublishReadyToRunCrossgen2ExtraArgs=--parallelism:{parallelism}"])
        r2r[parallelism] = {k: built[k] for k in ("seconds", "peak_tree_rss_kib", "time_max_rss_kib")}
        print(f"r2r parallelism {parallelism}: {r2r[parallelism]}", flush=True)
    results["r2r_parallelism"] = r2r

    (OUT / "results-linux-rest.json").write_text(json.dumps(results, indent=2, ensure_ascii=False) + "\n",
                                                encoding="utf-8")
    print(json.dumps(results, indent=2, ensure_ascii=False))


if __name__ == "__main__":
    main()
