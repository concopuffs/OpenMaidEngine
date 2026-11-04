"""Plain tests for the game driver's platform-independent logic: steps, conditions, profiles, sandboxes, the
protocol client and the step executor against a fake server (no game, Godot, Frida, Pillow or Windows needed)."""
import json
import socket
import struct
import sys
import tempfile
import threading
from pathlib import Path

import drive_core as core
import drive_native as native
import parse_sys4ini
import paths

FAILS = []
HERE = Path(__file__).resolve().parent


def check(condition, message):
    (FAILS.append(message) or print("FAIL:", message)) if not condition else print("ok:", message)


def raises(function, *args, contains=""):
    try:
        function(*args)
    except core.DriveError as error:
        return contains in str(error)
    return False


def test_step_parsing():
    step = core.parse_step('click title.start hold=200')
    check((step.name, step.args, step.options) == ("click", ("title.start",), {"hold": "200"}),
          "a named-point click keeps its option")
    check(core.parse_step("click 0x10 20").args == ("0x10", "20"), "coordinates may be hex")
    check(core.parse_step('answer "&Yes"').args == ("&Yes",), "quoted arguments survive")
    check(core.parse_step("expect top=SC0000@0x8a9").options == {"top": "SC0000@0x8a9"},
          "an option value may contain @ and =-free text")
    check(raises(core.parse_step, "jump 1 2", contains="unknown step"), "unknown steps are rejected")
    check(raises(core.parse_step, "click", contains="takes 1..2"), "argument counts are checked")
    check(raises(core.parse_step, "click 1 2 speed=3", contains="no option"), "unknown options are rejected")
    check(raises(core.parse_step, "settle timeout=soon", contains="not a number"), "numeric options must parse")
    check(raises(core.parse_step, "key esc", contains="WM_CLOSE"), "ESC is refused at parse time")
    check(raises(core.parse_step, "key F1", contains="screenshot"), "F1 is refused at parse time")
    check(raises(core.parse_step, "expect", contains="at least one"), "expect needs a condition")
    check(core.parse_step("expect script=SC0000 waiting=input").options == {"script": "SC0000", "waiting": "input"},
          "conditions combine; every one must match")
    check(raises(core.parse_step, "expect waiting=page", contains="input, menu"), "waiting kinds are checked")
    check(raises(core.parse_step, "wait-state top=SC0000 timeout=5", contains="SCRIPT@OFFSET"),
          "locations need an offset")
    check(raises(core.parse_step, "wait-state script=TITLE every=3", contains="needs click"),
          "every= only makes sense with click=")
    check(raises(core.parse_step, "shot ../escape", contains="plain file names"), "shot names cannot be paths")
    check(raises(core.parse_step, "wheel sideways", contains="up or down"), "wheel direction is checked")
    check(raises(core.parse_step, "wait soon", contains="seconds"), "wait takes seconds")
    check(raises(core.parse_step, "speed 20", contains="between"), "speed factors are bounded like the port's")
    check(core.parse_step("wait-state script=TITLE click=adv.next every=3").options["every"] == "3",
          "wait-state can click while it waits")


def test_step_files():
    steps = core.parse_steps(["# comment", "", "click 1 2   # trailing note", "  settle timeout=3"])
    check([step.text for step in steps] == ["click 1 2", "settle timeout=3"], "comments and blanks are skipped")
    check([step.line for step in steps] == [3, 4], "steps keep their line numbers")
    check(raises(core.parse_steps, ["settle", "bogus"], contains="line 2"), "errors name the line")
    scenarios = sorted((HERE / "drive" / "scenarios").glob("*.steps"))
    check(bool(scenarios), "scenario files exist")
    for path in scenarios:
        try:
            parsed = core.parse_steps(path.read_text(encoding="utf-8").splitlines())
            core.check_capabilities(parsed, core.SHARED_CAPABILITIES, "shared subset")
            portable = True
        except core.DriveError as error:
            print("   ", error)
            portable = False
        check(portable, f"{path.name} parses and needs no target-only capability")


