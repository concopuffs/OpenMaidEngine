#!/usr/bin/env python3
"""The native AGE target of the driving protocol: a daemon that owns a Frida-hooked AGE.EXE.

``drive.py native start`` runs this file's ``serve`` command detached. The daemon spawns AGE suspended, installs
the agent (``tools/drive/native-agent.js``), and only then resumes it, so every hook is in place before AGE's first
instruction. AGE runs in a kill-on-close job owned by the daemon, so it never outlives the hooks. The daemon then
serves the shared protocol (JSON lines over 127.0.0.1, token in ``hello``; spec §4) until AGE exits:

    state pointer click key wheel wait settle shot close     (common)
    dialogs answer detach                                    (native capabilities)

Inside the driven AGE only:
- GetAsyncKeyState, GetCursorPos and SetCursorPos use the driver's keys and cursor. The desktop's keyboard,
  mouse and joystick no longer reach the game, and AGE cannot move the desktop cursor.
- MessageBoxA follows the profile's dialog policy (the registration box on copied installs is answered Yes);
  other boxes are shown and reported, and ``answer`` presses one.
- SHGetFolderPathA (save root, SYS4REG.INI, F1 screenshots, debug report) points into a sandbox below
  ``build/games/<profile>/drive/native/sandbox/``. ``--live-saves`` uses the real folders after a backup.

Usage, steps and the port target: docs/tools-reference.md ("Game driver").
"""
from __future__ import annotations

import argparse
import hmac
import json
import os
import secrets
import shutil
import socket
import subprocess
import sys
import threading
import time
import tomllib
from pathlib import Path
from typing import Any, Callable

sys.path.insert(0, str(Path(__file__).resolve().parent))
import paths  # noqa: E402
import drive_core as core  # noqa: E402
from drive_core import DriveError  # noqa: E402

AGENT = core.DATA / "native-agent.js"
ENGINE_CTX_TOML = paths.VM_MAP / "engine-ctx.toml"
CAPABILITIES = ("dialogs", "answer", "window", "detach")
QUIET_EVENTS = {"folder", "request"}       # logged, but not sent to clients


def scratch_exe(context: paths.ToolContext = paths.CONTEXT) -> Path:
    return context.game_build / "native-scratch" / "AGE.EXE"


# --------------------------------------------------------------------------------------------- agent config

ENGINE_FIELDS = ("cur_ctx_index", "frame_codebase", "frame_pc", "frame_script_resource_id")
POLLING_GATE_FIELD = "input_polling_enabled"


def engine_ctx_offsets(toml_text: str, names: tuple[str, ...] = ENGINE_FIELDS) -> dict[str, int]:
    """Offsets of named fields in vm-map/engine-ctx.toml (the one home of EngineCtx offsets)."""
    fields = {entry["name"]: entry["offset"] for entry in tomllib.loads(toml_text).get("field", [])}
    missing = [name for name in names if name not in fields]
    if missing:
        raise DriveError(f"engine-ctx.toml lacks {', '.join(missing)}")
    return {name: fields[name] for name in names}


def agent_config(profile: core.Profile, folders: dict[int, Path] | None, engine_ctx_text: str | None) -> dict[str, Any]:
    if folders is not None:
        for path in folders.values():
            text = str(path)
            if not text.isascii() or len(text) > 180:
                raise DriveError(f"sandbox path must be ASCII and short enough for AGE's buffers: {text}")
    engine = keep_polling = None
    if profile.engine is not None:
        if engine_ctx_text is None:
            raise DriveError("the profile reads engine state but vm-map/engine-ctx.toml is missing")
        engine = {**profile.engine, **engine_ctx_offsets(engine_ctx_text)}
        if profile.keep_polling is not None:
            gate = engine_ctx_offsets(engine_ctx_text, (POLLING_GATE_FIELD,))[POLLING_GATE_FIELD]
            keep_polling = {**profile.keep_polling, "gate": gate}
    return {
        "dialogs": [{"match": list(policy.match.encode("cp932")), "answer": policy.code} for policy in profile.dialogs],
        "folders": None if folders is None else {str(csidl): str(path) for csidl, path in folders.items()},
        "engine": engine,
        "keep_polling": keep_polling,
    }


# --------------------------------------------------------------------------------------------- VM state

