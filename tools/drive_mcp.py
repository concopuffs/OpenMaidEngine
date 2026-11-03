#!/usr/bin/env python3
"""MCP server for the game driver: drive.py's operations as five tools for agents (Claude Code, Codex).

    py -3.11 -X utf8 tools/drive_mcp.py [--profile ID]

Register it once per agent; a newly added server loads at the agent's next session:

    claude mcp add drive --scope local -- py -3.11 -X utf8 <repo>/tools/drive_mcp.py
    codex mcp add drive -- py -3.11 -X utf8 <repo>/tools/drive_mcp.py

It is a thin client with no logic of its own. drive_start and drive_stop run `drive.py <target> start|stop`.
drive_steps and drive_screenshot run steps through drive.execute_steps, the code path `drive.py do|run` uses.
drive_state returns drive.session_status. Sessions belong to the game side, so this server and the CLI drive the
same running game. It needs the optional `mcp` package (2.x): `py -3.11 -m pip install --user mcp`.

Usage: docs/tools-reference.md ("Game driver"). Design: docs/superpowers/specs/2026-11-03-game-driving-design.md §8.
"""
from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path
from typing import Any, Literal

sys.path.insert(0, str(Path(__file__).resolve().parent))
import paths  # noqa: E402
import drive  # noqa: E402
import drive_core as core  # noqa: E402
from drive_core import DriveError  # noqa: E402

try:
    from mcp.server.mcpserver import MCPServer
    from mcp.server.mcpserver.exceptions import ToolError
    from mcp.server.mcpserver.utilities.types import Image
except ImportError as error:  # pragma: no cover - depends on the optional package
    raise SystemExit(f"drive_mcp: the optional `mcp` package (2.x) is missing ({error}); "
                     "install it with `py -3.11 -m pip install --user mcp`") from None

Target = Literal["native", "port"]
DRIVE_PY = Path(__file__).resolve().parent / "drive.py"
START_TIMEOUT_S = 600.0          # the port's start includes `dotnet build` and Godot's boot

INSTRUCTIONS = f"""Drive native AGE ("native") or the Godot port ("port") of the profile {paths.CONTEXT.profile_id!r} \
with one step language; the same steps run on both targets.

Typical use: drive_start(target), then drive_steps(target, steps=[...]) as often as needed (each call returns the
per-step results and a screenshot after the last step), drive_state(target) for the VM position, and
drive_stop(target), which closes the game the way its own quit does. A session outlives a call, so later calls drive
the same running game. Saves go to a sandbox (default 'fresh', emptied on every start); seed a new sandbox from
another target's with seed='port:fresh' or 'native:fresh'. Windows stay muted, unfocused and at the bottom of the
z-order.

Positions read SCRIPT@offset (dword offsets, as in the disassembly and the page locator). At a text wait the pc is
already past the wait instruction, so a page reads as the page map's next page_start_offset. The driver refuses ESC
on native AGE (it closes the game). Committed scenarios live in tools/drive/scenarios/*.steps (run one with
drive_steps(target, file=...)).

Steps (one per string; '#' starts a comment):
{drive.steps_help()}
"""

server = MCPServer("drive", instructions=INSTRUCTIONS)


def run_cli(*arguments: str, timeout: float) -> str:
    """Run drive.py with this server's profile context and return its output; a failure becomes the tool's error."""
    command = [sys.executable, "-X", "utf8", str(DRIVE_PY), *arguments]
    try:
        completed = subprocess.run(
            command, cwd=paths.REPO, stdin=subprocess.DEVNULL, capture_output=True, text=True, encoding="utf-8",
            errors="replace", env={**os.environ, **paths.CONTEXT.subprocess_environment()}, timeout=timeout)
    except subprocess.TimeoutExpired:
        raise ToolError(f"drive.py {' '.join(arguments)}: no answer within {timeout:.0f} s") from None
    text = "\n".join(part.strip() for part in (completed.stdout, completed.stderr) if part.strip())
    if completed.returncode != 0:
        raise ToolError(text or f"drive.py {' '.join(arguments)} failed ({completed.returncode})")
    return text


def load_steps(steps: list[str] | None, file: str | None) -> list[core.Step]:
    if (steps is None) == (file is None):
        raise ToolError("give either steps or file")
    if file is not None:
        path = Path(file)
        if not path.is_absolute():
            path = paths.REPO / path
        try:
            lines = path.read_text(encoding="utf-8").splitlines()
        except OSError as error:
            raise ToolError(f"{path}: {error}") from None
    else:
        lines = [line for text in steps or [] for line in text.splitlines()]
    try:
        parsed = core.parse_steps(lines)
    except DriveError as error:
        raise ToolError(f"{file}: {error}" if file else str(error)) from None
    if not parsed:
        raise ToolError("no steps")
    return parsed


