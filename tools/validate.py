#!/usr/bin/env python3
"""Layered project validation driver.

Run from the repository root:
    py -3.11 -X utf8 tools/validate.py [--level core|workspace|runtime|full]

The default is the complete validation level. Every selected prerequisite is required;
the driver never turns an unavailable required gate into a successful skip.
"""
from __future__ import annotations

import argparse
import csv
import os
import shlex
import shutil
import signal
import subprocess
import sys
import time
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent
sys.path.insert(0, str(HERE))
import paths

LEVELS = ("core", "workspace", "runtime", "full")
CORE_TESTS = (
    "test_validate.py",
    "test_install_godot_templates.py",
    "test_package_linux_x64.py",
    "test_package_windows_x64.py",
    "test_dotnet_publish_proxy.py",
    "test_publish_github_release.py",
    "test_release_workflow.py",
    "test_verify_windows_native.py",
    "test_diff_optrace.py",
    "test_engine_ctx.py",
    "test_ghidra_handler_map.py",
    "test_init_table_profile.py",
    "test_locate_page.py",
    "test_opcodes.py",
    "test_paths.py",
    "test_sys4load.py",
    "frida/test_dump_engine.py",
    "frida/test_map_imports.py",
)
WORKSPACE_TESTS = (
    "test_extract_init.py",
    "test_globals.py",
    "test_scjump.py",
)
WORKSPACE_GENERATED_INPUTS = (
    paths.BUILD / "global-var-map.json",
    paths.BUILD / "callscript-names.json",
    paths.BUILD / "scjump-decisions.json",
)
LOW_MEMORY_DOTNET_ENVIRONMENT = (
    ("DOTNET_CLI_USE_MSBUILD_SERVER", "0"),
    ("MSBUILDDISABLENODEREUSE", "1"),
    ("DOTNET_gcServer", "0"),
    ("DOTNET_GCConserveMemory", "9"),
)


@dataclass(frozen=True)
class Gate:
    key: str
    label: str
    command: tuple[str, ...]
    timeout_seconds: int = 180
    environment: tuple[tuple[str, str], ...] = ()


@dataclass(frozen=True)
class GateResult:
    label: str
    status: str
    duration_seconds: float
    detail: str
    log_path: Path | None = None


def _python_tool(relative: str, *arguments: str) -> tuple[str, ...]:
    return (sys.executable, str(HERE / relative), *arguments)


def resolve_godot(explicit: str | None, environ: dict[str, str] | None = None) -> Path:
    """Resolve explicit value, AGE_GODOT_CONSOLE, then known PATH commands."""
    env = os.environ if environ is None else environ
    configured = explicit or env.get("AGE_GODOT_CONSOLE")
    if configured:
        candidate = Path(configured).expanduser()
        if not candidate.is_file():
            raise ValueError(f"Godot .NET console executable not found: {candidate}")
        return candidate.resolve()
    for command in ("godot4", "godot", "godot-mono"):
        if found := shutil.which(command):
            return Path(found).resolve()
    raise ValueError(
        "Godot .NET console executable not found; pass --godot, set "
        "AGE_GODOT_CONSOLE, or add godot4/godot/godot-mono to PATH"
    )


def resolve_game_root(
    explicit: str | None,
    environ: dict[str, str] | None = None,
    conventional: Path | None = None,
) -> Path:
    """Resolve explicit value, AGE_GAME_ROOT, then the conventional sibling install."""
    env = os.environ if environ is None else environ
    candidate = Path(
        explicit or env.get("AGE_GAME_ROOT") or conventional or paths.GAME_DIR
    ).expanduser()
    if not candidate.is_dir():
        raise ValueError(f"game root not found: {candidate}")
    resolved = candidate.resolve()
    if not (resolved / "SYS4INI.BIN").is_file():
        raise ValueError(f"game root does not contain SYS4INI.BIN: {resolved}")
    return resolved


def selected_phases(level: str) -> set[str]:
    if level not in LEVELS:
        raise ValueError(f"unknown validation level: {level}")
    phases = {"core"}
    if level in ("workspace", "full"):
        phases.add("workspace")
    if level in ("runtime", "full"):
        phases.add("runtime")
    if level == "full":
        phases.add("full")
    return phases