def append_record_names(game_root: Path) -> dict[str, str]:
    """Packed id -> record name for every ``*.AAI`` append catalog beside AGE.EXE (the install's own and any a mod
    export added), so frames in append scripts read ``$2$SCENE`` as on the port, not ``#0x2000001``."""
    import parse_sys4ini
    names: dict[str, str] = {}
    for catalog in sorted(game_root.glob("*.AAI")) + sorted(game_root.glob("*.aai")):
        try:
            selector = int.from_bytes(catalog.read_bytes()[0x108:0x10C], "little")
            for entry in parse_sys4ini.parse(catalog)["files"]:
                names[str((selector << 24) | entry["raw_index"])] = entry["name"]
        except (OSError, ValueError, KeyError, SystemExit):
            continue
    return names


def describe_frames(raw: dict[str, Any] | None, names: dict[str, str]) -> dict[str, Any]:
    """Turn the agent's raw frame reads into the protocol's state. offset = (pc - codebase) >> 2, the instruction
    offset in dwords that the port and the disassembly use. Native AGE keeps every caller's pc on its call-script
    instruction and the innermost frame's past its wait; the port reports the same convention."""
    if raw is None:
        raise DriveError("this profile has no [native.engine] section, so the VM position is unknown")
    if "error" in raw:
        raise DriveError(f"reading the VM position failed: {raw['error']}")
    frames = []
    for frame in raw["frames"]:
        resid, codebase, pc = frame["resid"], frame["codebase"], frame["pc"]
        name = names.get(str(resid))
        script = f"#{resid:#x}" if name is None else core.normalize_script(name)
        offset = (pc - codebase) >> 2 if codebase and pc >= codebase else None
        frames.append({"script": script, "offset": offset,
                       "location": f"{script}@{offset:#x}" if offset is not None else f"{script}@?",
                       "index": frame["index"], "resid": resid})
    return {"frames": frames, "location": frames[-1]["location"] if frames else None, "waiting": None,
            "ctx": raw["ctx"], "current": raw["current"]}


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

class Win32(core.Windows):
    """Adds message boxes, posted input, client capture and the kill-on-close job to the shared calls."""

    def __init__(self) -> None:
        super().__init__()
        ctypes, wintypes = self.ctypes, self.wintypes
        user32, kernel32 = self.user32, self.kernel32
        self.gdi32 = gdi32 = ctypes.WinDLL("gdi32", use_last_error=True)
        user32.EnumChildWindows.argtypes = [wintypes.HWND, self.WNDENUMPROC, wintypes.LPARAM]
        user32.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
        user32.IsWindowEnabled.argtypes = [wintypes.HWND]
        user32.PrintWindow.argtypes = [wintypes.HWND, wintypes.HDC, wintypes.UINT]
        user32.GetDC.argtypes = [wintypes.HWND]
        user32.GetDC.restype = wintypes.HDC
        user32.ReleaseDC.argtypes = [wintypes.HWND, wintypes.HDC]
        user32.ClientToScreen.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.POINT)]
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
        kernel32.CreateJobObjectW.argtypes = [ctypes.c_void_p, wintypes.LPCWSTR]
        kernel32.CreateJobObjectW.restype = wintypes.HANDLE
        kernel32.SetInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD]
        kernel32.AssignProcessToJobObject.argtypes = [wintypes.HANDLE, wintypes.HANDLE]

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

    def post_input(self, hwnd: int, message: int, wparam: int, lparam: int) -> None:
        """Post a mouse or keyboard message as hardware input arrives: not while the window is disabled. A modal
        message box disables its owner, and AGE's window procedure must not see input from inside the box's loop
        (a right-button-up posted under the Depth error box crashed AGE with 0xC000041D)."""
        if self.user32.IsWindowEnabled(hwnd):
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


WM_MOUSEMOVE, WM_LBUTTONDOWN, WM_LBUTTONUP, WM_RBUTTONDOWN, WM_RBUTTONUP = 0x200, 0x201, 0x202, 0x204, 0x205
WM_KEYDOWN, WM_KEYUP, WM_MOUSEWHEEL, WM_CLOSE, BM_CLICK = 0x100, 0x101, 0x20A, 0x10, 0xF5
WM_SYSCOMMAND, SC_MINIMIZE, SC_RESTORE = 0x112, 0xF020, 0xF120


# --------------------------------------------------------------------------------------------- requests

