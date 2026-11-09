#!/usr/bin/env python3
"""The shared game-driving core: one step language, run against native AGE or the port.

Both targets serve one protocol (docs/superpowers/specs/2026-11-03-game-driving-design.md §4): JSON lines over
loopback TCP, a token in the first ``hello``, then primitive operations (``state``, ``pointer``, ``click``,
``key``, ``wheel``, ``wait``, ``settle``, ``shot``, ``close`` and each target's extras). This module parses and
validates steps, checks them against the target's capabilities, and runs them as those primitives
(:class:`Executor`). It also holds the profile files (``tools/drive/<profile>.toml``), the save-sandbox and seed
helpers, and the session files under ``build/games/<profile>/drive/<target>/``.

Nothing here needs the game, Frida, Godot or Pillow; ``tools/test_drive_core.py`` runs it against a fake server.
The CLI is ``tools/drive.py``; the servers are ``tools/drive_native.py`` and the port's ``--control`` channel.
"""
from __future__ import annotations

import json
import os
import re
import shlex
import shutil
import socket
import sys
import time
import tomllib
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any, Callable, Iterable

sys.path.insert(0, str(Path(__file__).resolve().parent))
import paths  # noqa: E402

HERE = Path(__file__).resolve().parent
DATA = HERE / "drive"
TARGETS = ("native", "port")
PROTOCOL_VERSION = 1
TOKEN_ENVIRONMENT = "OME_DRIVE_TOKEN"


class DriveError(Exception):
    """A step, request or command that cannot run; the message is meant for the user."""


def now_text() -> str:
    return datetime.now().isoformat(timespec="milliseconds")


# --------------------------------------------------------------------------------------------- layout

def drive_root(target: str, context: paths.ToolContext = paths.CONTEXT) -> Path:
    """build/games/<profile>/drive/<target>/: session.json, logs, events, shots/, sandbox/, backups/."""
    if target not in TARGETS:
        raise DriveError(f"unknown target {target!r} (native or port)")
    return context.game_build / "drive" / target


def session_file(target: str, context: paths.ToolContext = paths.CONTEXT) -> Path:
    return drive_root(target, context) / "session.json"


def sandbox_dir(target: str, name: str, context: paths.ToolContext = paths.CONTEXT) -> Path:
    if not re.fullmatch(r"[\w.-]+", name):
        raise DriveError(f"sandbox names are plain folder names (letters, digits, . _ -), got {name!r}")
    return drive_root(target, context) / "sandbox" / name


def write_json_atomic(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=1), encoding="utf-8")
    os.replace(temporary, path)


# --------------------------------------------------------------------------------------------- saves

# Folders native AGE asks SHGetFolderPathA for (CSIDL & 0xff) and where a native sandbox puts them. Himegari asks
# for LOCAL_APPDATA (save root, SYS4REG.INI) and DESKTOPDIRECTORY (F1 screenshots, debug report); the rest are
# cheap insurance for other profiles.
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


def native_profile_in_sandbox(sandbox: Path, live: Path, appdata: Path) -> Path:
    """Where native AGE's profile root lands inside a sandbox: the live root's path below LOCALAPPDATA, re-rooted at
    the sandbox's LocalAppData folder."""
    if not live.is_relative_to(appdata):
        raise DriveError(f"the save root {live} is not under %LOCALAPPDATA%; a sandbox cannot redirect it "
                         "(use --live-saves)")
    return sandbox_folders(sandbox)[0x1C] / live.relative_to(appdata)


def sandbox_profile_root(target: str, sandbox: Path, context: paths.ToolContext = paths.CONTEXT) -> Path:
    """The folder holding SYS4REG.INI and SAVE\\ inside a sandbox. The port's --data-root is the sandbox itself;
    native AGE's sits where its redirected LOCALAPPDATA puts it."""
    if target == "port":
        return sandbox
    return native_profile_in_sandbox(sandbox, live_root(context), local_app_data())


def resolve_seed(spec: str, context: paths.ToolContext = paths.CONTEXT) -> Path:
    """--seed: 'live', 'native:NAME' / 'port:NAME' (another sandbox's profile root), or a folder (a backup or a plain
    copy of a profile root)."""
    if spec == "live":
        return live_root(context)
    target, colon, name = spec.partition(":")
    if colon and target in TARGETS:
        return sandbox_profile_root(target, sandbox_dir(target, name, context), context)
    return backup_contents(Path(spec))


def parse_settings(items: Iterable[str]) -> dict[str, str]:
    """`start --set` values: SECTION.KEY=VALUE each, later ones winning."""
    settings: dict[str, str] = {}
    for item in items:
        dotted, equals, value = item.partition("=")
        section, dot, key = dotted.strip().partition(".")
        if not equals or not dot or not section or not key:
            raise DriveError(f"--set {item!r} must be SECTION.KEY=VALUE")
        settings[f"{section}.{key}"] = value.strip()
    return settings


