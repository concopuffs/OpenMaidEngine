#!/usr/bin/env python3
"""Drive native AGE from the shell: synthetic input, screenshots, VM position, sandboxed saves.

``start`` spawns AGE suspended under Frida, installs the agent (``tools/native-drive/agent.js``), and only then
resumes it, so every hook is in place before AGE's first instruction. A detached daemon keeps the Frida session;
AGE runs in a kill-on-close job owned by that daemon, so it never outlives the hooks. ``do`` and ``run`` send
steps to the daemon, and ``stop`` closes AGE the way its own quit does.

    py -3.11 -X utf8 tools/native_drive.py --profile himegari start
    py -3.11 -X utf8 tools/native_drive.py do "click title.start" settle "shot title"
    py -3.11 -X utf8 tools/native_drive.py run tools/native-drive/scenarios/himegari-new-game.steps
    py -3.11 -X utf8 tools/native_drive.py status
    py -3.11 -X utf8 tools/native_drive.py stop

Inside the driven AGE only:
- GetAsyncKeyState, GetCursorPos and SetCursorPos use the driver's keys and cursor. The desktop's keyboard,
  mouse and joystick no longer reach the game, and AGE cannot move the desktop cursor.
- MessageBoxA follows the profile's dialog policy (the registration box on copied installs is answered Yes);
  other boxes are shown and reported, and ``answer BUTTON`` presses one.
- SHGetFolderPathA (save root, SYS4REG.INI, F1 screenshots, debug report) points into a sandbox below
  ``build/games/<profile>/native-drive/sandbox/``. ``--live-saves`` uses the real folders after a backup.

The scratch install from ``native_scratch.py`` is the default executable. Step syntax: ``native_drive.py steps``
or docs/tools-reference.md ("Native AGE driver").
"""
from __future__ import annotations

import argparse
import json
import os
import re
import secrets
import shlex
import shutil
import subprocess
import sys
import threading
import time
import tomllib
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any, Callable, Iterable

sys.path.insert(0, str(Path(__file__).resolve().parent))
import paths  # noqa: E402

HERE = Path(__file__).resolve().parent
DATA = HERE / "native-drive"
AGENT = DATA / "agent.js"
ENGINE_CTX_TOML = paths.VM_MAP / "engine-ctx.toml"


class DriveError(Exception):
    """A step or command that cannot run; the message is meant for the user."""


# --------------------------------------------------------------------------------------------- layout

def drive_root(context: paths.ToolContext = paths.CONTEXT) -> Path:
    return context.game_build / "native-drive"


def session_file(context: paths.ToolContext = paths.CONTEXT) -> Path:
    return drive_root(context) / "session.json"


def scratch_exe(context: paths.ToolContext = paths.CONTEXT) -> Path:
    return context.game_build / "native-scratch" / "AGE.EXE"


# Folders AGE asks SHGetFolderPathA for (CSIDL & 0xff) and where the sandbox puts them. Himegari asks for
# LOCAL_APPDATA (save root, SYS4REG.INI) and DESKTOPDIRECTORY (F1 screenshots, debug report); the rest are cheap
# insurance for other profiles.
SANDBOX_FOLDERS = {
    0x1C: "LocalAppData",
    0x1A: "AppData",
    0x10: "Desktop",
    0x05: "Documents",
    0x23: "ProgramData",
}


def sandbox_folders(root: Path) -> dict[int, Path]:
    return {csidl: root / name for csidl, name in SANDBOX_FOLDERS.items()}


def live_profile_root(settings: Iterable[tuple[str, str]], local_app_data: Path, game_root: Path) -> Path:
    """The folder native AGE keeps SYS4REG.INI and SAVE\\ in (save_root_resolve@0x40b880 and its siblings).

    USEAPPDATAFOLDER=1 puts REGFILEPATH under CSIDL_LOCAL_APPDATA; otherwise it is used as written (relative paths
    resolve against the game directory). SAVEPATH must lie beneath REGFILEPATH, as the port also requires."""
    values = {key.upper(): value for key, value in settings}
    if "REGFILEPATH" not in values or "SAVEPATH" not in values:
        raise DriveError("SYS4INI has no REGFILEPATH/SAVEPATH settings")
    base = local_app_data if values.get("USEAPPDATAFOLDER", "0").strip() == "1" else game_root
    regfile = base / Path(values["REGFILEPATH"].replace("\\", "/"))
    save = base / Path(values["SAVEPATH"].replace("\\", "/"))
    if regfile != save and regfile not in save.parents:
        raise DriveError(f"SAVEPATH {save} is not beneath REGFILEPATH {regfile}")
    return regfile


def local_app_data() -> Path:
    return Path(os.environ.get("LOCALAPPDATA") or Path.home() / "AppData" / "Local")


def live_root(context: paths.ToolContext = paths.CONTEXT) -> Path:
    import parse_sys4ini
    return live_profile_root(parse_sys4ini.read_settings(context.sys4ini), local_app_data(), context.game_root)


def snapshot_tree(root: Path) -> dict[str, list[int]]:
    """Relative path -> [size, mtime_ns] for every file below root (empty when root is absent)."""
    if not root.is_dir():
        return {}
    return {
        path.relative_to(root).as_posix(): [path.stat().st_size, path.stat().st_mtime_ns]
        for path in sorted(root.rglob("*")) if path.is_file()
    }


def diff_snapshots(before: dict[str, list[int]], after: dict[str, list[int]]) -> dict[str, list[str]]:
    return {
        "added": sorted(set(after) - set(before)),
        "removed": sorted(set(before) - set(after)),
        "changed": sorted(name for name in set(before) & set(after) if before[name] != after[name]),
    }


def backup_tree(source: Path, backups: Path, stamp: str | None = None) -> tuple[Path, bool]:
    """Copy source into backups/<stamp>/ unless the newest backup already matches it. Returns (path, created)."""
    current = snapshot_tree(source)
    existing = sorted(path for path in backups.glob("*") if (path / "manifest.json").is_file()) if backups.is_dir() else []
    if existing:
        newest = existing[-1]
        recorded = json.loads((newest / "manifest.json").read_text(encoding="utf-8"))
        if recorded.get("files") == current:
            return newest, False
    target = backups / (stamp or datetime.now().strftime("%Y%m%d-%H%M%S"))
    if target.exists():
        raise DriveError(f"backup {target} already exists")
    shutil.copytree(source, target / "files")
    (target / "manifest.json").write_text(
        json.dumps({"source": str(source), "files": current}, ensure_ascii=False, indent=1), encoding="utf-8")
    return target, True


def backup_contents(backup: Path) -> Path:
    """The profile-root copy inside a backup: <backup>/files for ours, the folder itself for a plain copy."""
    inner = backup / "files"
    return inner if (backup / "manifest.json").is_file() and inner.is_dir() else backup


def update_ini(text: str, values: dict[str, str]) -> str:
    """Set section.key=value pairs in INI text, keeping every other line, the key order and the newline style.
    Missing keys go to the end of their section; missing sections are appended."""
    newline = "\r\n" if "\r\n" in text or not text else "\n"
    lines = text.splitlines()
    for dotted, value in values.items():
        section, _, key = dotted.partition(".")
        if not section or not key:
            raise DriveError(f"setting {dotted!r} must be section.key")
        start = next((i for i, line in enumerate(lines) if line.strip().lower() == f"[{section.lower()}]"), None)
        if start is None:
            lines += [f"[{section}]", f"{key}={value}"]
            continue
        end = next((i for i in range(start + 1, len(lines)) if lines[i].lstrip().startswith("[")), len(lines))
        for i in range(start + 1, end):
            name, equals, _ = lines[i].partition("=")
            if equals and name.strip().lower() == key.lower():
                lines[i] = f"{name}={value}"
                break
        else:
            lines.insert(end, f"{key}={value}")
    return newline.join(lines) + newline


# --------------------------------------------------------------------------------------------- profile data

DIALOG_ANSWERS = {"ok": 1, "cancel": 2, "abort": 3, "retry": 4, "ignore": 5, "yes": 6, "no": 7}


@dataclass(frozen=True)
class DialogPolicy:
    match: str
    answer: str
    reason: str = ""

    @property
    def code(self) -> int:
        return DIALOG_ANSWERS[self.answer]


