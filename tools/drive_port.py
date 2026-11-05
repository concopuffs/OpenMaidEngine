#!/usr/bin/env python3
"""The port target of the driving protocol: launch the Godot port with its ``--control`` channel.

The port serves the protocol itself (``engine/Age.Engine.Frontend/Control/``, ``godot/Main.Drive.cs``); this module
only starts and stops it. ``start`` builds the Godot C# project (``--no-build`` skips that), prepares a save
sandbox, and runs the Godot .NET console binary (``--godot``, ``AGE_GODOT_CONSOLE`` or PATH) with

    --path godot -- --game-root <install> --data-root <sandbox> --control --control-file <session.json>

``--data-root`` is the sandbox itself, in the native profile layout (SYS4REG.INI, SAVE\\), so a port sandbox and a
native one can seed each other. Its SYS4REG.INI gets the profile's ``[sandbox.settings]`` and, unless ``--sound``,
``[sandbox.quiet]``. The token travels in ``OME_DRIVE_TOKEN``. The window goes to the bottom of the z-order without
being activated unless ``--foreground``; injected input does not need focus, and the port drops real input for the
whole run.

Usage and steps: docs/tools-reference.md ("Game driver").
"""
from __future__ import annotations

import argparse
import json
import os
import secrets
import subprocess
import sys
import time
from pathlib import Path
from typing import Callable

sys.path.insert(0, str(Path(__file__).resolve().parent))
import paths  # noqa: E402
import drive_core as core  # noqa: E402
from drive_core import DriveError  # noqa: E402

GODOT_PROJECT = paths.REPO / "godot"


def resolve_godot(explicit: str | None) -> Path:
    from validate import resolve_godot as resolve   # the one resolution rule (--godot, AGE_GODOT_CONSOLE, PATH)
    try:
        return resolve(explicit)
    except ValueError as error:
        raise DriveError(str(error)) from None


def add_launch_options(command: argparse.ArgumentParser) -> None:
    command.add_argument("--sandbox", help="persistent named save sandbox (default: 'fresh', emptied every start)")
    command.add_argument("--seed", help="fill a new sandbox from 'live', 'native:NAME', 'port:NAME' or a folder")
    command.add_argument("--sound", action="store_true",
                         help="keep the sandbox profile's volume (default: the profile's [sandbox.quiet] settings)")
    command.add_argument("--foreground", action="store_true",
                         help="leave the window where Windows puts it (default: the bottom of the z-order)")
    command.add_argument("--godot", help="Godot .NET console binary (default: AGE_GODOT_CONSOLE, then PATH)")
    command.add_argument("--no-build", action="store_true", help="skip `dotnet build godot/OME.csproj`")
    command.add_argument("--mod", action="append", default=[], metavar="PATH",
                         help="load this mod package (.omp or folder) in addition to the sandbox's mods/ (repeatable)")
    command.add_argument("--port-arg", action="append", default=[], metavar="ARG",
                         help="extra argument for the port after --, e.g. --port-arg=--locator-hud (repeatable)")


def build() -> None:
    command = ["dotnet", "build", str(GODOT_PROJECT / "OME.csproj"), "--nologo", "--verbosity", "minimal"]
    print("drive port: " + " ".join(command[:3]), flush=True)
    completed = subprocess.run(command, cwd=paths.REPO, capture_output=True, text=True, encoding="utf-8",
                               errors="replace")
    if completed.returncode != 0:
        print(completed.stdout[-4000:], completed.stderr[-2000:], file=sys.stderr)
        raise DriveError(f"dotnet build failed ({completed.returncode})")