def test_capabilities():
    steps = core.parse_steps(["speed 4", "wait-state waiting=input", "answer Yes", "click 1 2"])
    check(core.step_capabilities(steps[0]) == {"speed"} and core.step_capabilities(steps[1]) == {"waiting"},
          "steps name the capability they need")
    try:
        core.check_capabilities(steps, ["dialogs", "answer"], "native target")
        message = ""
    except core.DriveError as error:
        message = str(error)
    check("line 1: `speed 4` needs speed, which the native target lacks" in message
          and "line 2:" in message and "answer" not in message.split("line 2:")[1],
          "a scenario is checked against the target before it starts, naming each line")
    check(not raises(core.check_capabilities, steps[2:], ["dialogs", "answer"], "native target"),
          "native capabilities cover dialogs and answer")
    check(set(core.SHARED_CAPABILITIES) <= set(native.CAPABILITIES), "shared capabilities are native capabilities")
    port_source = (HERE.parent / "godot" / "Main.Drive.cs").read_text(encoding="utf-8")
    check(all(f'"{name}"' in port_source.split("PortCapabilities =")[1].split(";")[0]
              for name in core.SHARED_CAPABILITIES), "shared capabilities are port capabilities")


def test_keys_and_points():
    check(core.virtual_key("enter") == 0x0D and core.virtual_key("Ctrl") == 0x11, "key names map to VKs")
    check(core.virtual_key("a") == 0x41 and core.virtual_key("7") == 0x37, "letters and digits map to VKs")
    check(core.virtual_key("0x26") == 0x26 and core.virtual_key("f2") == 0x71, "hex VKs and function keys")
    check(raises(core.virtual_key, "0x1b", contains="WM_CLOSE"), "ESC is refused as a hex VK too")
    check(raises(core.virtual_key, "hyper", contains="unknown key"), "unknown key names are rejected")
    points = {"title.start": (400, 302)}
    check(core.resolve_point(("title.start",), points) == (400, 302), "named points resolve")
    check(core.resolve_point(("5", "6"), points) == (5, 6), "coordinates pass through")
    check(raises(core.resolve_point, ("nowhere",), points, contains="title.start"), "unknown points list known ones")
    check(native.lparam(800, 600) == (600 << 16) | 800, "lparam packs y over x")
    check(native.wheel_wparam(-1) == 0xFF880000, "a wheel notch down is -120 in the high word")


def test_locations_and_conditions():
    check(core.parse_location("sc0000.bin@0x8A9") == ("SC0000", 0x8A9), "locations normalize case and .BIN")
    check(core.parse_location("TITLE@229") == ("TITLE", 229), "decimal offsets are accepted")
    check(core.condition_of(core.parse_step("expect top=sc0000.bin@2217 script=system4.bin"))
          == {"top": "SC0000@0x8a9", "script": "SYSTEM4"}, "conditions reach the wire normalized")
    raw = {"ctx": "0x327d020", "current": 1, "frames": [
        {"index": 0, "codebase": 0x1000, "pc": 0x1000 + 4 * 0x477, "resid": 0},
        {"index": 1, "codebase": 0x9000, "pc": 0x9000 + 4 * 0x8A9, "resid": 13190},
    ]}
    state = native.describe_frames(raw, {"0": "SYSTEM4.BIN", "13190": "SC0000.BIN"})
    check([frame["location"] for frame in state["frames"]] == ["SYSTEM4@0x477", "SC0000@0x8a9"],
          "native frames become script@dword-offset")
    check(state["location"] == "SC0000@0x8a9" and state["waiting"] is None,
          "the top frame is the location; native reports no waiting kind")
    check(native.describe_frames(raw, {})["frames"][1]["script"] == "#0x3386", "unnamed resource ids stay visible")
    match = core.condition_matches
    check(match(state, {"top": "SC0000@0x8a9"}), "top= matches the running frame")
    check(not match(state, {"top": "SYSTEM4@0x477"}), "top= ignores callers")
    check(match(state, {"at": "SYSTEM4@0x477"}), "at= matches any frame")
    check(match(state, {"script": "sc0000.bin"}), "script= matches by name")
    check(match(state, {"script": "SYSTEM4", "top": "SC0000@0x8a9"}), "combined conditions match together")
    check(not match(state, {"script": "SYSTEM4", "top": "SC0000@0x8aa"}), "one failing key fails the condition")
    port_state = {"frames": [{"script": "SC0000", "offset": 0x8A9, "location": "SC0000@0x8a9"}], "waiting": "input"}
    check(match(port_state, {"waiting": "input"}) and not match(port_state, {"waiting": "menu"}),
          "waiting= compares the port's waiting kind")
    check(core.describe_stack(port_state) == "SC0000@0x8a9 waiting=input", "stacks describe themselves")
    check(raises(native.describe_frames, None, {}, contains="[native.engine]"), "profiles without engine facts say so")
    check(raises(native.describe_frames, {"error": "access violation"}, {}, contains="access violation"),
          "agent read errors surface")
    check(raises(native.native_condition, {"waiting": "input"}, contains="not supported by the native"),
          "native refuses waiting conditions")