def build_gate_plan(
    level: str,
    godot: Path | None = None,
    game_root: Path | None = None,
    runtime_state_root: Path | None = None,
    tool_context: paths.ToolContext | None = None,
) -> list[Gate]:
    context = tool_context or paths.CONTEXT
    profile_environment = tuple(context.subprocess_environment().items())
    low_memory_profile_environment = LOW_MEMORY_DOTNET_ENVIRONMENT + profile_environment
    phases = selected_phases(level)
    gates = [
        Gate("opcodes-build", "Opcode metadata build", _python_tool("opcodes_build.py", "--build")),
        Gate("globals-lint", "Global registry lint", _python_tool("globals_build.py", "--lint")),
        Gate("engine-ctx-lint", "Engine context lint", _python_tool("engine_ctx_build.py", "--lint")),
        Gate(
            "generated-opcode-diff",
            "Generated opcode references",
            (
                "git", "diff", "--exit-code", "--",
                "tools/age_opcode_semantics.py", "docs/opcode-reference.md",
            ),
        ),
    ]
    gates.extend(
        Gate(
            f"python-{Path(test).stem}",
            f"Python {test}",
            _python_tool(test),
        )
        for test in CORE_TESTS
    )
    gates.append(
        Gate(
            "engine-restore",
            ".NET engine restore",
            (
                "dotnet", "restore", "engine/AgeEngine.sln", "--nologo",
                "--verbosity", "minimal", "--disable-parallel",
                "-p:RestoreDisableParallel=true",
            ),
            300,
            low_memory_profile_environment,
        )
    )
    gates.append(
        Gate(
            "engine-cli-build",
            ".NET engine CLI build",
            (
                "dotnet", "build", "engine/Age.Cli/Age.Cli.csproj", "--no-restore", "--nologo",
                "--verbosity", "minimal", "-m:1", "-p:UseSharedCompilation=false",
                "-p:BuildInParallel=false", "-p:DebugType=None", "-p:DebugSymbols=false",
            ),
            300,
            low_memory_profile_environment,
        )
    )
    gates.append(
        Gate(
            "engine-tests",
            ".NET engine core tests",
            (
                "dotnet", "test", "engine/Age.Engine.Tests/Age.Engine.Tests.csproj",
                "--no-restore", "--nologo",
                "--verbosity", "minimal", "--filter", "Category!=Workspace", "-m:1",
                "-p:UseSharedCompilation=false", "-p:BuildInParallel=false",
                "-p:DebugType=None", "-p:DebugSymbols=false",
            ),
            300,
            low_memory_profile_environment,
        )
    )

    if "workspace" in phases:
        workspace_filter = (
            "Category=Workspace&Profile=kamidori"
            if context.profile_id == "kamidori"
            else "Category=Workspace&Profile!=kamidori"
        )
        gates.append(Gate(
            "engine-workspace-tests",
            f".NET installed-data tests ({context.profile_id})",
            (
                "dotnet", "test", "engine/Age.Engine.Tests/Age.Engine.Tests.csproj",
                "--no-restore", "--nologo", "--verbosity", "minimal",
                "--filter", workspace_filter, "-m:1", "-p:UseSharedCompilation=false",
                "-p:BuildInParallel=false", "-p:DebugType=None", "-p:DebugSymbols=false",
            ),
            300,
            low_memory_profile_environment,
        ))
        gates.append(Gate(
            "catalog-corpus-scan",
            f"Catalog/store corpus decode ({context.profile_id})",
            (
                "dotnet", "run", "--project", "engine/Age.Cli/Age.Cli.csproj",
                "--no-build", "--", "catalog-scan", "--profile", context.profile_id,
                "--game-root", str(game_root or context.game_root), "--output",
                str(context.game_build / "catalog-opcode-coverage.json"),
            ),
            300,
            profile_environment,
        ))
        if context.profile_id == "himegari":
            gates.append(Gate(
                "globals-build", "Global registry build",
                _python_tool("globals_build.py", "--build"),
                environment=profile_environment,
            ))
            gates.extend(
                Gate(
                    f"python-{Path(test).stem}",
                    f"Python {test}",
                    _python_tool(test),
                    300,
                    profile_environment,
                )
                for test in WORKSPACE_TESTS
            )
            gates.append(Gate(
                "vm0-recover", "Python VM RECOVER", _python_tool("vm0.py", "--test"),
                environment=profile_environment,
            ))

    if "runtime" in phases:
        if godot is None or game_root is None:
            raise ValueError("runtime validation requires resolved Godot and game-root paths")
        runtime_root = runtime_state_root or paths.BUILD / "validation" / "runtime-user"
        runtime_environment = (
            ("APPDATA", str(runtime_root / "appdata")),
            ("LOCALAPPDATA", str(runtime_root / "local-appdata")),
            ("XDG_DATA_HOME", str(runtime_root / "xdg-data")),
            ("XDG_CONFIG_HOME", str(runtime_root / "xdg-config")),
        )
        gates.extend((
            Gate(
                "godot-build",
                "Godot C# build",
                ("dotnet", "build", "godot/OME.csproj", "--nologo", "--verbosity", "minimal"),
                300,
            ),
            Gate(
                "godot-selftest",
                "Godot threaded self-test",
                (
                    str(godot), "--headless", "--log-file", str(runtime_root / "godot.log"),
                    "--path", str(REPO / "godot"), "--",
                    "--selftest", "--game-root", str(game_root), "--text-backend", "portable",
                ),
                300,
                runtime_environment,
            ),
        ))

    if "full" in phases:
        if context.profile_id == "himegari":
            gates.append(
                Gate(
                    "age-cli-sweep",
                    "C# VM scene sweep",
                    (
                        "dotnet", "run", "--project", "engine/Age.Cli", "--configuration", "Debug",
                        "--", "sweep", "--boot", "--halt-at-wait",
                        "--profile", context.profile_id,
                        "--game-root", str(game_root or context.game_root),
                    ),
                    600,
                    profile_environment,
                )
            )
        elif context.profile_id == "kamidori":
            gates.append(
                Gate(
                    "natural-boot-acceptance",
                    f"C# natural boot/ADV acceptance ({context.profile_id})",
                    (
                        "dotnet", "test", "engine/Age.Engine.Tests/Age.Engine.Tests.csproj",
                        "--no-restore", "--nologo", "--verbosity", "minimal",
                        "--filter",
                        "FullyQualifiedName=NaturalBootIntegrationTests."
                        "KamidoriSystem4Root_NewGameSelectionNaturallyCallsFirstAdvScene",
                    ),
                    600,
                    low_memory_profile_environment,
                )
            )
        else:
            raise ValueError(
                f"Profile {context.profile_id!r} has no full-validation terminal gate"
            )

    gates.append(Gate("diff-check", "Git whitespace check", ("git", "diff", "--check")))
    return gates