def _int(request: dict[str, Any], name: str, default: int | None = None) -> int:
    value = request.get(name, default)
    if isinstance(value, bool) or not isinstance(value, (int, float)) or value != int(value):
        raise DriveError(f"'{request.get('op')}' needs an integer '{name}'")
    return int(value)


def _number(request: dict[str, Any], name: str, default: float) -> float:
    value = request.get(name, default)
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise DriveError(f"'{request.get('op')}' needs a number '{name}'")
    return float(value)


def native_condition(value: Any) -> dict[str, str]:
    """Validate a protocol condition for native AGE, which has no waiting state (spec §11)."""
    if not isinstance(value, dict) or not value:
        raise DriveError("a condition is an object with at, top and/or script")
    for key, text in value.items():
        if key == "waiting":
            raise DriveError("'waiting' is not supported by the native target")
        if key not in core.CONDITION_KEYS or not isinstance(text, str):
            raise DriveError(f"unknown condition {key!r}")
        if key in ("at", "top"):
            core.parse_location(text)
    return value


# --------------------------------------------------------------------------------------------- daemon

class Daemon:
    """Owns AGE (spawned suspended, hooked, resumed), the Frida session, and the protocol listener."""

    def __init__(self, arguments: argparse.Namespace) -> None:
        self.arguments = arguments
        self.context = paths.CONTEXT
        self.root = core.drive_root("native")
        self.events_path = self.root / "events.jsonl"
        self.profile = core.load_profile()
        names_path = self.context.game_build / "callscript-names.json"
        self.names = json.loads(names_path.read_text(encoding="utf-8")) if names_path.is_file() else {}
        self.token = os.environ.get(core.TOKEN_ENVIRONMENT) or secrets.token_hex(16)
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
        self.server: socket.socket | None = None
        self.connection: socket.socket | None = None
        self.answering = False                  # a request is running: its reply must still go out

    # events ----------------------------------------------------------------------------------------
    def event(self, kind: str, **fields: Any) -> None:
        record = {"t": core.now_text(), "kind": kind, **fields}
        with self.lock:
            if kind not in QUIET_EVENTS:
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
        live = core.live_root()
        if arguments.attach is not None or arguments.live_saves:
            self.saves = {"mode": "live", "root": str(live)}
            if live.is_dir():
                backup, created = core.backup_tree(live, self.root / "backups")
                self.saves["backup"] = str(backup)
                print(f"drive_native: live saves {'backed up to' if created else 'match backup'} {backup}", flush=True)
            return None
        sandbox, profile_root = core.prepare_sandbox(
            "native", arguments.sandbox or "fresh", arguments.seed, arguments.sound, self.profile,
            log=lambda message: self.event("sandbox", detail=message))
        self.saves = {"mode": "sandbox", "root": str(sandbox), "profile": str(profile_root)}
        self.live_root = live
        self.live_before = core.snapshot_tree(live)
        return core.sandbox_folders(sandbox)

    def verify_live_untouched(self) -> None:
        if self.live_before is None:
            return
        changes = core.diff_snapshots(self.live_before, core.snapshot_tree(self.live_root))
        if any(changes.values()):
            self.event("live-saves-changed", **changes)
        else:
            self.event("live-saves-unchanged", root=str(self.live_root))

    # AGE -------------------------------------------------------------------------------------------
    def launch(self, folders: dict[int, Path] | None) -> None:
        import frida
        device = frida.get_local_device()
        engine_ctx = ENGINE_CTX_TOML.read_text(encoding="utf-8") if ENGINE_CTX_TOML.is_file() else None
        config = agent_config(self.profile, folders, engine_ctx)
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
        self.names = {**self.names, **append_record_names(exe.parent)}
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
                    # Polled input (menus, the cursor) must keep working while the window is inactive.
                    kept = self.api.keeppolling()
                    if not self.arguments.foreground:
                        win32().send_to_bottom(hwnd)
                    self.event("window", hwnd=hex(hwnd), title=win32().text(hwnd), size=[width, height],
                               keep_polling=kept is not None)
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
                self.answered.add(box["hwnd"])
                self.event("dialog-open", text=box["text"], title=box["title"],
                           buttons=[text for _, text in box["buttons"]])
            time.sleep(0.25)

    def watch_exit(self) -> None:
        self.exit_code = win32().wait_exit(self.age_pid, None)
        self.event("age-exited", code=self.exit_code)
        self.closing = True
        self.wake()

    def wake(self) -> None:
        """Unblock the accept loop, and an idle client, once the session is over. A running request finishes and
        sends its reply first (close returns AGE's exit code just as AGE exits)."""
        for sock in (None if self.answering else self.connection, self.server):
            if sock is None:
                continue
            try:
                sock.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass
            try:
                sock.close()
            except OSError:
                pass

    # primitives ------------------------------------------------------------------------------------
    def require_window(self) -> int:
        if self.closing:
            raise DriveError("AGE has exited")
        if self.hwnd is None:
            raise DriveError("AGE has no main window yet (a dialog may be open: try `dialogs`)")
        return self.hwnd

    def take_warps(self) -> list[int] | None:
        """Where AGE moved the cursor itself since the last look (e.g. onto a confirmation's default button)."""
        try:
            warps = self.api.warps()
        except Exception:
            return None
        if warps["count"] and warps["last"]:
            self.cursor = tuple(warps["last"])
            return list(warps["last"])
        return None

    def move(self, x: int, y: int) -> None:
        hwnd = self.require_window()
        self.api.setcursor(x, y)
        self.cursor = (x, y)
        win32().post_input(hwnd, WM_MOUSEMOVE, 0, lparam(x, y))

    def click(self, x: int, y: int, right: bool, hold: float) -> None:
        hwnd = self.require_window()
        self.move(x, y)
        time.sleep(0.12)
        vk, down, up = (2, WM_RBUTTONDOWN, WM_RBUTTONUP) if right else (1, WM_LBUTTONDOWN, WM_LBUTTONUP)
        # Menus poll the button (GetAsyncKeyState); the movie skip and CLICKONUP consume the message latch.
        self.api.setkey(vk, True)
        win32().post_input(hwnd, down, vk, lparam(x, y))
        time.sleep(hold)
        self.api.setkey(vk, False)
        win32().post_input(hwnd, up, 0, lparam(x, y))
        time.sleep(0.05)

    def key(self, vk: int, hold: float) -> None:
        hwnd = self.require_window()
        scan = win32().scan_code(vk)
        self.api.setkey(vk, True)
        win32().post_input(hwnd, WM_KEYDOWN, vk, 1 | (scan << 16))
        time.sleep(hold)
        self.api.setkey(vk, False)
        win32().post_input(hwnd, WM_KEYUP, vk, 1 | (scan << 16) | 0xC0000000)
        time.sleep(0.05)

    def capture(self) -> Any:
        return win32().capture(self.require_window())

    def state(self) -> dict[str, Any]:
        return describe_frames(self.api.state(), self.names)

    def try_location(self) -> str | None:
        try:
            return self.state()["location"]
        except Exception:
            return None

    def static_info(self) -> dict[str, Any]:
        return {"target": "native", "profile": self.context.profile_id, "version": core.PROTOCOL_VERSION,
                "capabilities": list(CAPABILITIES), "game_pid": self.age_pid,
                "mode": "attach" if self.arguments.attach is not None else "spawn", "saves": self.saves}

    # ops -------------------------------------------------------------------------------------------
    def op_hello(self, request: dict[str, Any]) -> dict[str, Any]:
        try:
            hooked = self.api.hooked()
        except Exception as error:
            hooked = [f"agent unavailable: {error}"]
        try:
            kept = self.api.keeppolling() if self.hwnd else None
        except Exception:
            kept = None
        return {**self.static_info(), "window": hex(self.hwnd) if self.hwnd else None, "hooked": hooked,
                **({"activations_ignored": kept} if kept is not None else {})}

    def op_state(self, request: dict[str, Any]) -> dict[str, Any]:
        return self.state()

    def op_pointer(self, request: dict[str, Any]) -> dict[str, Any]:
        self.move(_int(request, "x"), _int(request, "y"))
        return {}

    def op_click(self, request: dict[str, Any]) -> dict[str, Any]:
        x, y = _int(request, "x"), _int(request, "y")
        button = request.get("button", "left")
        if button not in ("left", "right"):
            raise DriveError(f"button must be left or right, got {button!r}")
        hold = _int(request, "hold_ms", core.DEFAULT_HOLD_MS)
        if hold < 0:
            raise DriveError("hold_ms must not be negative")
        self.take_warps()
        self.click(x, y, button == "right", hold / 1000)
        warp = self.take_warps()
        return {"x": x, "y": y, **({"cursor_moved_to": warp} if warp else {})}

    def op_key(self, request: dict[str, Any]) -> dict[str, Any]:
        vk = _int(request, "vk")
        if not 0 < vk < 0xFF:
            raise DriveError(f"vk {vk:#x} is not a key")
        if vk in core.REFUSED_KEYS:
            raise DriveError(core.REFUSED_KEYS[vk])
        self.key(vk, _int(request, "hold_ms", core.DEFAULT_HOLD_MS) / 1000)
        return {}

    def op_wheel(self, request: dict[str, Any]) -> dict[str, Any]:
        notches = _int(request, "notches")
        self.take_warps()
        if "x" in request or "y" in request:
            self.move(_int(request, "x"), _int(request, "y"))
        hwnd = self.require_window()
        x, y = win32().client_to_screen(hwnd, *self.cursor)
        for _ in range(abs(notches)):
            win32().post_input(hwnd, WM_MOUSEWHEEL, wheel_wparam(1 if notches > 0 else -1), lparam(x, y))
            time.sleep(0.08)
        return {}

    def op_wait(self, request: dict[str, Any]) -> dict[str, Any]:
        condition = native_condition(request.get("until"))
        timeout_ms = _int(request, "timeout_ms", int(core.DEFAULT_WAIT_S * 1000))
        poke = request.get("poke")
        if poke is not None:
            poke = {"x": _int(poke, "x"), "y": _int(poke, "y"),
                    "every_ms": _int(poke, "every_ms", int(core.DEFAULT_POKE_S * 1000))}
            if poke["every_ms"] <= 0:
                raise DriveError("poke every_ms must be positive")
        started = time.monotonic()
        next_poke, pokes = started, 0
        while True:
            try:
                state = self.state()
            except DriveError as error:            # e.g. a frame read while AGE switches scripts
                state = {"frames": [], "location": None, "error": str(error)}
            elapsed = time.monotonic() - started
            if state["frames"] and core.condition_matches(state, condition):
                return {**state, "elapsed_ms": int(elapsed * 1000), **({"pokes": pokes} if poke else {})}
            if elapsed * 1000 >= timeout_ms:
                raise DriveError(f"timed out after {timeout_ms} ms; now at {core.describe_stack(state)}")
            if self.closing:
                raise DriveError("AGE exited while waiting")
            if poke is not None and time.monotonic() >= next_poke:
                self.click(poke["x"], poke["y"], right=False, hold=core.DEFAULT_HOLD_MS / 1000)
                pokes += 1
                next_poke = time.monotonic() + poke["every_ms"] / 1000
            time.sleep(0.1)

    def op_settle(self, request: dict[str, Any]) -> dict[str, Any]:
        settled, elapsed, _ = settle(self.capture, threshold=_number(request, "threshold", 0.004),
                                     quiet=_int(request, "quiet_ms", 700) / 1000,
                                     timeout=_int(request, "timeout_ms", int(core.DEFAULT_SETTLE_S * 1000)) / 1000)
        return {"settled": settled, "elapsed_ms": int(elapsed * 1000), "location": self.try_location()}

    def op_shot(self, request: dict[str, Any]) -> dict[str, Any]:
        path = request.get("path")
        if not isinstance(path, str) or not Path(path).is_absolute():
            raise DriveError(f"shot needs an absolute path, got {path!r}")
        image = self.capture()
        Path(path).parent.mkdir(parents=True, exist_ok=True)
        image.save(path)
        return {"path": path, "width": image.size[0], "height": image.size[1]}

    def op_dialogs(self, request: dict[str, Any]) -> dict[str, Any]:
        return {"dialogs": [{"title": box["title"], "text": box["text"],
                             "buttons": [text for _, text in box["buttons"]]}
                            for box in win32().dialogs(self.age_pid)]}

    def op_answer(self, request: dict[str, Any]) -> dict[str, Any]:
        button = request.get("button")
        if not isinstance(button, str):
            raise DriveError("'answer' needs a string 'button'")
        wanted = button.replace("&", "").lower()
        for box in win32().dialogs(self.age_pid):
            for handle, text in box["buttons"]:
                if text.replace("&", "").lower() == wanted:
                    win32().post(handle, BM_CLICK, 0, 0)
                    self.event("dialog", text=box["text"], answered=wanted, via="answer")
                    return {"text": box["text"], "answered": text}
        raise DriveError(f"no open message box has a {button!r} button")

    def op_close(self, request: dict[str, Any]) -> dict[str, Any]:
        if self.arguments.attach is not None:
            raise DriveError("this session attached to a running AGE; `detach` ends it and leaves AGE running")
        hwnd = self.require_window()
        self.closing = True
        win32().post(hwnd, WM_CLOSE, 1, 0)
        code = win32().wait_exit(self.age_pid, _int(request, "timeout_ms", 20_000) / 1000)
        if code is None:
            win32().terminate(self.age_pid)
            raise DriveError("AGE did not exit after WM_CLOSE; terminated it")
        return {"exit_code": code}

    def op_close_request(self, request: dict[str, Any]) -> dict[str, Any]:
        """An unforced WM_CLOSE (wParam 0), as the close button and ESC post: AGE opens its 終了 dialog."""
        win32().post(self.require_window(), WM_CLOSE, 0, 0)
        return {}

    def op_window(self, request: dict[str, Any]) -> dict[str, Any]:
        """Minimize or restore AGE's window as its title-bar and taskbar buttons do. AGE's window procedure sets
        run-state 0x1000000 on SC_MINIMIZE and clears it on SC_RESTORE; a close while it is set quits at once."""
        state = request.get("state")
        if state not in ("minimize", "restore"):
            raise DriveError("window state must be minimize or restore")
        hwnd = self.require_window()
        minimize = state == "minimize"
        win32().post(hwnd, WM_SYSCOMMAND, SC_MINIMIZE if minimize else SC_RESTORE, 0)
        deadline = time.monotonic() + 5
        while win32().iconic(hwnd) != minimize and time.monotonic() < deadline:
            time.sleep(0.05)
        if not minimize and not self.arguments.foreground:
            win32().send_to_bottom(hwnd)    # a restore activates the window
        return {"minimized": win32().iconic(hwnd)}

    def op_detach(self, request: dict[str, Any]) -> dict[str, Any]:
        self.closing = True
        return {"left_running": self.arguments.attach is not None}

    def handle(self, request: dict[str, Any]) -> dict[str, Any]:
        op = request.get("op")
        name = op.replace("-", "_") if isinstance(op, str) else None   # wire ops may hyphenate (close-request)
        handler = getattr(self, f"op_{name}", None) if name is not None and name.isidentifier() else None
        if handler is None:
            raise DriveError(f"unknown op {op!r} (the native target supports state, pointer, click, key, wheel, "
                             f"wait, settle, shot, close, close-request, {', '.join(CAPABILITIES)})")
        return handler(request)

    # protocol --------------------------------------------------------------------------------------
    def serve(self) -> None:
        while not self.closing:
            try:
                connection, _ = self.server.accept()
            except OSError:
                if self.closing:
                    break
                continue
            self.connection = connection
            try:
                self.converse(connection)
            except OSError:
                pass
            finally:
                self.connection = None
                try:
                    connection.close()
                except OSError:
                    pass

    def converse(self, connection: socket.socket) -> None:
        connection.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        stream = connection.makefile("rwb")

        def send(reply: dict[str, Any]) -> None:
            events = self.take_events()
            if events:
                reply["events"] = events
            stream.write((json.dumps(reply, ensure_ascii=False) + "\n").encode("utf-8"))
            stream.flush()

        try:
            hello = json.loads(stream.readline().decode("utf-8") or "null")
        except ValueError:
            hello = None
        if not isinstance(hello, dict) or hello.get("op") != "hello" or not hmac.compare_digest(
                str(hello.get("token", "")).encode("utf-8"), self.token.encode("utf-8")):
            send({"id": hello.get("id", 0) if isinstance(hello, dict) else 0, "ok": False,
                  "error": "the first request must be hello with the session token"})
            return
        send({"id": hello.get("id", 0), "ok": True, "result": self.op_hello(hello)})
        while not self.closing:
            line = stream.readline()
            if not line:
                return
            if not line.strip():
                continue
            started = time.monotonic()
            try:
                request = json.loads(line.decode("utf-8"))
                if not isinstance(request, dict) or not isinstance(request.get("op"), str):
                    raise DriveError("a request is a JSON object with a string 'op'")
            except (ValueError, DriveError) as error:
                send({"id": 0, "ok": False, "error": f"invalid request: {error}"})
                continue
            identifier = request.get("id", 0)
            self.answering = True
            try:
                reply = {"id": identifier, "ok": True, "result": self.handle(request)}
            except DriveError as error:
                reply = {"id": identifier, "ok": False, "error": str(error)}
            except Exception as error:   # a bug or a dead session: report it, keep the daemon alive
                import traceback
                traceback.print_exc()
                reply = {"id": identifier, "ok": False, "error": f"{type(error).__name__}: {error}"}
            self.event("request", op=request["op"], ok=reply["ok"], seconds=round(time.monotonic() - started, 2),
                       **({} if reply["ok"] else {"error": reply["error"]}))
            try:
                send(reply)
            finally:
                self.answering = False

    # main ------------------------------------------------------------------------------------------
    def run(self) -> int:
        self.root.mkdir(parents=True, exist_ok=True)
        self.events_path.write_text("", encoding="utf-8")       # one session per events file
        session = core.session_file("native")
        self.server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.server.bind(("127.0.0.1", 0))
        self.server.listen(1)
        try:
            folders = self.prepare_saves()
            self.launch(folders)
            threading.Thread(target=self.watch_window, daemon=True).start()
            threading.Thread(target=self.watch_exit, daemon=True).start()
            core.write_json_atomic(session, {
                **self.static_info(), "pid": os.getpid(), "port": self.server.getsockname()[1], "token": self.token,
                "started": core.now_text(), "log": str(self.root / "serve.log"), "events": str(self.events_path),
            })
            self.serve()
        except Exception as error:
            self.event("error", message=f"{type(error).__name__}: {error}")
            if not isinstance(error, DriveError):
                import traceback
                traceback.print_exc()
            print(f"drive_native: {error}", file=sys.stderr, flush=True)
            return 1
        finally:
            self.closing = True
            self.wake()
            try:
                if session.is_file() and json.loads(session.read_text(encoding="utf-8")).get("pid") == os.getpid():
                    session.unlink()
            except (OSError, ValueError):
                pass
            self.shutdown()
        return 0

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
        summary = {"ended": core.now_text(), "age_exit_code": self.exit_code, "saves": self.saves}
        core.write_json_atomic(self.root / "last-session.json", summary)