def test_settle():
    def run(frames, difference, **options):
        clock = [0.0]
        sequence = iter(frames)
        last = [None]

        def capture():
            last[0] = next(sequence, last[0])
            return last[0]

        def sleep(seconds):
            clock[0] += seconds

        return native.settle(capture, difference, clock=lambda: clock[0], sleep=sleep, interval=0.1, **options)

    # Frames are numbers; the "difference" is their distance in percent of the screen.
    distance = lambda a, b: abs(a - b) / 100
    settled, seconds, _ = run([0, 0.2, 0.4, 0.6, 0.8, 1.0], distance, threshold=0.004, quiet=0.3, timeout=5)
    check(settled and seconds > 0.5, "slow typing adds up against the reference and delays settling")
    settled, seconds, _ = run([5, 5.2, 5, 5.2, 5, 5.2, 5], distance, threshold=0.004, quiet=0.3, timeout=5)
    check(settled and seconds < 0.5, "a small looping animation does not prevent settling")
    settled, seconds, _ = run(list(range(0, 1000, 10)), distance, threshold=0.004, quiet=0.3, timeout=1)
    check(not settled and seconds >= 1, "a screen that keeps changing times out")
    try:
        from PIL import Image
    except ImportError:
        print("skip: Pillow not installed (changed_fraction)")
        return
    black = Image.new("RGB", (10, 10))
    dotted = black.copy()
    dotted.putpixel((3, 3), (255, 255, 255))
    check(native.changed_fraction(black, dotted) == 0.01, "one changed pixel of 100 is 1%")
    check(native.changed_fraction(black, Image.new("RGB", (5, 5))) == 1.0, "a size change counts as everything")


def test_ini_update():
    text = "[display]\r\nScreenMode=0\r\nFullScreenBit=32\r\n[sound]\r\nVolume0=-1\r\n"
    updated = core.update_ini(text, {"display.screenmode": "1", "sound.Volume4": "0", "system.UseMMX": "1"})
    check(updated == "[display]\r\nScreenMode=1\r\nFullScreenBit=32\r\n[sound]\r\nVolume0=-1\r\nVolume4=0\r\n"
          "[system]\r\nUseMMX=1\r\n", "keys update in place, insert into their section, or add the section")
    check(core.update_ini("", {"display.ScreenMode": "1"}) == "[display]\r\nScreenMode=1\r\n",
          "a missing file becomes a CRLF INI like AGE's")
    check(core.update_ini("[a]\nx=1\n", {"a.x": "2"}) == "[a]\nx=2\n", "LF files stay LF")
    check(raises(core.update_ini, "", {"ScreenMode": "1"}, contains="section.key"), "keys need a section")