def start(arguments: argparse.Namespace) -> int:
    root = core.drive_root("port")
    root.mkdir(parents=True, exist_ok=True)
    session = core.session_file("port")
    if core.read_session("port") is not None:
        raise DriveError("a port session is already running; `drive.py port stop` first")
    session.unlink(missing_ok=True)
    godot = resolve_godot(arguments.godot)
    if not arguments.no_build:
        build()
    profile = core.load_profile()
    sandbox, profile_root = core.prepare_sandbox(
        "port", arguments.sandbox or "fresh", arguments.seed, arguments.sound, profile,
        log=lambda message: print(f"drive port: {message}", flush=True))
    token = secrets.token_hex(16)
    environment = {**os.environ, core.TOKEN_ENVIRONMENT: token}
    command = [str(godot), "--path", str(GODOT_PROJECT), "--",
               "--game-root", str(paths.CONTEXT.game_root), "--data-root", str(profile_root.resolve()),
               "--control", "--control-file", str(session.resolve()),
               *[part for mod in arguments.mod for part in ("--mod", str(Path(mod).resolve()))], *arguments.port_arg]
    log_path = root / "godot.log"
    log = log_path.open("wb")
    flags = 0x00000008 | 0x00000200            # DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP
    process = subprocess.Popen(command, cwd=paths.REPO, stdout=log, stderr=subprocess.STDOUT,
                               stdin=subprocess.DEVNULL, env=environment, creationflags=flags)
    core.write_json_atomic(root / "launch.json", {
        "launcher_pid": process.pid, "command": command, "sandbox": str(sandbox), "profile_root": str(profile_root),
        "log": str(log_path), "started": core.now_text(), "foreground": bool(arguments.foreground),
    })
    deadline = time.monotonic() + arguments.timeout
    while time.monotonic() < deadline:
        info = core.read_session("port")
        if info is not None and info.get("token") == token:
            if not arguments.foreground:
                core.send_windows_to_bottom(int(info["pid"]))
            with core.Client(int(info["port"]), token) as client:
                hello = client.hello
            print(f"drive port: pid {info['pid']} on 127.0.0.1:{info['port']}, profile {hello.get('profile')}, "
                  f"capabilities {', '.join(hello.get('capabilities', []))}")
            print(f"drive port: saves sandbox {profile_root}; log {log_path}")
            return 0
        if process.poll() is not None and core.read_session("port") is None:
            print(log_path.read_bytes().decode("utf-8", errors="replace")[-3000:], file=sys.stderr)
            raise DriveError(f"the port exited ({process.returncode}) before opening its control channel")
        time.sleep(0.25)
    raise DriveError(f"no control session within {arguments.timeout:.0f} s; see {log_path}")


def window_placement(info: dict | None) -> Callable[[dict], None] | None:
    """What runs after a `window` step: a restore activates the window, so it goes back to the bottom of the z-order
    as at start, unless the session was started with --foreground."""
    launch = core.drive_root("port") / "launch.json"
    foreground = launch.is_file() and json.loads(launch.read_text(encoding="utf-8")).get("foreground", False)
    if info is None or foreground:
        return None

    def place(result: dict) -> None:
        if not result.get("minimized"):
            core.send_windows_to_bottom(int(info["pid"]))
    return place


def wait_exit(info: dict, timeout: float) -> int | None:
    """Wait for the port's process (and the console launcher in front of it) to exit; terminate stragglers."""
    code = core.windows().wait_exit(int(info["pid"]), timeout)
    if code is None:
        core.windows().terminate(int(info["pid"]))
    launch = core.drive_root("port") / "launch.json"
    if launch.is_file():
        launcher = int(json.loads(launch.read_text(encoding="utf-8")).get("launcher_pid", 0))
        if launcher and core.windows().wait_exit(launcher, 5) is None:
            core.windows().terminate(launcher)
    return code


def stop(timeout: float) -> int:
    info = core.read_session("port")
    if info is None:
        print("drive port: no session")
        return 0
    try:
        with core.Client(int(info["port"]), info["token"]) as client:
            client.call("close")
    except DriveError as error:
        print(f"drive port: {error}", file=sys.stderr)
    code = wait_exit(info, timeout)
    print(f"drive port: the port exited ({code})" if code is not None else "drive port: terminated the port")
    return 0