@dataclass(frozen=True)
class Profile:
    points: dict[str, tuple[int, int]] = field(default_factory=dict)
    dialogs: tuple[DialogPolicy, ...] = ()
    engine: dict[str, int] | None = None
    advance_point: str = "adv.next"
    settings_file: str = ""
    settings: dict[str, str] = field(default_factory=dict)
    quiet: dict[str, str] = field(default_factory=dict)


ENGINE_FIELDS = ("cur_ctx_index", "frame_codebase", "frame_pc", "frame_script_resource_id")


def engine_ctx_offsets(toml_text: str) -> dict[str, int]:
    """Offsets of the frame fields in vm-map/engine-ctx.toml (the one home of EngineCtx offsets)."""
    fields = {entry["name"]: entry["offset"] for entry in tomllib.loads(toml_text).get("field", [])}
    missing = [name for name in ENGINE_FIELDS if name not in fields]
    if missing:
        raise DriveError(f"engine-ctx.toml lacks {', '.join(missing)}")
    return {name: fields[name] for name in ENGINE_FIELDS}


def parse_profile(text: str, engine_ctx_text: str | None) -> Profile:
    data = tomllib.loads(text)
    points = {}
    for name, value in data.get("points", {}).items():
        if not (isinstance(value, list) and len(value) == 2 and all(isinstance(v, int) for v in value)):
            raise DriveError(f"point {name!r} must be [x, y]")
        points[name] = (value[0], value[1])
    dialogs = []
    for entry in data.get("dialog", []):
        answer = str(entry.get("answer", "")).lower()
        if answer not in DIALOG_ANSWERS or not entry.get("match"):
            raise DriveError(f"dialog policy needs match and one of {', '.join(DIALOG_ANSWERS)}: {entry}")
        dialogs.append(DialogPolicy(entry["match"], answer, entry.get("reason", "")))
    engine = None
    if "engine" in data:
        if engine_ctx_text is None:
            raise DriveError("the profile reads engine state but engine-ctx.toml is missing")
        engine = {key: int(data["engine"][key]) for key in ("ctx_global", "frame_stride", "frame_count")}
        engine.update(engine_ctx_offsets(engine_ctx_text))
    advance = data.get("advance", {}).get("point", "adv.next")
    sandbox = data.get("sandbox", {})
    settings = {key: str(value) for key, value in sandbox.get("settings", {}).items()}
    quiet = {key: str(value) for key, value in sandbox.get("quiet", {}).items()}
    if (settings or quiet) and not sandbox.get("settings_file"):
        raise DriveError("[sandbox] settings need settings_file")
    return Profile(points, tuple(dialogs), engine, advance, sandbox.get("settings_file", ""), settings, quiet)


def load_profile(context: paths.ToolContext = paths.CONTEXT) -> Profile:
    path = DATA / f"{context.profile_id}.toml"
    if not path.is_file():
        return Profile()
    engine_ctx = ENGINE_CTX_TOML.read_text(encoding="utf-8") if ENGINE_CTX_TOML.is_file() else None
    return parse_profile(path.read_text(encoding="utf-8"), engine_ctx)


def agent_config(profile: Profile, folders: dict[int, Path] | None) -> dict[str, Any]:
    if folders is not None:
        for path in folders.values():
            text = str(path)
            if not text.isascii() or len(text) > 180:
                raise DriveError(f"sandbox path must be ASCII and short enough for AGE's buffers: {text}")
    return {
        "dialogs": [{"match": list(policy.match.encode("cp932")), "answer": policy.code} for policy in profile.dialogs],
        "folders": None if folders is None else {str(csidl): str(path) for csidl, path in folders.items()},
        "engine": profile.engine,
    }


# --------------------------------------------------------------------------------------------- steps

VK_NAMES = {
    "enter": 0x0D, "return": 0x0D, "space": 0x20, "tab": 0x09, "backspace": 0x08, "shift": 0x10,
    "ctrl": 0x11, "control": 0x11, "alt": 0x12, "pause": 0x13, "pgup": 0x21, "pageup": 0x21, "pgdn": 0x22,
    "pagedown": 0x22, "end": 0x23, "home": 0x24, "left": 0x25, "up": 0x26, "right": 0x27, "down": 0x28,
    "insert": 0x2D, "delete": 0x2E, "lshift": 0xA0, "rshift": 0xA1, "lctrl": 0xA2, "rctrl": 0xA3,
}
VK_NAMES.update({f"f{n}": 0x6F + n for n in range(1, 13)})
REFUSED_KEYS = {
    0x1B: "ESC posts WM_CLOSE (age_main_window_proc); use the close step",
    0x70: "F1 saves a screenshot BMP (engine_save_screenshot_bmp); use the shot step",
}


def virtual_key(name: str) -> int:
    lowered = name.lower()
    if lowered in ("esc", "escape"):
        vk = 0x1B
    elif lowered in VK_NAMES:
        vk = VK_NAMES[lowered]
    elif len(name) == 1 and name.isalnum():
        vk = ord(name.upper())
    else:
        try:
            vk = int(name, 0)
        except ValueError:
            raise DriveError(f"unknown key {name!r}") from None
    if not 0 < vk < 0xFF:
        raise DriveError(f"virtual key {vk:#x} out of range")
    if vk in REFUSED_KEYS:
        raise DriveError(f"key {name}: {REFUSED_KEYS[vk]}")
    return vk


@dataclass(frozen=True)
class StepSpec:
    min_args: int
    max_args: int
    options: frozenset[str]
    usage: str


def _spec(min_args: int, max_args: int, options: str, usage: str) -> StepSpec:
    return StepSpec(min_args, max_args, frozenset(options.split()), usage)


STEPS = {
    "move": _spec(1, 2, "", "move X Y | move POINT          park the synthetic cursor (hover)"),
    "click": _spec(1, 2, "hold", "click X Y | click POINT [hold=MS]   left click (polled button + message latch)"),
    "rclick": _spec(1, 2, "hold", "rclick X Y | rclick POINT [hold=MS] right click (opens the ADV menu)"),
    "key": _spec(1, 1, "hold", "key NAME|0xVK [hold=MS]       press a key; ESC and F1 are refused"),
    "wheel": _spec(1, 2, "", "wheel up|down [N]             turn the wheel N notches at the cursor"),
    "wait": _spec(1, 1, "", "wait SECONDS"),
    "settle": _spec(0, 0, "timeout quiet threshold",
                    "settle [timeout=S] [quiet=S] [threshold=F]  wait until the screen stops changing"),
    "advance": _spec(0, 1, "timeout", "advance [N]                   N x (click the advance point, settle)"),
    "shot": _spec(0, 1, "", "shot [NAME]                   save the client area to shots/NAME.png"),
    "state": _spec(0, 0, "", "state                         the VM frame stack (script@offset per frame)"),
    "expect": _spec(0, 0, "at top script", "expect at=S@OFF | top=S@OFF | script=S   fail unless the VM matches"),
    "wait-state": _spec(0, 0, "at top script timeout click every",
                        "wait-state at=S@OFF|top=S@OFF|script=S [timeout=S] [click=POINT every=S]   poll until "
                        "the VM matches, clicking POINT every S seconds (default 2) meanwhile"),
    "point": _spec(3, 3, "", "point NAME X Y                define a named point for this session"),
    "dialogs": _spec(0, 0, "", "dialogs                       list AGE's open message boxes"),
    "answer": _spec(1, 1, "", "answer BUTTON                 press a button of the open message box"),
    "close": _spec(0, 0, "timeout", "close [timeout=S]             quit AGE (WM_CLOSE wParam=1) and end the session"),
}
NUMERIC_OPTIONS = {"hold", "timeout", "quiet", "threshold", "every"}
LOCATION = re.compile(r"^([^@\s]+?)(?:\.bin)?@(0x[0-9a-f]+|\d+)$", re.IGNORECASE)


@dataclass(frozen=True)
class Step:
    name: str
    args: tuple[str, ...]
    options: dict[str, str]
    text: str
    line: int = 0


def parse_location(text: str) -> tuple[str, int]:
    match = LOCATION.match(text)
    if not match:
        raise DriveError(f"expected SCRIPT@OFFSET (e.g. SC0000@0x2a91), got {text!r}")
    return match.group(1).upper(), int(match.group(2), 0)