def test_save_roots_and_backups():
    settings = [("USEAPPDATAFOLDER", "1"), ("SAVEPATH", r"Eushully\Game\SAVE"), ("REGFILEPATH", r"Eushully\Game")]
    local, game = Path("/local"), Path("/game")
    check(core.live_profile_root(settings, local, game) == local / "Eushully" / "Game",
          "USEAPPDATAFOLDER=1 puts REGFILEPATH under LOCALAPPDATA")
    check(core.live_profile_root([("SAVEPATH", "Save"), ("REGFILEPATH", ".")], local, game) == game,
          "otherwise the paths are relative to the game")
    check(raises(core.live_profile_root, [("SAVEPATH", "A"), ("REGFILEPATH", "B")], local, game,
                 contains="not beneath"), "SAVEPATH must lie under REGFILEPATH")
    check(set(core.sandbox_folders(Path("box"))) >= {0x1C, 0x10}, "the sandbox covers LocalAppData and the Desktop")
    check(core.native_profile_in_sandbox(Path("box"), local / "Eushully" / "Game", local)
          == Path("box") / "LocalAppData" / "Eushully" / "Game", "a native sandbox re-roots the live profile path")
    check(raises(core.native_profile_in_sandbox, Path("box"), game, local, contains="LOCALAPPDATA"),
          "a save root outside LOCALAPPDATA cannot be sandboxed")
    check(raises(core.sandbox_dir, "port", "../up", contains="plain folder"), "sandbox names cannot be paths")
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        live = root / "live"
        (live / "SAVE").mkdir(parents=True)
        (live / "SYS4REG.INI").write_text("[display]\n", encoding="utf-8")
        (live / "SAVE" / "SAVE.DAT").write_bytes(b"x")
        first, created = core.backup_tree(live, root / "backups", "one")
        check(created and (first / "files" / "SAVE" / "SAVE.DAT").is_file(), "a backup copies the folder")
        again, created = core.backup_tree(live, root / "backups", "two")
        check(not created and again == first, "an unchanged folder reuses the newest backup")
        before = core.snapshot_tree(live)
        (live / "SAVE" / "SAVE00.DAT").write_bytes(b"y")
        changes = core.diff_snapshots(before, core.snapshot_tree(live))
        check(changes == {"added": ["SAVE/SAVE00.DAT"], "removed": [], "changed": []}, "snapshots report additions")
        _, created = core.backup_tree(live, root / "backups", "three")
        check(created, "a changed folder gets a new backup")
        check(core.backup_contents(first) == first / "files" and core.backup_contents(live) == live,
              "restores accept our backups and plain folder copies")


def test_port_sandboxes():
    profile = core.Profile(settings_file="SYS4REG.INI", settings={"display.ScreenMode": "1"},
                           quiet={"sound.Volume0": "0"})
    with tempfile.TemporaryDirectory() as directory:
        context = paths.ToolContext("himegari", Path(directory) / "game", Path(directory) / "x",
                                    shared_build=Path(directory) / "build")
        sandbox, profile_root = core.prepare_sandbox("port", "keep", None, False, profile, context)
        check(sandbox == profile_root == context.game_build / "drive" / "port" / "sandbox" / "keep",
              "a port sandbox is its own profile root (--data-root)")
        ini = (profile_root / "SYS4REG.INI").read_bytes().decode("cp932")
        check(ini == "[display]\r\nScreenMode=1\r\n[sound]\r\nVolume0=0\r\n", "sandbox settings and quiet are written")
        (profile_root / "SAVE").mkdir()
        (profile_root / "SAVE" / "SAVE00.DAT").write_bytes(b"slot")
        check(core.resolve_seed("port:keep", context) == profile_root, "port:NAME names another sandbox's profile")
        other, _ = core.prepare_sandbox("port", "copy", "port:keep", True, profile, context)
        check((other / "SAVE" / "SAVE00.DAT").read_bytes() == b"slot", "a new sandbox is seeded from another")
        loud, _ = core.prepare_sandbox("port", "loud", None, True, profile, context)
        check("Volume0" not in (loud / "SYS4REG.INI").read_bytes().decode("cp932"), "--sound skips the quiet settings")
        (other / "SAVE" / "SAVE01.DAT").write_bytes(b"mine")
        core.prepare_sandbox("port", "copy", "port:keep", True, profile, context)
        check((other / "SAVE" / "SAVE01.DAT").is_file(), "an existing named sandbox is kept, not re-seeded")
        fresh, _ = core.prepare_sandbox("port", "fresh", None, False, profile, context)
        (fresh / "junk").write_bytes(b"")
        fresh, _ = core.prepare_sandbox("port", "fresh", None, False, profile, context)
        check(not (fresh / "junk").exists(), "'fresh' is emptied on every start")
        check(raises(core.prepare_sandbox, "port", "self", "port:self", False, profile, context, contains="is this"),
              "a sandbox cannot seed itself")