def prepare_sandbox(target: str, name: str, seed: str | None, sound: bool, profile: "Profile",
                    context: paths.ToolContext = paths.CONTEXT,
                    log: Callable[[str], None] = lambda _: None,
                    settings: dict[str, str] | None = None) -> tuple[Path, Path]:
    """Create (or reuse) sandbox NAME and return (sandbox, profile root). 'fresh' is emptied on every start; other
    names persist. A new sandbox is filled from the seed; the profile's settings (and, unless sound, its quiet
    settings), then the given `--set` settings, are written into its SYS4REG.INI on every start."""
    sandbox = sandbox_dir(target, name, context)
    if name == "fresh" and sandbox.exists():
        shutil.rmtree(sandbox)
    created = not sandbox.exists()
    profile_root = sandbox_profile_root(target, sandbox, context)
    if target == "native":
        for path in sandbox_folders(sandbox).values():
            path.mkdir(parents=True, exist_ok=True)
    profile_root.mkdir(parents=True, exist_ok=True)
    if seed:
        if created:
            source = resolve_seed(seed, context)
            if not source.is_dir():
                raise DriveError(f"seed {source} is not a folder")
            if source.resolve() == profile_root.resolve():
                raise DriveError(f"seed {source} is this sandbox")
            shutil.copytree(source, profile_root, dirs_exist_ok=True)
            log(f"seeded {profile_root} from {source}")
        else:
            log(f"sandbox {name} exists; --seed applies only to a new sandbox")
    overrides = {**profile.settings, **({} if sound else profile.quiet), **(settings or {})}
    if overrides:
        ini = profile_root / profile.settings_file
        text = ini.read_bytes().decode("cp932") if ini.is_file() else ""
        ini.write_bytes(update_ini(text, overrides).encode("cp932"))
        log(f"wrote {', '.join(f'{k}={v}' for k, v in overrides.items())} into {ini}")
    return sandbox, profile_root


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


# --------------------------------------------------------------------------------------------- profiles

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
    """tools/drive/<profile>.toml. [points], [advance] and [sandbox.*] serve both targets; [native.*] only the
    native daemon."""
    points: dict[str, tuple[int, int]] = field(default_factory=dict)
    advance_point: str = "adv.next"
    settings_file: str = ""
    settings: dict[str, str] = field(default_factory=dict)
    quiet: dict[str, str] = field(default_factory=dict)
    dialogs: tuple[DialogPolicy, ...] = ()
    engine: dict[str, int] | None = None        # ctx_global, frame_stride, frame_count [, frame field offsets]
    keep_polling: dict[str, int] | None = None  # setter, activate_return [, gate]
    rand: dict[str, int] | None = None          # op60_rand_return


# Engine field offsets a profile may give in [native.engine] / [native.keep_polling]. vm-map/engine-ctx.toml holds
# Himegari's EngineCtx (the /v2 image); another engine revision lays the context out differently and names its own.
ENGINE_FIELD_KEYS = ("cur_ctx_index", "frame_codebase", "frame_pc", "frame_script_resource_id")
POLLING_GATE_KEY = "gate"


def parse_profile(text: str) -> Profile:
    data = tomllib.loads(text)
    unknown = set(data) - {"points", "advance", "sandbox", "native"}
    if unknown:
        raise DriveError(f"unknown profile section(s): {', '.join(sorted(unknown))}")
    points = {}
    for name, value in data.get("points", {}).items():
        if not (isinstance(value, list) and len(value) == 2 and all(isinstance(v, int) for v in value)):
            raise DriveError(f"point {name!r} must be [x, y]")
        points[name] = (value[0], value[1])
    advance = data.get("advance", {}).get("point", "adv.next")
    sandbox = data.get("sandbox", {})
    settings = {key: str(value) for key, value in sandbox.get("settings", {}).items()}
    quiet = {key: str(value) for key, value in sandbox.get("quiet", {}).items()}
    if (settings or quiet) and not sandbox.get("settings_file"):
        raise DriveError("[sandbox] settings need settings_file")
    native = data.get("native", {})
    dialogs = []
    for entry in native.get("dialog", []):
        answer = str(entry.get("answer", "")).lower()
        if answer not in DIALOG_ANSWERS or not entry.get("match"):
            raise DriveError(f"dialog policy needs match and one of {', '.join(DIALOG_ANSWERS)}: {entry}")
        dialogs.append(DialogPolicy(entry["match"], answer, entry.get("reason", "")))
    engine = _native_table(native, "engine", ("ctx_global", "frame_stride", "frame_count"), ENGINE_FIELD_KEYS)
    keep_polling = _native_table(native, "keep_polling", ("setter", "activate_return"), (POLLING_GATE_KEY,))
    if keep_polling is not None and engine is None:
        raise DriveError("[native.keep_polling] needs [native.engine] (it reopens the gate through the ctx)")
    rand = _native_table(native, "rand", ("op60_rand_return",))
    if rand is not None and engine is None:
        raise DriveError("[native.rand] needs [native.engine] (it names the call site from the running frame)")
    return Profile(points, advance, sandbox.get("settings_file", ""), settings, quiet, tuple(dialogs), engine,
                   keep_polling, rand)