def shot_text(shot: dict[str, Any]) -> str:
    size = "x".join(str(value) for value in shot.get("size") or [])
    return f"screenshot {shot['path']} {size} at {shot.get('location')}"


def run(target: str, steps: list[core.Step], screenshot: bool) -> list[str | Image]:
    """Run steps the way `drive.py do` does; return a summary, one line per result and the final screenshot."""
    results: list[dict[str, Any]] = []
    events: list[dict[str, Any]] = []
    shot: dict[str, Any] = {}

    def finish(executor: core.Executor) -> None:
        if not screenshot:
            return
        try:
            shot.update(executor.execute(core.parse_step("shot")))
        except DriveError as error:
            shot["error"] = str(error)
    try:
        ok = drive.execute_steps(target, steps, results.append, on_event=events.append, finish=finish)
    except DriveError as error:
        raise ToolError(str(error)) from None
    seconds = round(sum(result.get("seconds", 0) for result in results), 2)
    if ok:
        summary = f"ok: {len(results)} of {len(steps)} steps in {seconds} s"
    else:
        summary = f"FAILED at step {len(results)} of {len(steps)}: {results[-1].get('error')}"
    lines = [summary, *map(core.describe_event, events), *map(core.describe, results)]
    if shot.get("path"):
        return ["\n".join([*lines, shot_text(shot)]), Image(path=shot["path"])]
    if shot.get("error"):
        lines.append(f"no screenshot: {shot['error']}")
    return ["\n".join(lines)]


@server.tool()
def drive_start(target: Target, sandbox: str | None = None, seed: str | None = None, sound: bool = False,
                foreground: bool = False, build: bool = True) -> str:
    """Launch the game under the driver and return once it is up (`drive.py <target> start`).

    sandbox: named save sandbox kept between runs (default 'fresh', emptied on every start).
    seed: fill a new sandbox from 'live', 'native:NAME', 'port:NAME' or a folder.
    sound: keep the profile's volume (default: muted).
    foreground: leave the window where Windows puts it (default: bottom of the z-order, not activated).
    build: port only; false skips `dotnet build` of the Godot project (start takes about 30-90 s with the build).
    Fails if the target already has a session; drive_stop it first."""
    arguments = [target, "start"]
    if sandbox:
        arguments += ["--sandbox", sandbox]
    if seed:
        arguments += ["--seed", seed]
    if sound:
        arguments.append("--sound")
    if foreground:
        arguments.append("--foreground")
    if not build:
        if target != "port":
            raise ToolError("build=false applies to the port only")
        arguments.append("--no-build")
    return run_cli(*arguments, timeout=START_TIMEOUT_S)


@server.tool()
def drive_steps(target: Target, steps: list[str] | None = None, file: str | None = None,
                screenshot: bool = True) -> list[str | Image]:
    """Run steps on the target's running session (`drive.py <target> do|run`).

    steps: step lines in the .steps language (see the server instructions), e.g. ["click title.start", "settle",
    "expect top=SC0000@0x83f"]. file: a .steps file instead (repo-relative or absolute).
    Runs in order and stops at the first failing step or after `close`. Returns a summary line, one line per step
    result and, unless screenshot=false or a step closed the game, a screenshot taken after the last step."""
    return run(target, load_steps(steps, file), screenshot)


@server.tool()
def drive_screenshot(target: Target, name: str | None = None) -> list[str | Image]:
    """Capture the game's client area as a PNG and return it (the `shot` step). name: file name under the target's
    shots/ folder (default: a timestamp)."""
    step = core.parse_step(f"shot {name}" if name else "shot")
    results: list[dict[str, Any]] = []
    try:
        drive.execute_steps(target, [step], results.append)
    except DriveError as error:
        raise ToolError(str(error)) from None
    result = results[0]
    if not result.get("ok"):
        raise ToolError(result.get("error") or "shot failed")
    return [shot_text(result), Image(path=result["path"])]


@server.tool()
def drive_state(target: Target) -> str:
    """The VM stack (innermost frame last), the session, the target's hello (capabilities) and the events since the
    last client (`drive.py <target> status`)."""
    try:
        status = drive.session_status(target)
    except DriveError as error:
        raise ToolError(str(error)) from None
    if status is None:
        return f"no {target} session; drive_start it first"
    return json.dumps(status, ensure_ascii=False, indent=1)


@server.tool()
def drive_stop(target: Target, timeout: float = 20.0) -> str:
    """Close the game the way its own quit does and end the session (`drive.py <target> stop`). AGE's quit writes
    SAVE.DAT and RT.DAT into the sandbox."""
    return run_cli(target, "stop", "--timeout", str(timeout), timeout=timeout + 60.0)


def main() -> int:
    server.run("stdio")
    return 0


if __name__ == "__main__":
    sys.exit(main())