def parse_step(text: str, line: int = 0) -> Step:
    try:
        words = shlex.split(text, posix=True)
    except ValueError as error:
        raise DriveError(f"line {line}: {error}") from None
    if not words:
        raise DriveError(f"line {line}: empty step")
    name = words[0].lower()
    spec = STEPS.get(name)
    where = f"line {line}: " if line else ""
    if spec is None:
        raise DriveError(f"{where}unknown step {words[0]!r} (see `native_drive.py steps`)")
    args: list[str] = []
    options: dict[str, str] = {}
    for word in words[1:]:
        key, equals, value = word.partition("=")
        if equals and re.fullmatch(r"[a-z][a-z-]*", key):
            if key not in spec.options:
                raise DriveError(f"{where}{name} has no option {key!r}: {spec.usage}")
            options[key] = value
        else:
            args.append(word)
    if not spec.min_args <= len(args) <= spec.max_args:
        raise DriveError(f"{where}{name} takes {spec.min_args}..{spec.max_args} arguments: {spec.usage}")
    step = Step(name, tuple(args), options, text.strip(), line)
    try:
        _validate(step)
    except DriveError as error:
        raise DriveError(f"{where}{error}") from None
    return step


def _validate(step: Step) -> None:
    for key in NUMERIC_OPTIONS & set(step.options):
        try:
            float(step.options[key])
        except ValueError:
            raise DriveError(f"{step.name}: {key}={step.options[key]!r} is not a number") from None
    if step.name in ("move", "click", "rclick") and len(step.args) == 2:
        _coordinates(step.args)
    elif step.name == "key":
        virtual_key(step.args[0])
    elif step.name == "wheel":
        if step.args[0].lower() not in ("up", "down"):
            raise DriveError("wheel takes up or down")
        if len(step.args) > 1 and not step.args[1].isdigit():
            raise DriveError("wheel notches must be a number")
    elif step.name == "wait":
        float(step.args[0])
    elif step.name == "advance" and step.args and not step.args[0].isdigit():
        raise DriveError("advance takes a page count")
    elif step.name == "shot" and step.args and not re.fullmatch(r"[\w.-]+", step.args[0]):
        raise DriveError("shot names are plain file names (letters, digits, . _ -)")
    elif step.name == "point":
        _coordinates(step.args[1:])
    elif step.name in ("expect", "wait-state"):
        matchers = [key for key in ("at", "top", "script") if key in step.options]
        if len(matchers) != 1:
            raise DriveError(f"{step.name} takes exactly one of at=, top=, script=")
        if matchers[0] != "script":
            parse_location(step.options[matchers[0]])


def _coordinates(words: Iterable[str]) -> tuple[int, int]:
    try:
        x, y = (int(word, 0) for word in words)
    except ValueError:
        raise DriveError(f"expected integer coordinates, got {' '.join(words)}") from None
    return x, y


def parse_steps(lines: Iterable[str]) -> list[Step]:
    steps = []
    for number, line in enumerate(lines, 1):
        text = line.split("#", 1)[0].strip()
        if text:
            steps.append(parse_step(text, number))
    return steps


def resolve_point(args: tuple[str, ...], points: dict[str, tuple[int, int]]) -> tuple[int, int]:
    if len(args) == 2:
        return _coordinates(args)
    if args[0] not in points:
        known = ", ".join(sorted(points)) or "none"
        raise DriveError(f"unknown point {args[0]!r} (known: {known})")
    return points[args[0]]


# --------------------------------------------------------------------------------------------- VM state

def describe_frames(raw: dict[str, Any] | None, names: dict[str, str]) -> dict[str, Any]:
    """Turn the agent's raw frame reads into script@offset locations. offset = (pc - codebase) >> 2, the
    instruction offset in dwords that the port's page locator and the disassembly use."""
    if raw is None:
        raise DriveError("this profile has no [engine] section, so the VM position is unknown")
    if "error" in raw:
        raise DriveError(f"reading the VM position failed: {raw['error']}")
    frames = []
    for frame in raw["frames"]:
        resid, codebase, pc = frame["resid"], frame["codebase"], frame["pc"]
        name = names.get(str(resid))
        script = f"#{resid:#x}" if name is None else name.upper().removesuffix(".BIN")
        offset = (pc - codebase) >> 2 if codebase and pc >= codebase else None
        frames.append({"index": frame["index"], "script": script, "offset": offset, "resid": resid,
                       "location": f"{script}@{offset:#x}" if offset is not None else f"{script}@?"})
    return {"ctx": raw["ctx"], "current": raw["current"], "frames": frames,
            "location": frames[-1]["location"] if frames else None}


def state_matches(state: dict[str, Any], options: dict[str, str]) -> bool:
    frames = state["frames"]
    if "script" in options:
        return any(frame["script"] == options["script"].upper().removesuffix(".BIN") for frame in frames)
    key = "top" if "top" in options else "at"
    script, offset = parse_location(options[key])
    candidates = frames[-1:] if key == "top" else frames
    return any(frame["script"] == script and frame["offset"] == offset for frame in candidates)


# --------------------------------------------------------------------------------------------- screen settle

def changed_fraction(before: Any, after: Any, tolerance: int = 24) -> float:
    """Share of pixels whose largest channel difference exceeds tolerance (PIL images)."""
    from PIL import ImageChops
    if before.size != after.size:
        return 1.0
    difference = ImageChops.difference(before, after).convert("L")
    changed = difference.point(lambda value: 255 if value > tolerance else 0).histogram()[255]
    return changed / (before.size[0] * before.size[1])


def settle(capture: Callable[[], Any], difference: Callable[[Any, Any], float] = changed_fraction, *,
           threshold: float = 0.004, quiet: float = 0.7, timeout: float = 10.0, interval: float = 0.1,
           clock: Callable[[], float] = time.monotonic, sleep: Callable[[float], None] = time.sleep
           ) -> tuple[bool, float, Any]:
    """Wait until the screen has stayed within threshold of one reference frame for quiet seconds.

    Comparing against the reference (not the previous frame) makes slow changes such as typed-out text add up and
    reset the wait, while a small looping animation (the ADV wait indicator) stays below the threshold."""
    start = clock()
    reference = last = capture()
    calm_since = start
    while True:
        now = clock()
        if now - calm_since >= quiet:
            return True, now - start, last
        if now - start >= timeout:
            return False, now - start, last
        sleep(interval)
        last = capture()
        if difference(reference, last) > threshold:
            reference = last
            calm_since = clock()


# --------------------------------------------------------------------------------------------- Win32

