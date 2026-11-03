#!/usr/bin/env python3
"""Drive native AGE or the Godot port with one step language: input, screenshots, VM position, sandboxed saves.

    py -3.11 -X utf8 tools/drive.py [--profile ID] native|port start [options]
    py -3.11 -X utf8 tools/drive.py native|port do "click title.start" settle "shot title"
    py -3.11 -X utf8 tools/drive.py native|port run tools/drive/scenarios/himegari-save-load.steps
    py -3.11 -X utf8 tools/drive.py native|port status
    py -3.11 -X utf8 tools/drive.py native|port stop
    py -3.11 -X utf8 tools/drive.py native|port steps
    py -3.11 -X utf8 tools/drive.py native backup | restore DIR

Sessions belong to the game side (the native daemon or the port process), so separate ``do``/``run`` calls drive the
same running game. Steps run in this process over the shared protocol (tools/drive_core.py); a step the target
cannot run fails before the first one starts. Output lives in ``build/games/<profile>/drive/<target>/``.

Full usage: docs/tools-reference.md ("Game driver"). Design: docs/superpowers/specs/2026-11-03-game-driving-design.md.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parent))
import paths  # noqa: E402
import drive_core as core  # noqa: E402
from drive_core import DriveError  # noqa: E402


def print_result(result: dict[str, Any], as_json: bool) -> None:
    print(json.dumps(result, ensure_ascii=False) if as_json else core.describe(result), flush=True)


def run_steps(target: str, steps: list[core.Step], as_json: bool) -> int:
    with core.connect(target) as client:
        core.check_capabilities(steps, client.capabilities, f"{target} target")
        for event in client.take_events():          # whatever happened since the last client (dialogs, exits)
            fields = {k: v for k, v in event.items() if k not in ("t", "kind")}
            print(json.dumps(event, ensure_ascii=False) if as_json else
                  f"event {event.get('kind')}: {json.dumps(fields, ensure_ascii=False)}", flush=True)
        on_close = None
        if target == "port":
            import drive_port
            info = core.read_session("port")

            def on_close(_: dict[str, Any]) -> dict[str, Any]:
                return {"exit_code": drive_port.wait_exit(info, 20)} if info else {}
        executor = core.Executor(client, core.load_profile(), core.drive_root(target) / "shots", on_close=on_close)
        ok = executor.run(steps, lambda result: print_result(result, as_json))
    return 0 if ok else 1


def command_start(arguments: argparse.Namespace) -> int:
    if arguments.target == "native":
        import drive_native
        return drive_native.start(arguments)
    import drive_port
    return drive_port.start(arguments)


def command_stop(arguments: argparse.Namespace) -> int:
    if arguments.target == "native":
        import drive_native
        return drive_native.stop(arguments.timeout)
    import drive_port
    return drive_port.stop(arguments.timeout)


def command_do(arguments: argparse.Namespace) -> int:
    steps = [core.parse_step(text) for text in arguments.steps]
    return run_steps(arguments.target, steps, arguments.json)


def command_run(arguments: argparse.Namespace) -> int:
    path = Path(arguments.file)
    try:
        steps = core.parse_steps(path.read_text(encoding="utf-8").splitlines())
    except DriveError as error:
        raise DriveError(f"{path}: {error}") from None
    return run_steps(arguments.target, steps, arguments.json)


def command_status(arguments: argparse.Namespace) -> int:
    info = core.read_session(arguments.target)
    if info is None:
        print(f"drive {arguments.target}: no session")
        return 1
    with core.Client(int(info["port"]), info["token"]) as client:
        status: dict[str, Any] = {"session": {k: v for k, v in info.items() if k != "token"}, "hello": client.hello}
        try:
            state = client.call("state")
            status["state"] = core.describe_stack(state)
        except DriveError as error:
            status["state"] = f"unavailable: {error}"
        status["events"] = client.take_events()
    print(json.dumps(status, ensure_ascii=False, indent=1))
    return 0


def command_steps(arguments: argparse.Namespace) -> int:
    for name, spec in core.STEPS.items():
        print(spec.usage + (f"   [needs {spec.capability}]" if spec.capability else ""))
    print("\nconditions: at=S@OFF (any frame), top=S@OFF (innermost), script=S, waiting=KIND (port: "
          f"{', '.join(core.WAITING_KINDS)}); every condition given must match")
    profile = core.load_profile()
    if profile.points:
        print(f"\npoints ({paths.CONTEXT.profile_id}): " + ", ".join(sorted(profile.points)))
    return 0


def command_backup(arguments: argparse.Namespace) -> int:
    import drive_native
    return drive_native.backup()


def command_restore(arguments: argparse.Namespace) -> int:
    import drive_native
    return drive_native.restore(arguments.backup)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    targets = parser.add_subparsers(dest="target", required=True, metavar="native|port")
    for target in core.TARGETS:
        target_parser = targets.add_parser(target, help=f"drive the {'native AGE' if target == 'native' else 'Godot port'}")
        commands = target_parser.add_subparsers(dest="command", required=True)
        start = commands.add_parser("start", help="launch the game under the driver; returns once it is up")
        if target == "native":
            import drive_native
            drive_native.add_launch_options(start)
            start.add_argument("--inline", action="store_true", help="run the daemon in this process (debugging)")
            start.add_argument("--timeout", type=float, default=90.0, help="seconds to wait for AGE's window")
        else:
            import drive_port
            drive_port.add_launch_options(start)
            start.add_argument("--timeout", type=float, default=180.0,
                               help="seconds to wait for the control channel (includes Godot's boot)")
        start.set_defaults(handler=command_start)
        do = commands.add_parser("do", help="run steps given on the command line")
        do.add_argument("steps", nargs="+")
        do.add_argument("--json", action="store_true", help="print one JSON result per step")
        do.set_defaults(handler=command_do)
        run = commands.add_parser("run", help="run a .steps file")
        run.add_argument("file")
        run.add_argument("--json", action="store_true")
        run.set_defaults(handler=command_run)
        status = commands.add_parser("status", help="session, hello, VM position, new events")
        status.set_defaults(handler=command_status)
        stop = commands.add_parser("stop", help="close the game and end the session")
        stop.add_argument("--timeout", type=float, default=20.0)
        stop.set_defaults(handler=command_stop)
        steps = commands.add_parser("steps", help="list the step syntax and this profile's named points")
        steps.set_defaults(handler=command_steps)
        if target == "native":
            backup = commands.add_parser("backup", help="copy the live save folder into drive/native/backups/")
            backup.set_defaults(handler=command_backup)
            restore = commands.add_parser("restore",
                                          help="replace the live save folder with a backup (the current one is kept)")
            restore.add_argument("backup")
            restore.set_defaults(handler=command_restore)
    return parser


def main(argv: list[str] | None = None) -> int:
    arguments = build_parser().parse_args(argv)
    try:
        return arguments.handler(arguments)
    except DriveError as error:
        print(f"drive {arguments.target}: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