# --------------------------------------------------------------------------------------------- launcher

SERVE_FLAGS = ("exe", "sandbox", "seed", "attach")
SERVE_SWITCHES = ("live_saves", "sound", "foreground")


def add_launch_options(command: argparse.ArgumentParser) -> None:
    command.add_argument("--exe", help="AGE.EXE to spawn (default: the native_scratch.py copy)")
    saves = command.add_mutually_exclusive_group()
    saves.add_argument("--sandbox", help="persistent named save sandbox (default: 'fresh', emptied every start)")
    saves.add_argument("--live-saves", action="store_true", help="use the real save folders (backed up first)")
    command.add_argument("--seed", help="fill a new sandbox from 'live', 'native:NAME', 'port:NAME' or a folder")
    command.add_argument("--sound", action="store_true",
                         help="keep the sandbox profile's volume (default: the profile's [sandbox.quiet] settings)")
    command.add_argument("--attach", nargs="?", const="auto",
                         help="hook an AGE that is already running (pid or auto); its saves stay live")
    command.add_argument("--foreground", action="store_true",
                         help="leave AGE's window where Windows puts it (default: the bottom of the z-order)")


def check_launch_options(arguments: argparse.Namespace) -> None:
    if arguments.seed and (arguments.live_saves or arguments.attach):
        raise DriveError("--seed fills a sandbox; it cannot be combined with live saves")