class Win32:
    """The handful of user32/gdi32/kernel32 calls the driver needs (Windows only; created lazily)."""

    def __init__(self) -> None:
        import ctypes
        from ctypes import wintypes
        self.ctypes, self.wintypes = ctypes, wintypes
        self.user32 = user32 = ctypes.WinDLL("user32", use_last_error=True)
        self.gdi32 = gdi32 = ctypes.WinDLL("gdi32", use_last_error=True)
        self.kernel32 = kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        user32.SetProcessDPIAware()
        self.WNDENUMPROC = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
        user32.EnumWindows.argtypes = [self.WNDENUMPROC, wintypes.LPARAM]
        user32.EnumChildWindows.argtypes = [wintypes.HWND, self.WNDENUMPROC, wintypes.LPARAM]
        user32.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
        user32.PrintWindow.argtypes = [wintypes.HWND, wintypes.HDC, wintypes.UINT]
        user32.GetDC.argtypes = [wintypes.HWND]
        user32.GetDC.restype = wintypes.HDC
        user32.ReleaseDC.argtypes = [wintypes.HWND, wintypes.HDC]
        user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
        user32.GetClientRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
        user32.ClientToScreen.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.POINT)]
        user32.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
        user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
        user32.GetWindowTextLengthW.argtypes = [wintypes.HWND]
        user32.IsWindowVisible.argtypes = [wintypes.HWND]
        user32.MapVirtualKeyW.argtypes = [wintypes.UINT, wintypes.UINT]
        gdi32.CreateCompatibleDC.argtypes = [wintypes.HDC]
        gdi32.CreateCompatibleDC.restype = wintypes.HDC
        gdi32.CreateCompatibleBitmap.argtypes = [wintypes.HDC, ctypes.c_int, ctypes.c_int]
        gdi32.CreateCompatibleBitmap.restype = wintypes.HBITMAP
        gdi32.SelectObject.argtypes = [wintypes.HDC, wintypes.HGDIOBJ]
        gdi32.SelectObject.restype = wintypes.HGDIOBJ
        gdi32.BitBlt.argtypes = [wintypes.HDC, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int,
                                 wintypes.HDC, ctypes.c_int, ctypes.c_int, wintypes.DWORD]
        gdi32.GetDIBits.argtypes = [wintypes.HDC, wintypes.HBITMAP, wintypes.UINT, wintypes.UINT,
                                    ctypes.c_void_p, ctypes.c_void_p, wintypes.UINT]
        gdi32.DeleteObject.argtypes = [wintypes.HGDIOBJ]
        gdi32.DeleteDC.argtypes = [wintypes.HDC]
        kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        kernel32.OpenProcess.restype = wintypes.HANDLE
        kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
        kernel32.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
        kernel32.WaitForSingleObject.restype = wintypes.DWORD
        kernel32.GetExitCodeProcess.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
        kernel32.TerminateProcess.argtypes = [wintypes.HANDLE, wintypes.UINT]
        kernel32.CreateJobObjectW.argtypes = [ctypes.c_void_p, wintypes.LPCWSTR]
        kernel32.CreateJobObjectW.restype = wintypes.HANDLE
        kernel32.SetInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD]
        kernel32.AssignProcessToJobObject.argtypes = [wintypes.HANDLE, wintypes.HANDLE]

    # windows ---------------------------------------------------------------------------------------
    def _pid(self, hwnd: int) -> int:
        pid = self.wintypes.DWORD()
        self.user32.GetWindowThreadProcessId(hwnd, self.ctypes.byref(pid))
        return pid.value

    def _class(self, hwnd: int) -> str:
        buffer = self.ctypes.create_unicode_buffer(256)
        self.user32.GetClassNameW(hwnd, buffer, 256)
        return buffer.value

    def text(self, hwnd: int) -> str:
        length = self.user32.GetWindowTextLengthW(hwnd)
        buffer = self.ctypes.create_unicode_buffer(length + 1)
        self.user32.GetWindowTextW(hwnd, buffer, length + 1)
        return buffer.value

    def top_windows(self, pid: int) -> list[int]:
        found: list[int] = []

        def visit(hwnd, _):
            if hwnd and self.user32.IsWindowVisible(hwnd) and self._pid(hwnd) == pid:
                found.append(hwnd)
            return True

        self.user32.EnumWindows(self.WNDENUMPROC(visit), 0)
        return found

    def client_size(self, hwnd: int) -> tuple[int, int]:
        rect = self.wintypes.RECT()
        self.user32.GetClientRect(hwnd, self.ctypes.byref(rect))
        return rect.right, rect.bottom

    def main_window(self, pid: int) -> int | None:
        candidates = [hwnd for hwnd in self.top_windows(pid) if self._class(hwnd) != "#32770"]
        candidates = [hwnd for hwnd in candidates if self.client_size(hwnd)[0] * self.client_size(hwnd)[1] > 0]
        return max(candidates, key=lambda hwnd: self.client_size(hwnd)[0] * self.client_size(hwnd)[1], default=None)

    def dialogs(self, pid: int) -> list[dict[str, Any]]:
        boxes = []
        for hwnd in self.top_windows(pid):
            if self._class(hwnd) != "#32770":
                continue
            children: list[tuple[int, str, str]] = []

            def child(handle, _):
                children.append((handle, self._class(handle), self.text(handle)))
                return True

            self.user32.EnumChildWindows(hwnd, self.WNDENUMPROC(child), 0)
            boxes.append({
                "hwnd": hwnd,
                "title": self.text(hwnd),
                "text": "\n".join(text for _, kind, text in children if kind == "Static" and text),
                "buttons": [(handle, text) for handle, kind, text in children if kind == "Button"],
            })
        return boxes

    def post(self, hwnd: int, message: int, wparam: int, lparam: int) -> None:
        self.user32.PostMessageW(hwnd, message, wparam, lparam)

    def client_to_screen(self, hwnd: int, x: int, y: int) -> tuple[int, int]:
        point = self.wintypes.POINT(x, y)
        self.user32.ClientToScreen(hwnd, self.ctypes.byref(point))
        return point.x, point.y

    def scan_code(self, vk: int) -> int:
        return self.user32.MapVirtualKeyW(vk, 0)

    def capture(self, hwnd: int) -> Any:
        """The client area through PrintWindow(PW_CLIENTONLY | PW_RENDERFULLCONTENT); a window-DC copy when that
        comes back black."""
        image = self._capture(hwnd, from_window_dc=False)
        if image.getextrema() == ((0, 0), (0, 0), (0, 0)):
            image = self._capture(hwnd, from_window_dc=True)
        return image

    def _capture(self, hwnd: int, from_window_dc: bool) -> Any:
        from PIL import Image
        ctypes, wintypes = self.ctypes, self.wintypes
        width, height = self.client_size(hwnd)
        if width <= 0 or height <= 0:
            raise DriveError("the AGE window has no client area (minimized?)")

        class BITMAPINFOHEADER(ctypes.Structure):
            _fields_ = [("biSize", wintypes.DWORD), ("biWidth", ctypes.c_long), ("biHeight", ctypes.c_long),
                        ("biPlanes", wintypes.WORD), ("biBitCount", wintypes.WORD),
                        ("biCompression", wintypes.DWORD), ("biSizeImage", wintypes.DWORD),
                        ("biXPelsPerMeter", ctypes.c_long), ("biYPelsPerMeter", ctypes.c_long),
                        ("biClrUsed", wintypes.DWORD), ("biClrImportant", wintypes.DWORD)]

        source = self.user32.GetDC(hwnd)
        memory = self.gdi32.CreateCompatibleDC(source)
        bitmap = self.gdi32.CreateCompatibleBitmap(source, width, height)
        old = self.gdi32.SelectObject(memory, bitmap)
        try:
            if from_window_dc:
                self.gdi32.BitBlt(memory, 0, 0, width, height, source, 0, 0, 0x00CC0020)   # SRCCOPY
            else:
                self.user32.PrintWindow(hwnd, memory, 3)
            header = BITMAPINFOHEADER(ctypes.sizeof(BITMAPINFOHEADER), width, -height, 1, 32, 0, 0, 0, 0, 0, 0)
            pixels = ctypes.create_string_buffer(width * height * 4)
            self.gdi32.GetDIBits(memory, bitmap, 0, height, pixels, ctypes.byref(header), 0)
        finally:
            self.gdi32.SelectObject(memory, old)
            self.gdi32.DeleteObject(bitmap)
            self.gdi32.DeleteDC(memory)
            self.user32.ReleaseDC(hwnd, source)
        return Image.frombuffer("RGBX", (width, height), pixels, "raw", "BGRX", 0, 1).convert("RGB")

    # processes -------------------------------------------------------------------------------------
    def kill_on_close_job(self, pid: int) -> int:
        """Put pid in a job that the OS kills when this process's handle closes (any exit, including a crash)."""
        ctypes, wintypes = self.ctypes, self.wintypes

        class BASIC(ctypes.Structure):
            _fields_ = [("PerProcessUserTimeLimit", ctypes.c_int64), ("PerJobUserTimeLimit", ctypes.c_int64),
                        ("LimitFlags", wintypes.DWORD), ("MinimumWorkingSetSize", ctypes.c_size_t),
                        ("MaximumWorkingSetSize", ctypes.c_size_t), ("ActiveProcessLimit", wintypes.DWORD),
                        ("Affinity", ctypes.c_size_t), ("PriorityClass", wintypes.DWORD),
                        ("SchedulingClass", wintypes.DWORD)]

        class EXTENDED(ctypes.Structure):
            _fields_ = [("BasicLimitInformation", BASIC), ("IoInfo", ctypes.c_uint64 * 6),
                        ("ProcessMemoryLimit", ctypes.c_size_t), ("JobMemoryLimit", ctypes.c_size_t),
                        ("PeakProcessMemoryUsed", ctypes.c_size_t), ("PeakJobMemoryUsed", ctypes.c_size_t)]

        job = self.kernel32.CreateJobObjectW(None, None)
        if not job:
            raise DriveError(f"CreateJobObject failed ({ctypes.get_last_error()})")
        info = EXTENDED()
        info.BasicLimitInformation.LimitFlags = 0x2000          # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if not self.kernel32.SetInformationJobObject(job, 9, ctypes.byref(info), ctypes.sizeof(info)):
            raise DriveError(f"SetInformationJobObject failed ({ctypes.get_last_error()})")
        process = self.kernel32.OpenProcess(0x0101, False, pid)  # PROCESS_SET_QUOTA | PROCESS_TERMINATE
        if not process:
            raise DriveError(f"OpenProcess({pid}) failed ({ctypes.get_last_error()})")
        try:
            if not self.kernel32.AssignProcessToJobObject(job, process):
                raise DriveError(f"AssignProcessToJobObject failed ({ctypes.get_last_error()})")
        finally:
            self.kernel32.CloseHandle(process)
        return job

    def wait_exit(self, pid: int, seconds: float | None) -> int | None:
        """Exit code once pid has exited, or None on timeout. A process that cannot be opened has exited."""
        process = self.kernel32.OpenProcess(0x00101000, False, pid)   # SYNCHRONIZE | QUERY_LIMITED_INFORMATION
        if not process:
            return -1
        try:
            milliseconds = 0xFFFFFFFF if seconds is None else int(seconds * 1000)
            if self.kernel32.WaitForSingleObject(process, milliseconds) != 0:
                return None
            code = self.wintypes.DWORD()
            self.kernel32.GetExitCodeProcess(process, self.ctypes.byref(code))
            return code.value
        finally:
            self.kernel32.CloseHandle(process)

    def alive(self, pid: int) -> bool:
        return self.wait_exit(pid, 0) is None

    def terminate(self, pid: int) -> None:
        process = self.kernel32.OpenProcess(0x0001, False, pid)
        if process:
            self.kernel32.TerminateProcess(process, 1)
            self.kernel32.CloseHandle(process)