def test_profiles():
    engine_ctx = (HERE.parent / "vm-map" / "engine-ctx.toml").read_text(encoding="utf-8")
    offsets = native.engine_ctx_offsets(engine_ctx)
    check(offsets == {"cur_ctx_index": 0x53D14, "frame_codebase": 0x53D28, "frame_pc": 0x53D2C,
                      "frame_script_resource_id": 0x53D64}, "frame offsets come from engine-ctx.toml")
    profile = core.parse_profile((HERE / "drive" / "himegari.toml").read_text(encoding="utf-8"))
    check(profile.engine == {"ctx_global": 0x5C6020, "frame_stride": 0x78, "frame_count": 20},
          "the Himegari profile names g_pEngineCtx and the frame stride under [native.engine]")
    check(profile.points["title.start"] == (400, 302) and profile.advance_point in profile.points,
          "the Himegari profile has its points and a valid advance point")
    check(profile.dialogs[0].code == 6 and profile.dialogs[0].match.startswith("データ"),
          "the registration box is answered Yes")
    check(profile.settings == {"display.ScreenMode": "1"} and profile.quiet == {"sound.Volume0": "0"},
          "the sandbox forces windowed mode and, unless --sound, silence")
    config = native.agent_config(profile, core.sandbox_folders(Path("C:/box")), engine_ctx)
    check(config["dialogs"][0]["match"] == list("データが壊れています．".encode("cp932")),
          "dialog matches reach the agent as cp932 bytes")
    check(config["folders"]["28"].endswith("LocalAppData"), "folders are keyed by CSIDL for the agent")
    check(config["engine"]["frame_pc"] == 0x53D2C and config["engine"]["ctx_global"] == 0x5C6020,
          "the agent gets the profile's engine facts merged with engine-ctx.toml")
    check(config["keep_polling"] == {"setter": 0x460180, "activate_return": 0x4865F3, "gate": 0x1C28},
          "the polling keeper gets its call site and the gate offset from engine-ctx.toml")
    check(raises(core.parse_profile, "[native.keep_polling]\nsetter = 1\nactivate_return = 2\n",
                 contains="needs [native.engine]"), "the polling keeper needs the engine facts")
    check(raises(native.agent_config, profile, {0x1C: Path("C:/箱")}, engine_ctx, contains="ASCII"),
          "sandbox paths must be ASCII")
    check(raises(core.parse_profile, '[[native.dialog]]\nmatch = "x"\nanswer = "maybe"\n', contains="dialog policy"),
          "dialog answers are checked")
    check(raises(core.parse_profile, '[points]\n"a" = [1]\n', contains="[x, y]"), "points need two numbers")
    check(raises(core.parse_profile, '[sandbox.settings]\n"a.b" = 1\n', contains="settings_file"),
          "sandbox settings name their file")
    check(raises(core.parse_profile, '[engine]\nctx_global = 1\n', contains="unknown profile section"),
          "the pre-M2 top-level [engine] layout is rejected, not ignored")