def start(arguments: argparse.Namespace) -> int:
    """`drive.py native start`: run the daemon detached and return once AGE's window (or a message box) is up."""
    check_launch_options(arguments)
    root = core.drive_root("native")
    root.mkdir(parents=True, exist_ok=True)
    if core.read_session("native") is not None:
        raise DriveError("a native session is already running; `drive.py native stop` first")
    forwarded = ["serve"]
    for flag in SERVE_FLAGS:
        value = getattr(arguments, flag)
        if value is not None:
            forwarded += [f"--{flag}", str(value)]
    forwarded += [f"--{switch.replace('_', '-')}" for switch in SERVE_SWITCHES if getattr(arguments, switch)]
    token = secrets.token_hex(16)
    environment = {**os.environ, **paths.CONTEXT.subprocess_environment(), core.TOKEN_ENVIRONMENT: token}
    if arguments.inline:
        os.environ[core.TOKEN_ENVIRONMENT] = token
        return Daemon(build_parser().parse_args(forwarded)).run()
    log = (root / "serve.log").open("w", encoding="utf-8")
    command = [sys.executable, "-X", "utf8", "-u", str(Path(__file__).resolve()), *forwarded]
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
            raise DriveError(f"the daemon exited ({process.returncode})")
        info = core.read_session("native")
        if info is not None and info["pid"] == process.pid:
            with core.Client(info["port"], token) as client:
                hello = client.hello
                seen += client.take_events()
            dialogs = [event for event in seen if event["kind"] == "dialog-open"]
            if hello.get("window") or dialogs:
                saves = info["saves"]
                print(f"drive native: AGE pid {info['game_pid']} ({info['mode']}), window {hello.get('window')}, "
                      f"saves {saves['mode']}: {saves.get('profile') or saves['root']}")
                print(f"drive native: hooked {', '.join(hello.get('hooked', []))}; log {root / 'serve.log'}")
                for event in dialogs:
                    print(f"drive native: AGE shows a message box: {event['text']!r} {event['buttons']} "
                          f"-> answer with `drive.py native do \"answer BUTTON\"`")
                return 0
        time.sleep(0.3)
    raise DriveError(f"AGE did not open a window within {arguments.timeout:.0f} s; see {root / 'serve.log'}")