_WIN32: Win32 | None = None


def win32() -> Win32:
    global _WIN32
    if _WIN32 is None:
        if sys.platform != "win32":
            raise DriveError("driving native AGE needs Windows")
        _WIN32 = Win32()
    return _WIN32


def lparam(x: int, y: int) -> int:
    return ((y & 0xFFFF) << 16) | (x & 0xFFFF)


def wheel_wparam(notches: int) -> int:
    return ((notches * 120) & 0xFFFF) << 16


# --------------------------------------------------------------------------------------------- daemon

WM_MOUSEMOVE, WM_LBUTTONDOWN, WM_LBUTTONUP, WM_RBUTTONDOWN, WM_RBUTTONUP = 0x200, 0x201, 0x202, 0x204, 0x205
WM_KEYDOWN, WM_KEYUP, WM_MOUSEWHEEL, WM_CLOSE, BM_CLICK = 0x100, 0x101, 0x20A, 0x10, 0xF5


def now_text() -> str:
    return datetime.now().isoformat(timespec="milliseconds")


class Daemon:
    """Owns AGE (spawned suspended, hooked, resumed), the Frida session, and the command pipe."""

    def __init__(self, arguments: argparse.Namespace) -> None:
        self.arguments = arguments
        self.context = paths.CONTEXT
        self.root = drive_root()
        self.shots = self.root / "shots"
        self.events_path = self.root / "events.jsonl"
        self.profile = load_profile()
        self.points = dict(self.profile.points)
        names_path = paths.GAME_BUILD / "callscript-names.json"
        self.names = json.loads(names_path.read_text(encoding="utf-8")) if names_path.is_file() else {}
        self.lock = threading.Lock()
        self.pending: list[dict[str, Any]] = []
        self.age_pid = 0
        self.hwnd: int | None = None
        self.api: Any = None
        self.session: Any = None
        self.job: int | None = None
        self.closing = False
        self.exit_code: int | None = None
        self.saves: dict[str, Any] = {}
        self.live_before: dict[str, list[int]] | None = None
        self.live_root: Path | None = None
        self.answered: set[int] = set()
        self.cursor: tuple[int, int] = (0, 0)

    # events ----------------------------------------------------------------------------------------
    def event(self, kind: str, **fields: Any) -> None:
        record = {"t": now_text(), "kind": kind, **fields}
        with self.lock:
            self.pending.append(record)
            with self.events_path.open("a", encoding="utf-8") as stream:
                stream.write(json.dumps(record, ensure_ascii=False) + "\n")
        print(f"[{record['t']}] {kind} {json.dumps(fields, ensure_ascii=False)}", flush=True)

    def take_events(self) -> list[dict[str, Any]]:
        with self.lock:
            taken, self.pending = self.pending, []
        return taken

    def on_message(self, message: dict[str, Any], _data: Any) -> None:
        if message.get("type") != "send":
            self.event("agent-error", detail=message.get("description") or message.get("stack") or str(message))
            return
        payload = dict(message["payload"])
        kind = payload.pop("kind", "agent")
        for key in ("text", "caption"):
            if key in payload:
                payload[key] = bytes.fromhex(payload[key]).decode("cp932", errors="replace")
        self.event(kind, **payload)

    # saves -----------------------------------------------------------------------------------------
    def prepare_saves(self) -> dict[int, Path] | None:
        arguments = self.arguments
        live = live_root()
        if arguments.attach is not None or arguments.live_saves:
            self.saves = {"mode": "live", "root": str(live)}
            if live.is_dir():
                backup, created = backup_tree(live, self.root / "backups")
                self.saves["backup"] = str(backup)
                print(f"native_drive: live saves {'backed up to' if created else 'match backup'} {backup}", flush=True)
            return None
        if not live.is_relative_to(local_app_data()):
            raise DriveError(f"the save root {live} is not under %LOCALAPPDATA%; the sandbox cannot redirect it "
                             "(pass --live-saves)")
        name = arguments.sandbox or "fresh"
        sandbox = self.root / "sandbox" / name
        if name == "fresh" and sandbox.exists():
            shutil.rmtree(sandbox)
        created = not sandbox.exists()
        folders = sandbox_folders(sandbox)
        for path in folders.values():
            path.mkdir(parents=True, exist_ok=True)
        profile_root = folders[0x1C] / live.relative_to(local_app_data())
        if created and arguments.seed:
            source = live if arguments.seed == "live" else backup_contents(Path(arguments.seed))
            if not source.is_dir():
                raise DriveError(f"seed {source} is not a folder")
            shutil.copytree(source, profile_root, dirs_exist_ok=True)
        overrides = {**self.profile.settings, **({} if arguments.sound else self.profile.quiet)}
        if overrides:
            ini = profile_root / self.profile.settings_file
            ini.parent.mkdir(parents=True, exist_ok=True)
            text = ini.read_bytes().decode("cp932") if ini.is_file() else ""
            ini.write_bytes(update_ini(text, overrides).encode("cp932"))
            self.event("sandbox-settings", file=str(ini), values=overrides)
        self.saves = {"mode": "sandbox", "root": str(sandbox), "profile": str(profile_root)}
        self.live_root = live
        self.live_before = snapshot_tree(live)
        return folders

    def verify_live_untouched(self) -> None:
        if self.live_before is None:
            return
        changes = diff_snapshots(self.live_before, snapshot_tree(self.live_root))
        if any(changes.values()):
            self.event("live-saves-changed", **changes)
        else:
            self.event("live-saves-unchanged", root=str(self.live_root))

    # AGE -------------------------------------------------------------------------------------------
    def launch(self, folders: dict[int, Path] | None) -> None:
        import frida
        device = frida.get_local_device()
        config = agent_config(self.profile, folders)
        source = AGENT.read_text(encoding="utf-8")
        if self.arguments.attach is not None:
            self.age_pid = self.find_running_age(device) if self.arguments.attach == "auto" else int(self.arguments.attach)
            self.session = device.attach(self.age_pid)
            self.load_agent(source, config)
            self.event("attached", pid=self.age_pid, hooked=self.api.hooked())
            return
        running = [process.pid for process in device.enumerate_processes() if process.name.upper() == "AGE.EXE"]
        if running:
            raise DriveError(f"AGE.EXE is already running (pid {', '.join(map(str, running))}); close it or use --attach")
        exe = Path(self.arguments.exe) if self.arguments.exe else scratch_exe()
        if not exe.is_file():
            raise DriveError(f"no {exe}; build the scratch install with native_scratch.py or pass --exe")
        self.age_pid = device.spawn([str(exe)], cwd=str(exe.parent))
        try:
            self.session = device.attach(self.age_pid)
            self.load_agent(source, config)
            self.job = win32().kill_on_close_job(self.age_pid)
            device.resume(self.age_pid)
        except Exception:
            device.kill(self.age_pid)         # never leave a suspended or unhooked AGE behind
            raise
        self.event("spawned", pid=self.age_pid, exe=str(exe), hooked=self.api.hooked())

    def find_running_age(self, device: Any) -> int:
        running = [process.pid for process in device.enumerate_processes() if process.name.upper() == "AGE.EXE"]
        if len(running) != 1:
            raise DriveError(f"--attach auto needs exactly one running AGE.EXE, found {len(running)}")
        return running[0]

    def load_agent(self, source: str, config: dict[str, Any]) -> None:
        script = self.session.create_script(source)
        script.on("message", self.on_message)
        script.load()
        self.api = script.exports_sync
        self.api.init(config)
        self.session.on("detached", lambda reason, *_: self.event("detached", reason=str(reason)))

    def watch_window(self) -> None:
        """Find the main window once AGE creates it; report dialogs; apply the dialog policy as a fallback."""
        while not self.closing:
            if self.hwnd is None:
                hwnd = win32().main_window(self.age_pid)
                if hwnd:
                    self.hwnd = hwnd
                    self.api.setwindow(hwnd)
                    width, height = win32().client_size(hwnd)
                    self.cursor = (max(width - 8, 0), max(height - 8, 0))    # parked, away from the top strip
                    self.api.setcursor(*self.cursor)
                    self.event("window", hwnd=hex(hwnd), title=win32().text(hwnd), size=[width, height])
            for box in win32().dialogs(self.age_pid):
                if box["hwnd"] in self.answered:
                    continue
                policy = next((p for p in self.profile.dialogs if box["text"].startswith(p.match)), None)
                if policy is not None:
                    button = next((h for h, text in box["buttons"] if text.replace("&", "").lower() == policy.answer), None)
                    if button is not None:
                        win32().post(button, BM_CLICK, 0, 0)
                        self.answered.add(box["hwnd"])
                        self.event("dialog", text=box["text"], answered=policy.answer, via="button")
                        continue
                if box["hwnd"] not in self.answered:
                    self.answered.add(box["hwnd"])
                    self.event("dialog-open", text=box["text"], title=box["title"],
                               buttons=[text for _, text in box["buttons"]])
            time.sleep(0.25)

    def watch_exit(self, listener_address: str, authkey: bytes) -> None:
        self.exit_code = win32().wait_exit(self.age_pid, None)
        self.event("age-exited", code=self.exit_code)
        self.closing = True
        from multiprocessing.connection import Client
        try:
            Client(listener_address, family="AF_PIPE", authkey=authkey).close()   # wake the accept loop
        except OSError:
            pass

    # commands --------------------------------------------------------------------------------------
    def require_window(self) -> int:
        if self.hwnd is None:
            raise DriveError("AGE has no main window yet (a dialog may be open: try `dialogs`)")
        return self.hwnd

    def move(self, x: int, y: int) -> None:
        hwnd = self.require_window()
        self.api.setcursor(x, y)
        self.cursor = (x, y)
        win32().post(hwnd, WM_MOUSEMOVE, 0, lparam(x, y))

    def click(self, x: int, y: int, right: bool, hold: float) -> None:
        hwnd = self.require_window()
        self.move(x, y)
        time.sleep(0.12)
        vk, down, up = (2, WM_RBUTTONDOWN, WM_RBUTTONUP) if right else (1, WM_LBUTTONDOWN, WM_LBUTTONUP)
        # Menus poll the button (GetAsyncKeyState); the movie skip and CLICKONUP consume the message latch.
        self.api.setkey(vk, True)
        win32().post(hwnd, down, vk, lparam(x, y))
        time.sleep(hold)
        self.api.setkey(vk, False)
        win32().post(hwnd, up, 0, lparam(x, y))
        time.sleep(0.05)

    def key(self, vk: int, hold: float) -> None:
        hwnd = self.require_window()
        scan = win32().scan_code(vk)
        self.api.setkey(vk, True)
        win32().post(hwnd, WM_KEYDOWN, vk, 1 | (scan << 16))
        time.sleep(hold)
        self.api.setkey(vk, False)
        win32().post(hwnd, WM_KEYUP, vk, 1 | (scan << 16) | 0xC0000000)
        time.sleep(0.05)

    def capture(self) -> Any:
        return win32().capture(self.require_window())

    def state(self) -> dict[str, Any]:
        return describe_frames(self.api.state(), self.names)

    def try_state(self) -> str | None:
        try:
            return self.state()["location"]
        except Exception:
            return None

    def settle(self, options: dict[str, str]) -> dict[str, Any]:
        settled, elapsed, _ = settle(self.capture, threshold=float(options.get("threshold", 0.004)),
                                     quiet=float(options.get("quiet", 0.7)), timeout=float(options.get("timeout", 10)))
        return {"settled": settled, "seconds": round(elapsed, 2)}

    def execute(self, step: Step) -> dict[str, Any]:
        options = step.options
        hold = float(options.get("hold", 150)) / 1000
        if step.name == "move":
            self.move(*resolve_point(step.args, self.points))
        elif step.name in ("click", "rclick"):
            x, y = resolve_point(step.args, self.points)
            self.click(x, y, step.name == "rclick", hold)
        elif step.name == "key":
            self.key(virtual_key(step.args[0]), hold)
        elif step.name == "wheel":
            notches = int(step.args[1]) if len(step.args) > 1 else 1
            sign = 1 if step.args[0].lower() == "up" else -1
            hwnd = self.require_window()
            x, y = win32().client_to_screen(hwnd, *self.cursor)
            for _ in range(notches):
                win32().post(hwnd, WM_MOUSEWHEEL, wheel_wparam(sign), lparam(x, y))
                time.sleep(0.08)
        elif step.name == "wait":
            time.sleep(float(step.args[0]))
        elif step.name == "settle":
            return self.settle(options)
        elif step.name == "advance":
            pages = int(step.args[0]) if step.args else 1
            point = resolve_point((self.profile.advance_point,), self.points)
            results = []
            for _ in range(pages):
                self.click(*point, right=False, hold=0.15)
                results.append(self.settle({"timeout": options.get("timeout", "10")}))
            return {"pages": pages, "settled": all(r["settled"] for r in results), "location": self.try_state()}
        elif step.name == "shot":
            name = step.args[0] if step.args else datetime.now().strftime("shot-%H%M%S-%f")[:-3]
            path = self.shots / (name if name.lower().endswith(".png") else f"{name}.png")
            path.parent.mkdir(parents=True, exist_ok=True)
            image = self.capture()
            image.save(path)
            return {"path": str(path), "size": list(image.size), "location": self.try_state()}
        elif step.name == "state":
            return self.state()
        elif step.name == "expect":
            state = self.state()
            if not state_matches(state, options):
                raise DriveError(f"expected {step.text.split(None, 1)[1]}, VM is at "
                                 f"{' > '.join(frame['location'] for frame in state['frames'])}")
            return {"location": state["location"]}
        elif step.name == "wait-state":
            deadline = time.monotonic() + float(options.get("timeout", 30))
            target = resolve_point((options["click"],), self.points) if "click" in options else None
            every, clicks, next_click = float(options.get("every", 2)), 0, time.monotonic()
            while True:
                state = self.state()
                if state_matches(state, options):
                    return {"location": state["location"], **({"clicks": clicks} if target else {})}
                if time.monotonic() > deadline:
                    raise DriveError(f"timed out; VM is at {' > '.join(f['location'] for f in state['frames'])}")
                if target is not None and time.monotonic() >= next_click:
                    self.click(*target, right=False, hold=0.15)
                    clicks += 1
                    next_click = time.monotonic() + every
                time.sleep(0.1)
        elif step.name == "point":
            self.points[step.args[0]] = _coordinates(step.args[1:])
        elif step.name == "dialogs":
            return {"dialogs": [{"title": box["title"], "text": box["text"],
                                 "buttons": [text for _, text in box["buttons"]]}
                                for box in win32().dialogs(self.age_pid)]}
        elif step.name == "answer":
            wanted = step.args[0].replace("&", "").lower()
            for box in win32().dialogs(self.age_pid):
                for handle, text in box["buttons"]:
                    if text.replace("&", "").lower() == wanted:
                        win32().post(handle, BM_CLICK, 0, 0)
                        self.event("dialog", text=box["text"], answered=wanted, via="answer")
                        return {"text": box["text"], "answered": text}
            raise DriveError(f"no open message box has a {step.args[0]!r} button")
        elif step.name == "close":
            self.closing = True
            win32().post(self.require_window(), WM_CLOSE, 1, 0)
            code = win32().wait_exit(self.age_pid, float(options.get("timeout", 20)))
            if code is None:
                win32().terminate(self.age_pid)
                raise DriveError("AGE did not exit after WM_CLOSE; terminated it")
            return {"exit_code": code}
        return {}

    # main ------------------------------------------------------------------------------------------
    def run(self) -> int:
        from multiprocessing.connection import Listener
        self.root.mkdir(parents=True, exist_ok=True)
        self.events_path.write_text("", encoding="utf-8")       # one session per events file
        session = session_file()
        authkey = secrets.token_bytes(16)
        address = rf"\\.\pipe\age-native-drive-{os.getpid()}-{secrets.token_hex(4)}"
        listener = Listener(address, family="AF_PIPE", authkey=authkey)
        try:
            folders = self.prepare_saves()
            self.launch(folders)
            threading.Thread(target=self.watch_window, daemon=True).start()
            threading.Thread(target=self.watch_exit, args=(address, authkey), daemon=True).start()
            info = {
                "daemon_pid": os.getpid(), "age_pid": self.age_pid, "address": address, "authkey": authkey.hex(),
                "profile": self.context.profile_id, "saves": self.saves, "shots": str(self.shots),
                "events": str(self.events_path), "started": now_text(),
                "mode": "attach" if self.arguments.attach is not None else "spawn",
            }
            session.write_text(json.dumps(info, ensure_ascii=False, indent=1), encoding="utf-8")
            self.serve(listener)
        except Exception as error:
            self.event("error", message=f"{type(error).__name__}: {error}")
            if not isinstance(error, DriveError):
                import traceback
                traceback.print_exc()
            print(f"native_drive: {error}", file=sys.stderr, flush=True)
            return 1
        finally:
            self.closing = True
            listener.close()
            if session.is_file() and json.loads(session.read_text(encoding="utf-8")).get("daemon_pid") == os.getpid():
                session.unlink()
            self.shutdown()
        return 0

    def serve(self, listener: Any) -> None:
        while not self.closing:
            try:
                connection = listener.accept()
            except (OSError, EOFError) as error:
                if self.closing:
                    break
                self.event("pipe-error", detail=str(error))
                continue
            with connection:
                if self.closing:
                    break
                try:
                    request = json.loads(connection.recv_bytes().decode("utf-8"))
                except (EOFError, OSError, ValueError):
                    continue
                self.handle(connection, request)

    def handle(self, connection: Any, request: dict[str, Any]) -> None:
        if request.get("command") == "status":
            try:
                hooked = self.api.hooked()
            except Exception as error:
                hooked = [f"agent unavailable: {error}"]
            connection.send_bytes(json.dumps({
                "done": True, "ok": True, "hwnd": hex(self.hwnd) if self.hwnd else None,
                "location": self.try_state(), "hooked": hooked, "events": self.take_events(),
            }, ensure_ascii=False).encode("utf-8"))
            return
        ok = True
        for text in request.get("steps", []):
            started = time.monotonic()
            try:
                step = parse_step(text)
                result = {"ok": True, **self.execute(step)}
            except DriveError as error:
                result = {"ok": False, "error": str(error)}
            except Exception as error:   # a bug or a dead session: report it, keep the daemon alive
                result = {"ok": False, "error": f"{type(error).__name__}: {error}"}
            try:
                warps = self.api.warps()
            except Exception:
                warps = {"count": 0}
            if warps["count"] and warps["last"]:
                self.cursor = tuple(warps["last"])
                result["age_moved_cursor"] = warps["last"]      # e.g. onto a confirmation's default button
            result.update(step=text, seconds=round(time.monotonic() - started, 2))
            self.event("step", **result)
            try:
                connection.send_bytes(json.dumps(result, ensure_ascii=False).encode("utf-8"))
            except OSError:
                return
            if not result["ok"]:
                ok = False
                break
            if self.closing:
                break
        connection.send_bytes(json.dumps({"done": True, "ok": ok, "events": self.take_events()},
                                         ensure_ascii=False).encode("utf-8"))

    def shutdown(self) -> None:
        if self.api is not None:
            try:
                self.api.release()
            except Exception:
                pass
        if self.arguments.attach is None and self.age_pid and win32().alive(self.age_pid):
            if self.hwnd:
                win32().post(self.hwnd, WM_CLOSE, 1, 0)
            if win32().wait_exit(self.age_pid, 20) is None:
                win32().terminate(self.age_pid)
        if self.session is not None:
            try:
                self.session.detach()
            except Exception:
                pass
        self.verify_live_untouched()
        summary = {"ended": now_text(), "age_exit_code": self.exit_code, "saves": self.saves}
        (self.root / "last-session.json").write_text(json.dumps(summary, ensure_ascii=False, indent=1), encoding="utf-8")