def _native_table(native: dict[str, Any], name: str, keys: tuple[str, ...],
                  optional: tuple[str, ...] = ()) -> dict[str, int] | None:
    if name not in native:
        return None
    unknown = set(native[name]) - set(keys) - set(optional)
    if unknown:
        raise DriveError(f"[native.{name}] has unknown key(s): {', '.join(sorted(unknown))}")
    try:
        table = {key: int(native[name][key]) for key in keys}
    except KeyError as error:
        raise DriveError(f"[native.{name}] lacks {error.args[0]}") from None
    table.update({key: int(native[name][key]) for key in optional if key in native[name]})
    return table


def load_profile(context: paths.ToolContext = paths.CONTEXT) -> Profile:
    path = DATA / f"{context.profile_id}.toml"
    return parse_profile(path.read_text(encoding="utf-8")) if path.is_file() else Profile()


# --------------------------------------------------------------------------------------------- steps

VK_NAMES = {
    "enter": 0x0D, "return": 0x0D, "space": 0x20, "tab": 0x09, "backspace": 0x08, "shift": 0x10,
    "ctrl": 0x11, "control": 0x11, "alt": 0x12, "pause": 0x13, "pgup": 0x21, "pageup": 0x21, "pgdn": 0x22,
    "pagedown": 0x22, "end": 0x23, "home": 0x24, "left": 0x25, "up": 0x26, "right": 0x27, "down": 0x28,
    "insert": 0x2D, "delete": 0x2E, "lshift": 0xA0, "rshift": 0xA1, "lctrl": 0xA2, "rctrl": 0xA3,
}
VK_NAMES.update({f"f{n}": 0x6F + n for n in range(1, 13)})
# Refused on both targets so that every scenario stays runnable natively.
REFUSED_KEYS = {
    0x1B: "ESC posts WM_CLOSE in native AGE (age_main_window_proc), which opens its 終了 dialog; "
          "use close-request (or close to quit)",
}
# F1 and the menu's 表示画面を保存 save a screenshot BMP in the Desktop folder (engine_save_screenshot_bmp), and
# 画像の保存場所 sets system:SaveBMPPath. A sandbox redirects the Desktop (native: CSIDL 0x10; port: --desktop-dir), so
# only a native session on the live folders refuses them.
LIVE_REFUSED_KEYS = {0x70: "F1 would save a screenshot BMP on the real Desktop; drive a sandbox"}
LIVE_REFUSED_MENU_COMMANDS = {
    0x9C46: "表示画面を保存 would save a screenshot BMP on the real Desktop; drive a sandbox",
    0x9C47: "画像の保存場所 would change the live system:SaveBMPPath; drive a sandbox",
}
WAITING_KINDS = ("input", "menu", "movie", "transition", "sleep")
CONDITION_KEYS = ("at", "top", "script", "waiting")


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


ALT_REFUSED_KEYS = {
    0x73: "Alt+F4 is SC_CLOSE, which posts WM_CLOSE to native AGE and opens its 終了 dialog; use close-request",
}


def key_chord(name: str) -> tuple[bool, int]:
    """A `key` step's key: (alt, vk). `alt+NAME` presses NAME with Alt held, as a chord (Alt+Enter, Alt+M)."""
    alt = name.lower().startswith("alt+")
    vk = virtual_key(name[4:] if alt else name)
    if alt and vk in (0x12, 0xA4, 0xA5):
        raise DriveError(f"key {name}: Alt is already held; press it alone with `key alt`")
    if alt and vk in ALT_REFUSED_KEYS:
        raise DriveError(f"key {name}: {ALT_REFUSED_KEYS[vk]}")
    return alt, vk


# Capabilities both targets offer (native: drive_native.CAPABILITIES; port: PortCapabilities in godot/Main.Drive.cs).
# Committed scenarios may use these and nothing target-only.
SHARED_CAPABILITIES = ("dialogs", "answer", "window", "menu", "rand", "options")

# AGERC.DLL's menu-bar commands (menu 110; docs/engine-re.md "Native menu bar"), by the names `menu` steps take.
MENU_COMMANDS = {
    "exit": 0x9c41, "save": 0x9c43, "load": 0x9c44, "version": 0x9c45, "save-screen": 0x9c46,
    "image-folder": 0x9c47, "fullscreen": 0x9c5a, "windowed": 0x9c5b, "skip": 0x9c63, "hide": 0x9c64,
    "auto": 0x9c65, "sound-settings": 0x9c68, "message-settings": 0x9c6c, "system-info": 0x9c6d,
    "other-settings": 0x9c6e, "read-skip": 0x9c6f,
}


# The options `option` sets (docs/superpowers/specs/2026-11-09-engine-shell-design.md §5.3; the port's ShellOptions):
# name -> (section, min, max, step, live). Values are the registry's: ms per glyph, ms, basis points, percent, 0/1. A
# live option applies while 設定 is open (the ｻｳﾝﾄﾞ volumes and routes); the rest apply on OK.
OPTIONS = {
    "message-speed": ("message", 0, 100, 1, False),
    "window-opacity": ("message", 0, 16, 1, False),
    "auto-wait-voice": ("message", 0, 10000, 100, False),
    "auto-wait-text": ("message", 0, 10000, 100, False),
    "smooth-text": ("message", 0, 1, 1, False),
    "wheel-advance": ("message", 0, 1, 1, False),
    "master-volume": ("sound", 0, 10000, 100, True),
    "bgm-volume": ("sound", 0, 10000, 100, True),
    "se-volume": ("sound", 0, 10000, 100, True),
    "voice-volume": ("sound", 0, 10000, 100, True),
    "bgm": ("sound", 0, 1, 1, True),
    "se": ("sound", 0, 1, 1, True),
    "voice": ("sound", 0, 1, 1, True),
    "lower-bgm-on-voice": ("sound", 0, 1, 1, False),
    "lower-bgm-level": ("sound", 0, 100, 1, False),
    "effect-skip": ("other", 0, 1, 1, False),
}