class FakeServer:
    """A protocol server on a background thread: checks the token, records requests, answers from replies(). It
    serves ``connections`` clients one after another (each drive.py or drive_mcp call is one client)."""

    def __init__(self, replies, token="secret", capabilities=("speed", "waiting"), connections=1):
        self.replies, self.token, self.capabilities = replies, token, list(capabilities)
        self.connections = connections
        self.requests = []
        self.listener = socket.socket()
        self.listener.bind(("127.0.0.1", 0))
        self.listener.listen(1)
        self.port = self.listener.getsockname()[1]
        self.thread = threading.Thread(target=self.serve, daemon=True)
        self.thread.start()

    def serve(self):
        for _ in range(self.connections):
            self.serve_one()

    def serve_one(self):
        connection, _ = self.listener.accept()
        with connection, connection.makefile("rwb") as stream:
            for line in stream:
                request = json.loads(line)
                self.requests.append(request)
                if request["op"] == "hello":
                    ok = request.get("token") == self.token
                    reply = {"id": request["id"], "ok": ok, **(
                        {"result": {"target": "port", "version": 1, "capabilities": self.capabilities}} if ok
                        else {"error": "the first request must be hello with the session token"})}
                else:
                    reply = {"id": request["id"], **self.replies(request)}
                stream.write((json.dumps(reply) + "\n").encode("utf-8"))
                stream.flush()
                if request["op"] in ("close",) or not reply["ok"] and request["op"] == "hello":
                    return

    def ops(self):
        return [request["op"] for request in self.requests]


def at(location, waiting=None):
    script, offset = core.parse_location(location)
    return {"frames": [{"script": script, "offset": offset, "location": f"{script}@{offset:#x}"}],
            "location": f"{script}@{offset:#x}", "waiting": waiting}