# --------------------------------------------------------------------------------------------- client

def read_session() -> dict[str, Any] | None:
    path = session_file()
    if not path.is_file():
        return None
    info = json.loads(path.read_text(encoding="utf-8"))
    if sys.platform == "win32" and not win32().alive(info["daemon_pid"]):
        path.unlink()
        return None
    return info


def connect(info: dict[str, Any]) -> Any:
    from multiprocessing.connection import Client
    return Client(info["address"], family="AF_PIPE", authkey=bytes.fromhex(info["authkey"]))


def describe(result: dict[str, Any]) -> str:
    mark = "ok  " if result.get("ok") else "FAIL"
    details = []
    for key in ("error", "path", "location", "settled", "clicks", "seconds", "answered", "exit_code",
                "age_moved_cursor"):
        if key in result and result[key] is not None:
            details.append(f"{key}={result[key]}")
    if "frames" in result:
        details.append("frames=" + " > ".join(frame["location"] for frame in result["frames"]))
    if "dialogs" in result:
        details.append(f"dialogs={json.dumps(result['dialogs'], ensure_ascii=False)}")
    return f"{mark} {result.get('step', '')}  " + "  ".join(details)


def send_steps(steps: list[str], as_json: bool) -> int:
    info = read_session()
    if info is None:
        print("native_drive: no session; run `native_drive.py start`", file=sys.stderr)
        return 2
    ok = True
    with connect(info) as connection:
        connection.send_bytes(json.dumps({"steps": steps}).encode("utf-8"))
        while True:
            try:
                result = json.loads(connection.recv_bytes().decode("utf-8"))
            except EOFError:
                print("native_drive: the session ended", file=sys.stderr)
                return 1
            if result.get("done"):
                ok = ok and result["ok"]
                for event in result.get("events", []):
                    if event["kind"] not in ("step", "folder"):
                        print(json.dumps(event, ensure_ascii=False) if as_json else
                              f"  event {event['kind']}: {json.dumps({k: v for k, v in event.items() if k not in ('t', 'kind')}, ensure_ascii=False)}")
                break
            print(json.dumps(result, ensure_ascii=False) if as_json else describe(result), flush=True)
    return 0 if ok else 1