# The menu command that opens each section's 設定 tab, and the section the `options` step opens.
SECTION_COMMANDS = {"message": "message-settings", "sound": "sound-settings", "other": "other-settings"}
OPTIONS_SECTION = "message"


def option_value(name: str, text: str) -> tuple[str, int]:
    """An `option` step's name and value: a value in the option's domain and on its step (on/off for a switch)."""
    key = name.lower()
    if key not in OPTIONS:
        raise DriveError(f"option takes one of {', '.join(OPTIONS)}, got {name!r}")
    _, low, high, step, _ = OPTIONS[key]
    switch = (low, high) == (0, 1)
    if switch and text.lower() in ("on", "off"):
        return key, int(text.lower() == "on")
    try:
        value = int(text, 0)
    except ValueError:
        raise DriveError(f"option {key} takes {'on, off or ' if switch else ''}an integer, got {text!r}") from None
    if not low <= value <= high or (value - low) % step:
        raise DriveError(f"option {key} takes {low}..{high}{f' in steps of {step}' if step > 1 else ''}, got {value}")
    return key, value


def rand_seed(text: str) -> int | None:
    """A rand-seed step's seed: an integer from 0 to 0xffffffff, or None for `off`."""
    if text.lower() == "off":
        return None
    try:
        seed = int(text, 0)
    except ValueError:
        raise DriveError(f"rand-seed takes a seed or off, got {text!r}") from None
    if not 0 <= seed <= 0xFFFFFFFF:
        raise DriveError("rand-seed takes a seed from 0 to 0xffffffff")
    return seed


def menu_command(name: str) -> int:
    """A `menu` step's command: a name from MENU_COMMANDS or a numeric WM_COMMAND id."""
    key = name.lower()
    if key in MENU_COMMANDS:
        return MENU_COMMANDS[key]
    try:
        command = int(name, 0)
    except ValueError:
        raise DriveError(f"menu takes a command id or one of {', '.join(MENU_COMMANDS)}, got {name!r}") from None
    if not 0 < command <= 0xFFFF:
        raise DriveError(f"menu command ids are 1..0xffff, got {name!r}")
    return command


@dataclass(frozen=True)
class StepSpec:
    min_args: int
    max_args: int
    options: frozenset[str]
    usage: str
    capability: str | None = None       # the target capability the step needs, if it is not common


def _spec(min_args: int, max_args: int, options: str, usage: str, capability: str | None = None) -> StepSpec:
    return StepSpec(min_args, max_args, frozenset(options.split()), usage, capability)