def stop(timeout: float) -> int:
    """`drive.py native stop`: close AGE the way its own quit does (or detach from an attached one)."""
    info = core.read_session("native")
    if info is None:
        print("drive native: no session")
        return 0
    code = 0
    try:
        with core.Client(info["port"], info["token"]) as client:
            if info.get("mode") == "spawn":
                result = client.call("close", timeout=timeout + core.REPLY_MARGIN_S, timeout_ms=int(timeout * 1000))
                print(f"drive native: AGE exited ({result.get('exit_code')})")
            else:
                client.call("detach")
                print("drive native: detached; AGE keeps running")
    except DriveError as error:
        print(f"drive native: {error}", file=sys.stderr)
        code = 1
    if win32().wait_exit(info["pid"], timeout) is None:
        win32().terminate(info["pid"])
    root = core.drive_root("native")
    events = root / "events.jsonl"
    if events.is_file():
        for line in events.read_text(encoding="utf-8").splitlines()[-8:]:
            record = json.loads(line)
            if record["kind"].startswith("live-saves"):
                print(f"drive native: {record['kind']}" + (
                    "" if record["kind"].endswith("unchanged") else f": {json.dumps(record, ensure_ascii=False)}"))
    if (root / "last-session.json").is_file():
        print(f"drive native: session ended; {root / 'last-session.json'}")
    return code