def command_start(arguments: argparse.Namespace) -> int:
    root = drive_root()
    root.mkdir(parents=True, exist_ok=True)
    if read_session() is not None:
        print("native_drive: a session is already running; `native_drive.py stop` first", file=sys.stderr)
        return 1
    forwarded = ["serve"]
    for flag in ("exe", "sandbox", "seed", "attach"):
        value = getattr(arguments, flag)
        if value is not None:
            forwarded += [f"--{flag}", str(value)]
    if arguments.live_saves:
        forwarded.append("--live-saves")
    if arguments.sound:
        forwarded.append("--sound")
    if arguments.foreground:
        return command_serve(build_parser().parse_args(forwarded))
    log = (root / "serve.log").open("w", encoding="utf-8")
    command = [sys.executable, "-X", "utf8", "-u", str(Path(__file__).resolve()), *forwarded]
    environment = {**os.environ, **paths.CONTEXT.subprocess_environment()}
    flags = 0x00000008 | 0x00000200            # DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP
    try:
        process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL,
                                   env=environment, creationflags=flags | 0x01000000)   # + CREATE_BREAKAWAY_FROM_JOB
    except OSError:
        process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL,
                                   env=environment, creationflags=flags)
    deadline = time.monotonic() + arguments.timeout
    seen: list[dict[str, Any]] = []
    while time.monotonic() < deadline:
        if process.poll() is not None:
            print((root / "serve.log").read_text(encoding="utf-8")[-3000:], file=sys.stderr)
            print(f"native_drive: the daemon exited ({process.returncode})", file=sys.stderr)
            return 1
        info = read_session()
        if info is not None and info["daemon_pid"] == process.pid:
            with connect(info) as connection:
                connection.send_bytes(json.dumps({"command": "status"}).encode("utf-8"))
                status = json.loads(connection.recv_bytes().decode("utf-8"))
            seen += status["events"]
            dialogs = [e for e in seen if e["kind"] == "dialog-open"]
            if status["hwnd"] or dialogs:
                print(f"native_drive: AGE pid {info['age_pid']} ({info['mode']}), window {status['hwnd']}, "
                      f"saves {info['saves']['mode']}: {info['saves'].get('profile') or info['saves']['root']}")
                print(f"native_drive: hooked {', '.join(status['hooked'])}; log {root / 'serve.log'}")
                for event in dialogs:
                    print(f"native_drive: AGE shows a message box: {event['text']!r} {event['buttons']} "
                          f"-> answer with `native_drive.py do \"answer BUTTON\"`")
                return 0
        time.sleep(0.3)
    print(f"native_drive: AGE did not open a window within {arguments.timeout:.0f} s; see {root / 'serve.log'}",
          file=sys.stderr)
    return 1