def test_client_and_executor():
    rejected = FakeServer(lambda request: {"ok": True})
    check(raises(core.Client, rejected.port, "wrong", contains="session token"), "a wrong token is refused")

    location = ["SC0000@0x83f"]

    def replies(request):
        op = request["op"]
        if op == "state":
            return {"ok": True, "result": at(location[0], "input")}
        if op == "click":
            if request["button"] == "left" and (request["x"], request["y"]) == (400, 300):
                location[0] = {"SC0000@0x83f": "SC0000@0x877", "SC0000@0x877": "SC0000@0x8a9"}.get(location[0],
                                                                                                 location[0])
            return {"ok": True, "result": {"x": request["x"], "y": request["y"], "cursor_moved_to": [355, 299]},
                    "events": [{"t": "now", "kind": "dialog", "text": "?"}]}
        if op == "settle":
            return {"ok": True, "result": {"settled": True, "elapsed_ms": 700}}
        if op == "wait":
            return {"ok": True, "result": {**at("TITLE@0x229"), "pokes": 4}}
        if op == "shot":
            return {"ok": True, "result": {"path": request["path"], "width": 800, "height": 600}}
        if op in ("speed", "pointer", "key", "wheel", "close"):
            return {"ok": True, "result": {}}
        return {"ok": False, "error": f"unknown op '{op}'"}

    server = FakeServer(replies)
    steps = core.parse_steps([
        "wait-state script=TITLE click=adv.next every=3 timeout=120",
        "point here 9 8",
        "move here",
        "advance 2",
        "expect top=SC0000@0x8a9 waiting=input",
        "rclick 10 20 hold=40",
        "key enter",
        "wheel down 2",
        "speed 4",
        "wait 0.5",
        "shot page3",
        "expect top=SC0000@0x913",
        "close",
    ])
    slept, results = [], []
    with tempfile.TemporaryDirectory() as directory:
        with core.Client(server.port, "secret") as client:
            check(client.target == "port" and client.capabilities == ["speed", "waiting"], "hello reports the target")
            executor = core.Executor(client, core.Profile(points={"adv.next": (400, 300)}), Path(directory),
                                     sleep=slept.append)
            ok = executor.run(steps, results.append)
        shot_path = str((Path(directory) / "page3.png").resolve())
    check(not ok and [r["ok"] for r in results] == [True] * 11 + [False], "the run stops at the first failing step")
    check("SC0000@0x8a9" in results[11]["error"], "a failed expect says where the VM is")
    wait = server.requests[1]
    check(wait == {"id": 2, "op": "wait", "until": {"script": "TITLE"}, "timeout_ms": 120000,
                   "poke": {"x": 400, "y": 300, "every_ms": 3000}}, "wait-state click= every= becomes wait with poke")
    check(results[0]["pokes"] == 4, "the poke count comes back")
    ops = server.ops()
    check(ops[2:9] == ["pointer", "click", "settle", "click", "settle", "state", "state"],
          "move, advance 2 (click, settle, twice) and expect run as primitives")
    check(server.requests[2]["x"] == 9 and server.requests[2]["y"] == 8, "session points resolve")
    check(results[3]["location"] == "SC0000@0x8a9" and results[3]["settled"], "advance reports where it ended")
    rclick = next(r for r in server.requests if r["op"] == "click" and r["button"] == "right")
    check((rclick["x"], rclick["y"], rclick["hold_ms"]) == (10, 20, 40), "rclick sends the right button and hold")
    check(results[5]["cursor_moved_to"] == [355, 299], "a cursor warp is reported with the click")
    check(any(e["kind"] == "dialog" for e in results[5].get("events", [])), "server events ride along with results")
    key = next(r for r in server.requests if r["op"] == "key")
    check(key["vk"] == 0x0D and key["hold_ms"] == 150, "keys send their VK and a default hold")
    wheel = next(r for r in server.requests if r["op"] == "wheel")
    check(wheel == {"id": wheel["id"], "op": "wheel", "notches": -2}, "wheel down sends negative notches only")
    check(next(r for r in server.requests if r["op"] == "speed")["factor"] == 4.0, "speed sends its factor")
    check(slept == [0.5] and "wait" not in ops[9:], "wait sleeps in the client")
    shot = next(r for r in server.requests if r["op"] == "shot")
    check(shot["path"] == shot_path and Path(shot["path"]).is_absolute(), "shot sends an absolute path below shots/")
    check("close" not in ops, "nothing runs after the failure")


def test_settings_trailer():
    pairs = [("USEAPPDATAFOLDER", "1"), ("SAVEPATH", "Eushully\\姫\\SAVE")]
    strings = b"".join(key.encode("ascii") + b"\0" + value.encode("cp932") + b"\0" for key, value in pairs)
    directory = struct.pack("<I", 1) + b"DATA1.ALF".ljust(256, b"\0") + struct.pack("<I", 0)
    blob = directory + struct.pack("<I", 4) + b"meta" + struct.pack("<II", len(strings), len(pairs)) + strings
    check(parse_sys4ini.directory_end(blob) == len(directory), "the directory end skips names and records")
    check(parse_sys4ini.settings_trailer(blob, len(directory)) == pairs, "trailer pairs decode in order")
    check(parse_sys4ini.settings_trailer(directory, len(directory)) == [], "append catalogs have no trailer")


def main():
    test_step_parsing()
    test_step_files()
    test_capabilities()
    test_keys_and_points()
    test_locations_and_conditions()
    test_settle()
    test_ini_update()
    test_save_roots_and_backups()
    test_port_sandboxes()
    test_profiles()
    test_client_and_executor()
    test_settings_trailer()
    print("FAILURES:", len(FAILS))
    return 1 if FAILS else 0


if __name__ == "__main__":
    sys.exit(main())