def backup() -> int:
    live = core.live_root()
    if not live.is_dir():
        print(f"drive native: no live saves at {live}")
        return 1
    target, created = core.backup_tree(live, core.drive_root("native") / "backups")
    print(f"drive native: {'backed up' if created else 'already backed up'}: {live} -> {target}")
    return 0


def restore(folder: str) -> int:
    source = core.backup_contents(Path(folder))
    if not (source / "SAVE").is_dir() and not (source / "SYS4REG.INI").is_file():
        raise DriveError(f"{source} does not look like a save folder copy (no SAVE\\ or SYS4REG.INI)")
    if core.read_session("native") is not None:
        raise DriveError("stop the running native session first")
    import frida
    if any(process.name.upper() == "AGE.EXE" for process in frida.get_local_device().enumerate_processes()):
        raise DriveError("close AGE before restoring its saves")
    live = core.live_root()
    if live.is_dir():
        safety, _ = core.backup_tree(live, core.drive_root("native") / "backups")
        print(f"drive native: current live saves kept in {safety}")
        shutil.rmtree(live)
    shutil.copytree(source, live)
    print(f"drive native: restored {source} -> {live} ({len(core.snapshot_tree(live))} files)")
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)
    serve = commands.add_parser("serve", help="the daemon itself (`drive.py native start` runs it detached)")
    add_launch_options(serve)
    return parser


def main(argv: list[str] | None = None) -> int:
    arguments = build_parser().parse_args(argv)
    try:
        check_launch_options(arguments)
        return Daemon(arguments).run()
    except DriveError as error:
        print(f"drive_native: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