def command_serve(arguments: argparse.Namespace) -> int:
    try:
        return Daemon(arguments).run()
    except DriveError as error:
        print(f"native_drive: {error}", file=sys.stderr)
        return 1


def command_stop(arguments: argparse.Namespace) -> int:
    info = read_session()
    if info is None:
        print("native_drive: no session")
        return 0
    if info["mode"] == "spawn":
        code = send_steps([f"close timeout={arguments.timeout}"], as_json=False)
    else:
        code = 0
    if win32().wait_exit(info["daemon_pid"], arguments.timeout) is None:
        win32().terminate(info["daemon_pid"])
    summary_path = drive_root() / "last-session.json"
    events = drive_root() / "events.jsonl"
    if events.is_file():
        for line in events.read_text(encoding="utf-8").splitlines()[-6:]:
            record = json.loads(line)
            if record["kind"].startswith("live-saves"):
                print(f"native_drive: {record['kind']}" + (
                    "" if record["kind"].endswith("unchanged") else f": {json.dumps(record, ensure_ascii=False)}"))
    if summary_path.is_file():
        print(f"native_drive: session ended; {summary_path}")
    return code


def command_status(arguments: argparse.Namespace) -> int:
    info = read_session()
    if info is None:
        print("native_drive: no session")
        return 1
    with connect(info) as connection:
        connection.send_bytes(json.dumps({"command": "status"}).encode("utf-8"))
        status = json.loads(connection.recv_bytes().decode("utf-8"))
    print(json.dumps({**{k: v for k, v in info.items() if k != "authkey"}, **status}, ensure_ascii=False, indent=1))
    return 0


def command_run(arguments: argparse.Namespace) -> int:
    path = Path(arguments.file)
    try:
        steps = parse_steps(path.read_text(encoding="utf-8").splitlines())
    except DriveError as error:
        print(f"native_drive: {path}: {error}", file=sys.stderr)
        return 1
    return send_steps([step.text for step in steps], arguments.json)


def command_do(arguments: argparse.Namespace) -> int:
    try:
        steps = [parse_step(text) for text in arguments.steps]
    except DriveError as error:
        print(f"native_drive: {error}", file=sys.stderr)
        return 1
    return send_steps([step.text for step in steps], arguments.json)


def command_backup(arguments: argparse.Namespace) -> int:
    live = live_root()
    if not live.is_dir():
        print(f"native_drive: no live saves at {live}")
        return 1
    backup, created = backup_tree(live, drive_root() / "backups")
    print(f"native_drive: {'backed up' if created else 'already backed up'}: {live} -> {backup}")
    return 0


def command_restore(arguments: argparse.Namespace) -> int:
    source = backup_contents(Path(arguments.backup))
    if not (source / "SAVE").is_dir() and not (source / "SYS4REG.INI").is_file():
        print(f"native_drive: {source} does not look like a save folder copy (no SAVE\\ or SYS4REG.INI)", file=sys.stderr)
        return 1
    if read_session() is not None:
        print("native_drive: stop the running session first", file=sys.stderr)
        return 1
    import frida
    if any(process.name.upper() == "AGE.EXE" for process in frida.get_local_device().enumerate_processes()):
        print("native_drive: close AGE before restoring its saves", file=sys.stderr)
        return 1
    live = live_root()
    if live.is_dir():
        safety, _ = backup_tree(live, drive_root() / "backups")
        print(f"native_drive: current live saves kept in {safety}")
        shutil.rmtree(live)
    shutil.copytree(source, live)
    print(f"native_drive: restored {source} -> {live} ({len(snapshot_tree(live))} files)")
    return 0


def command_steps(arguments: argparse.Namespace) -> int:
    for spec in STEPS.values():
        print(spec.usage)
    profile = load_profile()
    if profile.points:
        print(f"\npoints ({paths.CONTEXT.profile_id}): " + ", ".join(sorted(profile.points)))
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)

    def launch_options(command: argparse.ArgumentParser) -> None:
        command.add_argument("--exe", help="AGE.EXE to spawn (default: the native_scratch.py copy)")
        saves = command.add_mutually_exclusive_group()
        saves.add_argument("--sandbox", help="persistent named save sandbox (default: a fresh empty one)")
        saves.add_argument("--live-saves", action="store_true", help="use the real save folders (backed up first)")
        command.add_argument("--seed", help="fill a new sandbox from 'live' or a backup folder")
        command.add_argument("--sound", action="store_true",
                             help="keep the sandbox profile's volume (default: the profile's [sandbox.quiet] settings)")
        command.add_argument("--attach", nargs="?", const="auto",
                             help="hook an AGE that is already running (pid or auto); its saves stay live")

    start = commands.add_parser("start", help="launch AGE under the driver and return when its window is up")
    launch_options(start)
    start.add_argument("--foreground", action="store_true", help="run the daemon in this process")
    start.add_argument("--timeout", type=float, default=90.0, help="seconds to wait for AGE's window")
    start.set_defaults(handler=command_start)
    serve = commands.add_parser("serve", help="the daemon itself (start runs it detached)")
    launch_options(serve)
    serve.set_defaults(handler=command_serve)
    do = commands.add_parser("do", help="run steps given on the command line")
    do.add_argument("steps", nargs="+")
    do.add_argument("--json", action="store_true", help="print one JSON result per step")
    do.set_defaults(handler=command_do)
    run = commands.add_parser("run", help="run a .steps file")
    run.add_argument("file")
    run.add_argument("--json", action="store_true")
    run.set_defaults(handler=command_run)
    status = commands.add_parser("status", help="session, window, hooks, VM position, new events")
    status.set_defaults(handler=command_status)
    stop = commands.add_parser("stop", help="close AGE and end the session")
    stop.add_argument("--timeout", type=float, default=20.0)
    stop.set_defaults(handler=command_stop)
    steps = commands.add_parser("steps", help="list the step syntax and this profile's named points")
    steps.set_defaults(handler=command_steps)
    backup = commands.add_parser("backup", help="copy the live save folder into native-drive/backups/")
    backup.set_defaults(handler=command_backup)
    restore = commands.add_parser("restore", help="replace the live save folder with a backup (current one is kept)")
    restore.add_argument("backup")
    restore.set_defaults(handler=command_restore)
    return parser


def main(argv: list[str] | None = None) -> int:
    arguments = build_parser().parse_args(argv)
    if arguments.command in ("start", "serve") and arguments.seed and (arguments.live_saves or arguments.attach):
        print("native_drive: --seed fills a sandbox; it cannot be combined with live saves", file=sys.stderr)
        return 1
    try:
        return arguments.handler(arguments)
    except DriveError as error:
        print(f"native_drive: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