def validate_prerequisites(
    level: str, tool_context: paths.ToolContext | None = None
) -> list[str]:
    context = tool_context or paths.CONTEXT
    errors = []
    for executable in ("git", "dotnet"):
        if not shutil.which(executable):
            errors.append(f"required executable not found on PATH: {executable}")
    phases = selected_phases(level)
    if "workspace" in phases:
        if not context.sys4ini.is_file():
            errors.append(f"selected game install is unavailable: {context.game_root}")
        if context.profile_id == "himegari":
            if not context.data1.is_dir():
                errors.append(f"extracted DATA1 corpus not found: {context.data1}")
            for required in WORKSPACE_GENERATED_INPUTS:
                if not required.is_file():
                    errors.append(
                        f"workspace-derived prerequisite not found: {required} "
                        "(rebuild it with the owning tool in docs/tools-reference.md)"
                    )
    return errors


def _command_text(command: tuple[str, ...]) -> str:
    return subprocess.list2cmdline(command) if os.name == "nt" else shlex.join(command)


def _terminate_process_tree(process: subprocess.Popen[str]) -> None:
    if process.poll() is not None:
        return
    if os.name == "nt":
        subprocess.run(
            ("taskkill", "/PID", str(process.pid), "/T", "/F"),
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )
    else:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
    try:
        process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        process.kill()


def run_gate(gate: Gate, log_dir: Path, verbose: bool) -> GateResult:
    started = time.monotonic()
    log_path = log_dir / f"{gate.key}.log"
    environment = os.environ.copy()
    environment.update({
        "PYTHONUTF8": "1",
        "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
        "DOTNET_NOLOGO": "1",
    })
    environment.update(dict(gate.environment))
    popen_arguments = {
        "args": gate.command,
        "cwd": REPO,
        "env": environment,
        "stdout": subprocess.PIPE,
        "stderr": subprocess.STDOUT,
        "text": True,
        "encoding": "utf-8",
        "errors": "replace",
    }
    if os.name == "nt":
        popen_arguments["creationflags"] = subprocess.CREATE_NEW_PROCESS_GROUP
    else:
        popen_arguments["start_new_session"] = True

    print(f"==> {gate.label}")
    process = subprocess.Popen(**popen_arguments)
    timed_out = False
    try:
        output, _ = process.communicate(timeout=gate.timeout_seconds)
    except subprocess.TimeoutExpired:
        timed_out = True
        _terminate_process_tree(process)
        output, _ = process.communicate()
    duration = time.monotonic() - started
    header = f"$ {_command_text(gate.command)}\n\n"
    log_path.write_text(header + output, encoding="utf-8")

    if verbose and output:
        print(output, end="" if output.endswith("\n") else "\n")
    if timed_out:
        detail = f"timeout after {gate.timeout_seconds}s"
        _print_failure_excerpt(output)
        return GateResult(gate.label, "FAIL", duration, detail, log_path)
    if process.returncode != 0:
        detail = f"exit {process.returncode}"
        _print_failure_excerpt(output)
        return GateResult(gate.label, "FAIL", duration, detail, log_path)
    return GateResult(gate.label, "PASS", duration, "ok", log_path)