STEPS = {
    "move": _spec(1, 2, "", "move X Y | move POINT          park the cursor (hover)"),
    "click": _spec(1, 2, "hold", "click X Y | click POINT [hold=MS]   left click"),
    "rclick": _spec(1, 2, "hold", "rclick X Y | rclick POINT [hold=MS] right click (opens the ADV menu)"),
    "key": _spec(1, 1, "hold", "key [alt+]NAME|0xVK [hold=MS] press a key, alt+ with Alt held; ESC and Alt+F4 are "
                 "refused (F1 saves a screenshot BMP in the sandbox Desktop)"),
    "wheel": _spec(1, 2, "", "wheel up|down [N]             turn the wheel N notches at the cursor"),
    "wait": _spec(1, 1, "", "wait SECONDS"),
    "settle": _spec(0, 0, "timeout quiet threshold",
                    "settle [timeout=S] [quiet=S] [threshold=F]  wait until the game is idle (native: the screen "
                    "stops changing; port: an input or menu wait with nothing animating)"),
    "advance": _spec(0, 1, "timeout", "advance [N]                   N x (click the advance point, settle)"),
    "shot": _spec(0, 1, "", "shot [NAME]                   save the game canvas to shots/NAME.png"),
    "state": _spec(0, 0, "", "state                         the VM frame stack (script@offset per frame)"),
    "expect": _spec(0, 0, "at top script waiting",
                    "expect [at=S@OFF] [top=S@OFF] [script=S] [waiting=KIND]   fail unless the VM matches all"),
    "wait-state": _spec(0, 0, "at top script waiting timeout click every",
                        "wait-state [at=…] [top=…] [script=…] [waiting=KIND] [timeout=S] [click=POINT every=S]   "
                        "wait until the VM matches, clicking POINT every S seconds (default 2) meanwhile"),
    "point": _spec(3, 3, "", "point NAME X Y                define a named point for this session"),
    "speed": _spec(1, 1, "", "speed FACTOR                  run the frame clock FACTOR times as fast (port)", "speed"),
    "rand-seed": _spec(1, 1, "", "rand-seed SEED|off            draw op 0x60 from SEED's per-call-site streams, alike on "
                       "both targets (off: the game's own draws); reports the draws per site since the last "
                       "rand-seed", "rand"),
    "dialogs": _spec(0, 0, "", "dialogs                       list the game's open dialogs (native message boxes; "
                     "both: the 終了 dialog)", "dialogs"),
    "answer": _spec(1, 1, "", "answer BUTTON                 press a button of an open dialog (radio buttons too)",
                    "answer"),
    "close-request": _spec(0, 0, "", "close-request                 ask to close as the close button and ESC do: AGE "
                           "opens its 終了 dialog (quit / return to title / cancel)"),
    "window": _spec(1, 1, "", "window minimize|restore       minimize or restore the game window, as its title-bar "
                    "and taskbar buttons do (AGE: WM_SYSCOMMAND SC_MINIMIZE/SC_RESTORE)", "window"),
    "menu": _spec(1, 1, "", "menu NAME|ID                  choose a menu-bar command as a user does: the menu "
                  "refreshes, a grayed item fails the step (AGE: WM_INITMENU, then WM_COMMAND); names: skip, "
                  "read-skip, auto, hide, exit, windowed, …", "menu"),
    "option": _spec(2, 2, "", "option NAME VALUE             set one 設定 option and commit it as OK does (the "
                    "section's menu command opens 設定; a grayed one fails the step); VALUE in the registry's units, "
                    "on/off for a switch; names: message-speed, wheel-advance, se, effect-skip, …", "options"),
    "options": _spec(0, 0, "", "options                       list the 設定 options with their values and domains "
                     "(opens 設定 and cancels it)", "options"),
    "close": _spec(0, 0, "timeout", "close [timeout=S]             quit the game and end the session"),
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


def normalize_script(name: str) -> str:
    return name.strip().upper().removesuffix(".BIN")


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
        raise DriveError(f"{where}unknown step {words[0]!r} (see `drive.py <target> steps`)")
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
        key_chord(step.args[0])
    elif step.name == "wheel":
        if step.args[0].lower() not in ("up", "down"):
            raise DriveError("wheel takes up or down")
        if len(step.args) > 1 and not step.args[1].isdigit():
            raise DriveError("wheel notches must be a number")
    elif step.name == "window":
        if step.args[0].lower() not in ("minimize", "restore"):
            raise DriveError("window takes minimize or restore")
    elif step.name == "menu":
        menu_command(step.args[0])
    elif step.name == "option":
        option_value(*step.args)
    elif step.name == "wait":
        try:
            float(step.args[0])
        except ValueError:
            raise DriveError(f"wait takes seconds, got {step.args[0]!r}") from None
    elif step.name == "advance" and step.args and not step.args[0].isdigit():
        raise DriveError("advance takes a page count")
    elif step.name == "shot" and step.args and not re.fullmatch(r"[\w.-]+", step.args[0]):
        raise DriveError("shot names are plain file names (letters, digits, . _ -)")
    elif step.name == "point":
        _coordinates(step.args[1:])
    elif step.name == "speed":
        try:
            factor = float(step.args[0])
        except ValueError:
            raise DriveError(f"speed takes a factor, got {step.args[0]!r}") from None
        if not 0.05 <= factor <= 8:
            raise DriveError("speed factor must be between 0.05 and 8")
    elif step.name == "rand-seed":
        rand_seed(step.args[0])
    elif step.name in ("expect", "wait-state"):
        condition_of(step)
        if "every" in step.options and "click" not in step.options:
            raise DriveError("every= needs click=")


def condition_of(step: Step) -> dict[str, str]:
    """The protocol condition of an expect/wait-state step (§4 "Conditions"): every key given must match."""
    condition: dict[str, str] = {}
    for key in CONDITION_KEYS:
        if key not in step.options:
            continue
        value = step.options[key]
        if key in ("at", "top"):
            script, offset = parse_location(value)
            condition[key] = f"{script}@{offset:#x}"
        elif key == "script":
            condition[key] = normalize_script(value)
        else:
            if value not in WAITING_KINDS:
                raise DriveError(f"waiting= takes one of {', '.join(WAITING_KINDS)}, got {value!r}")
            condition[key] = value
    if not condition:
        raise DriveError(f"{step.name} needs at least one of at=, top=, script=, waiting=")
    return condition


def _coordinates(words: Iterable[str]) -> tuple[int, int]:
    words = list(words)
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


def step_capabilities(step: Step) -> set[str]:
    needed = {STEPS[step.name].capability} - {None}
    if "waiting" in step.options:
        needed.add("waiting")
    return needed


def check_capabilities(steps: Iterable[Step], capabilities: Iterable[str], target: str) -> None:
    """Fail before the first step runs if any step needs something the target lacks (spec §6)."""
    offered = set(capabilities)
    problems = []
    for step in steps:
        missing = step_capabilities(step) - offered
        if missing:
            where = f"line {step.line}: " if step.line else ""
            problems.append(f"{where}`{step.text}` needs {', '.join(sorted(missing))}, which the {target} lacks")
    if problems:
        raise DriveError("; ".join(problems))


# --------------------------------------------------------------------------------------------- state

def frame_location(frame: dict[str, Any]) -> str:
    return frame.get("location") or f"{frame['script']}@{frame['offset']:#x}"


def describe_stack(state: dict[str, Any]) -> str:
    frames = state.get("frames") or []
    stack = " > ".join(frame_location(frame) for frame in frames) or "no frame"
    return stack + (f" waiting={state['waiting']}" if state.get("waiting") else "")


def condition_matches(state: dict[str, Any], condition: dict[str, str]) -> bool:
    """All keys must match (the same rule as the port's DriveCondition)."""
    frames = state.get("frames") or []

    def same(frame: dict[str, Any], location: str) -> bool:
        script, offset = parse_location(location)
        return frame["script"] == script and frame["offset"] == offset

    for key, value in condition.items():
        if key == "at" and not any(same(frame, value) for frame in frames):
            return False
        if key == "top" and not (frames and same(frames[-1], value)):
            return False
        if key == "script" and not any(frame["script"] == normalize_script(value) for frame in frames):
            return False
        if key == "waiting" and state.get("waiting") != value:
            return False
        if key not in CONDITION_KEYS:
            raise DriveError(f"unknown condition {key!r}")
    return True


# --------------------------------------------------------------------------------------------- protocol client

class Client:
    """One protocol connection: hello with the token, then one request at a time."""

    def __init__(self, port: int, token: str, *, host: str = "127.0.0.1", connect_timeout: float = 10.0) -> None:
        try:
            self.socket = socket.create_connection((host, port), timeout=connect_timeout)
        except OSError as error:
            raise DriveError(f"cannot connect to 127.0.0.1:{port}: {error}") from None
        self.socket.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        self.stream = self.socket.makefile("rwb")
        self.next_id = 1
        self.events: list[dict[str, Any]] = []
        self.hello = self.call("hello", token=token)

    @property
    def target(self) -> str:
        return self.hello.get("target", "?")

    @property
    def capabilities(self) -> list[str]:
        return list(self.hello.get("capabilities", []))

    def call(self, op: str, timeout: float = 60.0, **arguments: Any) -> dict[str, Any]:
        request = {"id": self.next_id, "op": op, **arguments}
        self.next_id += 1
        self.socket.settimeout(timeout)
        try:
            self.stream.write((json.dumps(request, ensure_ascii=False) + "\n").encode("utf-8"))
            self.stream.flush()
            line = self.stream.readline()
        except TimeoutError:
            raise DriveError(f"{op}: no reply within {timeout:.0f} s") from None
        except OSError as error:
            raise DriveError(f"{op}: the connection failed ({error})") from None
        if not line:
            raise DriveError(f"{op}: the game closed the connection")
        try:
            reply = json.loads(line.decode("utf-8"))
        except ValueError:
            raise DriveError(f"{op}: unreadable reply {line[:200]!r}") from None
        self.events.extend(reply.get("events") or [])
        if reply.get("id") not in (request["id"], 0):
            raise DriveError(f"{op}: reply id {reply.get('id')} does not match request {request['id']}")
        if not reply.get("ok"):
            raise DriveError(reply.get("error") or f"{op} failed")
        return reply.get("result") or {}

    def take_events(self) -> list[dict[str, Any]]:
        taken, self.events = self.events, []
        return taken

    def close(self) -> None:
        for closer in (self.stream.close, self.socket.close):
            try:
                closer()
            except OSError:
                pass

    def __enter__(self) -> "Client":
        return self

    def __exit__(self, *_: Any) -> None:
        self.close()


def pid_alive(pid: int) -> bool:
    if sys.platform == "win32":
        return windows().alive(pid)
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    return True


def read_session(target: str, context: paths.ToolContext = paths.CONTEXT,
                 alive: Callable[[int], bool] = pid_alive) -> dict[str, Any] | None:
    """The target's session file, or None (a file whose server process has gone is removed)."""
    path = session_file(target, context)
    if not path.is_file():
        return None
    try:
        info = json.loads(path.read_text(encoding="utf-8"))
    except ValueError:
        return None
    if not alive(int(info.get("pid", 0))):
        path.unlink(missing_ok=True)
        return None
    return info


def connect(target: str, context: paths.ToolContext = paths.CONTEXT) -> Client:
    info = read_session(target, context)
    if info is None:
        raise DriveError(f"no {target} session; run `drive.py {target} start`")
    return Client(int(info["port"]), info["token"])


# --------------------------------------------------------------------------------------------- executor

DEFAULT_HOLD_MS = 150
DEFAULT_WAIT_S = 30.0
DEFAULT_POKE_S = 2.0
DEFAULT_SETTLE_S = 10.0
REPLY_MARGIN_S = 30.0       # how much longer than an operation's own timeout the client waits for its reply


class Executor:
    """Runs steps as protocol primitives. ``wait`` sleeps here; ``point`` and ``advance`` are resolved here; the rest
    are one or a few requests each."""

    def __init__(self, client: Client, profile: Profile, shots: Path, *,
                 on_close: Callable[[dict[str, Any]], dict[str, Any]] | None = None,
                 on_window: Callable[[dict[str, Any]], None] | None = None,
                 sleep: Callable[[float], None] = time.sleep, clock: Callable[[], float] = time.monotonic) -> None:
        self.client = client
        self.points = dict(profile.points)
        self.advance_point = profile.advance_point
        self.shots = shots
        self.on_close = on_close
        self.on_window = on_window
        self.sleep = sleep
        self.clock = clock
        self.closed = False

    def state(self) -> dict[str, Any]:
        return self.client.call("state")

    def location(self) -> str | None:
        try:
            return self.state().get("location")
        except DriveError:
            return None

    def click(self, x: int, y: int, button: str = "left", hold_ms: int = DEFAULT_HOLD_MS) -> dict[str, Any]:
        result = self.client.call("click", x=x, y=y, button=button, hold_ms=hold_ms)
        return {"cursor_moved_to": result["cursor_moved_to"]} if result.get("cursor_moved_to") else {}

    def settle(self, timeout: float, **options: Any) -> dict[str, Any]:
        result = self.client.call("settle", timeout=timeout + REPLY_MARGIN_S, timeout_ms=int(timeout * 1000),
                                  **options)
        return {"settled": bool(result.get("settled")), "elapsed_ms": result.get("elapsed_ms")}

    def execute(self, step: Step) -> dict[str, Any]:
        options = step.options
        hold = int(float(options.get("hold", DEFAULT_HOLD_MS)))
        if step.name == "move":
            x, y = resolve_point(step.args, self.points)
            self.client.call("pointer", x=x, y=y)
        elif step.name in ("click", "rclick"):
            x, y = resolve_point(step.args, self.points)
            return self.click(x, y, "right" if step.name == "rclick" else "left", hold)
        elif step.name == "key":
            alt, vk = key_chord(step.args[0])
            self.client.call("key", vk=vk, hold_ms=hold, **({"alt": True} if alt else {}))
        elif step.name == "wheel":
            notches = int(step.args[1]) if len(step.args) > 1 else 1
            self.client.call("wheel", notches=notches if step.args[0].lower() == "up" else -notches)
        elif step.name == "wait":
            self.sleep(float(step.args[0]))
        elif step.name == "settle":
            extra = {}
            if "quiet" in options:
                extra["quiet_ms"] = int(float(options["quiet"]) * 1000)
            if "threshold" in options:
                extra["threshold"] = float(options["threshold"])
            return self.settle(float(options.get("timeout", DEFAULT_SETTLE_S)), **extra)
        elif step.name == "advance":
            pages = int(step.args[0]) if step.args else 1
            x, y = resolve_point((self.advance_point,), self.points)
            settled = True
            for _ in range(pages):
                self.click(x, y)
                settled = self.settle(float(options.get("timeout", DEFAULT_SETTLE_S)))["settled"] and settled
            return {"pages": pages, "settled": settled, "location": self.location()}
        elif step.name == "shot":
            name = step.args[0] if step.args else datetime.now().strftime("shot-%H%M%S-%f")[:-3]
            path = (self.shots / (name if name.lower().endswith(".png") else f"{name}.png")).resolve()
            path.parent.mkdir(parents=True, exist_ok=True)
            result = self.client.call("shot", path=str(path))
            return {"path": result.get("path", str(path)), "size": [result.get("width"), result.get("height")],
                    "location": self.location()}
        elif step.name == "state":
            return self.state()
        elif step.name == "expect":
            state = self.state()
            if not condition_matches(state, condition_of(step)):
                raise DriveError(f"expected {step.text.split(None, 1)[1]}, the VM is at {describe_stack(state)}")
            return {"location": state.get("location")}
        elif step.name == "wait-state":
            timeout = float(options.get("timeout", DEFAULT_WAIT_S))
            arguments: dict[str, Any] = {"until": condition_of(step), "timeout_ms": int(timeout * 1000)}
            if "click" in options:
                x, y = resolve_point((options["click"],), self.points)
                every = float(options.get("every", DEFAULT_POKE_S))
                arguments["poke"] = {"x": x, "y": y, "every_ms": int(every * 1000)}
            result = self.client.call("wait", timeout=timeout + REPLY_MARGIN_S, **arguments)
            return {"location": result.get("location"),
                    **({"pokes": result["pokes"]} if "pokes" in result else {})}
        elif step.name == "point":
            self.points[step.args[0]] = _coordinates(step.args[1:])
        elif step.name == "speed":
            self.client.call("speed", factor=float(step.args[0]))
        elif step.name == "rand-seed":
            return self.client.call("rand-seed", seed=rand_seed(step.args[0]))
        elif step.name == "dialogs":
            return self.client.call("dialogs")
        elif step.name == "answer":
            return self.client.call("answer", button=step.args[0])
        elif step.name == "close-request":
            self.client.call("close-request")
        elif step.name == "window":
            result = self.client.call("window", state=step.args[0].lower())
            if self.on_window is not None:
                self.on_window(result)
            return result
        elif step.name == "menu":
            return self.client.call("menu", command=menu_command(step.args[0]))
        elif step.name == "option":
            name, value = option_value(*step.args)
            return self.client.call("option", name=name, value=value)
        elif step.name == "options":
            return self.client.call("options")
        elif step.name == "close":
            timeout = float(options.get("timeout", 20))
            result = self.client.call("close", timeout=timeout + REPLY_MARGIN_S, timeout_ms=int(timeout * 1000))
            self.closed = True
            if self.on_close is not None:
                result = {**result, **self.on_close(result)}
            return result
        return {}

    def run(self, steps: Iterable[Step], report: Callable[[dict[str, Any]], None]) -> bool:
        """Run steps in order, reporting one result per step; stop at the first failure or after close."""
        for step in steps:
            started = self.clock()
            try:
                result = {"ok": True, **self.execute(step)}
            except DriveError as error:
                result = {"ok": False, "error": str(error)}
            events = self.client.take_events()
            result.update(step=step.text, seconds=round(self.clock() - started, 2))
            if events:
                result["events"] = events
            report(result)
            if not result["ok"]:
                return False
            if self.closed:
                break
        return True


def describe_event(event: dict[str, Any]) -> str:
    fields = {k: v for k, v in event.items() if k not in ("t", "kind")}
    return f"event {event.get('kind')}: {json.dumps(fields, ensure_ascii=False)}"


def describe(result: dict[str, Any]) -> str:
    mark = "ok  " if result.get("ok") else "FAIL"
    details = []
    for key in ("error", "path", "location", "settled", "pokes", "seconds", "answered", "exit_code",
                "cursor_moved_to", "label", "checked", "seed", "previous_seed", "draws", "option", "value",
                "section"):
        if key in result and result[key] is not None:
            details.append(f"{key}={result[key]}")
    if "frames" in result:
        details.append("frames=" + describe_stack(result))
    if "dialogs" in result:
        details.append(f"dialogs={json.dumps(result['dialogs'], ensure_ascii=False)}")
    if result.get("sites"):
        details.append("sites=" + " ".join(f"{site}x{count}" for site, count in result["sites"].items()))
    if "options" in result:
        details.append("options=" + " ".join(f"{entry['name']}={entry['value']}" for entry in result["options"]))
    lines = [f"{mark} {result.get('step', '')}  " + "  ".join(details)]
    lines += ["  " + describe_event(event) for event in result.get("events", [])]
    return "\n".join(lines)


# --------------------------------------------------------------------------------------------- Windows

class Windows:
    """The process and window calls both launchers need (Windows only; created lazily by :func:`windows`)."""

    def __init__(self) -> None:
        import ctypes
        from ctypes import wintypes
        self.ctypes, self.wintypes = ctypes, wintypes
        self.user32 = user32 = ctypes.WinDLL("user32", use_last_error=True)
        self.kernel32 = kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        user32.SetProcessDPIAware()
        self.WNDENUMPROC = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
        user32.EnumWindows.argtypes = [self.WNDENUMPROC, wintypes.LPARAM]
        user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
        user32.GetClientRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
        user32.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
        user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
        user32.GetWindowTextLengthW.argtypes = [wintypes.HWND]
        user32.IsWindowVisible.argtypes = [wintypes.HWND]
        user32.IsIconic.argtypes = [wintypes.HWND]
        user32.SetWindowPos.argtypes = [wintypes.HWND, wintypes.HWND, ctypes.c_int, ctypes.c_int, ctypes.c_int,
                                        ctypes.c_int, wintypes.UINT]
        kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        kernel32.OpenProcess.restype = wintypes.HANDLE
        kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
        kernel32.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
        kernel32.WaitForSingleObject.restype = wintypes.DWORD
        kernel32.GetExitCodeProcess.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
        kernel32.TerminateProcess.argtypes = [wintypes.HANDLE, wintypes.UINT]

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
        sizes = {hwnd: self.client_size(hwnd)[0] * self.client_size(hwnd)[1] for hwnd in candidates}
        return max((hwnd for hwnd in candidates if sizes[hwnd] > 0), key=sizes.get, default=None)

    def iconic(self, hwnd: int) -> bool:
        """Minimized."""
        return bool(self.user32.IsIconic(hwnd))

    def send_to_bottom(self, hwnd: int) -> None:
        """Bottom of the z-order without activating it, so a run does not cover the user's work."""
        self.user32.SetWindowPos(hwnd, 1, 0, 0, 0, 0, 0x0013)   # HWND_BOTTOM; SWP_NOSIZE|NOMOVE|NOACTIVATE

    # processes -------------------------------------------------------------------------------------
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
        return pid > 0 and self.wait_exit(pid, 0) is None

    def terminate(self, pid: int) -> None:
        process = self.kernel32.OpenProcess(0x0001, False, pid)
        if process:
            self.kernel32.TerminateProcess(process, 1)
            self.kernel32.CloseHandle(process)


_WINDOWS: Windows | None = None


def windows() -> Windows:
    global _WINDOWS
    if _WINDOWS is None:
        if sys.platform != "win32":
            raise DriveError("driving the games needs Windows")
        _WINDOWS = Windows()
    return _WINDOWS


def send_windows_to_bottom(pid: int, timeout: float = 10.0) -> int:
    """Push pid's visible top-level windows to the bottom once they exist. Returns how many were moved."""
    deadline = time.monotonic() + timeout
    while True:
        found = windows().top_windows(pid)
        if found or time.monotonic() >= deadline:
            for hwnd in found:
                windows().send_to_bottom(hwnd)
            return len(found)
        time.sleep(0.25)