def _print_failure_excerpt(output: str, line_count: int = 30) -> None:
    lines = output.rstrip().splitlines()
    if lines:
        print("--- failure excerpt ---")
        print("\n".join(lines[-line_count:]))


def snapshot_godot_processes() -> dict[int, str]:
    """Return live Godot-like process ids for a before/after leak audit."""
    processes: dict[int, str] = {}
    try:
        if os.name == "nt":
            completed = subprocess.run(
                ("tasklist", "/FO", "CSV", "/NH"),
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                check=False,
            )
            for row in csv.reader(completed.stdout.splitlines()):
                if len(row) >= 2 and "godot" in row[0].lower():
                    processes[int(row[1])] = row[0]
        else:
            completed = subprocess.run(
                ("ps", "-eo", "pid=,comm="),
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                check=False,
            )
            for line in completed.stdout.splitlines():
                pid_text, _, name = line.strip().partition(" ")
                if pid_text.isdigit() and "godot" in name.lower():
                    processes[int(pid_text)] = name.strip()
    except (OSError, ValueError):
        return {}
    return processes


def print_summary(results: list[GateResult], log_dir: Path) -> None:
    label_width = max(len("Gate"), *(len(result.label) for result in results))
    print("\nValidation summary")
    print(f"{'Gate':<{label_width}}  Result  Seconds  Detail")
    print(f"{'-' * label_width}  ------  -------  ------")
    for result in results:
        print(
            f"{result.label:<{label_width}}  {result.status:<6}  "
            f"{result.duration_seconds:7.1f}  {result.detail}"
        )
    print(f"Logs: {log_dir}")


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--level", choices=LEVELS, default="full")
    parser.add_argument("--godot", help="Godot 4.7 .NET console executable")
    parser.add_argument("--profile", default=paths.CONTEXT.profile_id,
                        help="game profile id (common tool-context option)")
    parser.add_argument("--game-root", help="AGE install containing SYS4INI.BIN")
    parser.add_argument("--extracted-root", help="optional extracted DATA1..DATA5 root")
    parser.add_argument("--verbose", action="store_true", help="stream successful gate output")
    parser.add_argument("--fail-fast", action="store_true", help="stop after the first failed gate")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    arguments = parse_args(argv)
    context = paths.CONTEXT
    prerequisite_errors = validate_prerequisites(arguments.level, context)
    phases = selected_phases(arguments.level)
    godot = game_root = None
    if "runtime" in phases or "workspace" in phases:
        try:
            if "runtime" in phases:
                godot = resolve_godot(arguments.godot)
            game_root = resolve_game_root(arguments.game_root)
        except ValueError as error:
            prerequisite_errors.append(str(error))
    if prerequisite_errors:
        print("Validation prerequisites failed:", file=sys.stderr)
        for error in prerequisite_errors:
            print(f"  - {error}", file=sys.stderr)
        return 2

    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    log_dir = context.game_build / "validation" / f"validate-{stamp}"
    log_dir.mkdir(parents=True, exist_ok=True)
    initial_godot = snapshot_godot_processes()
    results: list[GateResult] = []
    runtime_state_root = log_dir / "godot-user"
    runtime_state_root.mkdir(parents=True, exist_ok=True)
    for gate in build_gate_plan(
        arguments.level, godot, game_root, runtime_state_root, context
    ):
        result = run_gate(gate, log_dir, arguments.verbose)
        results.append(result)
        if result.status == "FAIL" and arguments.fail_fast:
            break

    final_godot = snapshot_godot_processes()
    leaked = final_godot.keys() - initial_godot.keys()
    if leaked:
        time.sleep(0.5)
        final_godot = snapshot_godot_processes()
        leaked = final_godot.keys() - initial_godot.keys()
    leak_detail = ", ".join(f"{final_godot[pid]}({pid})" for pid in sorted(leaked))
    results.append(GateResult(
        "Godot child-process audit",
        "FAIL" if leaked else "PASS",
        0.0,
        leak_detail or "no new Godot processes",
    ))
    print_summary(results, log_dir)
    return 1 if any(result.status == "FAIL" for result in results) else 0


if __name__ == "__main__":
    raise SystemExit(main())
