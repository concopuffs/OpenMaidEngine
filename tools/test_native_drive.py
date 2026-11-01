"""Plain tests for native_drive.py's platform-independent logic (no AGE, Frida, or Windows needed)."""
import json
import struct
import sys
import tempfile
from pathlib import Path

import native_drive as nd
import parse_sys4ini

FAILS = []
HERE = Path(__file__).resolve().parent


def check(condition, message):
    (FAILS.append(message) or print("FAIL:", message)) if not condition else print("ok:", message)


def raises(function, *args, contains=""):
    try:
        function(*args)
    except nd.DriveError as error:
        return contains in str(error)
    return False


def test_step_parsing():
    step = nd.parse_step('click title.start hold=200')
    check((step.name, step.args, step.options) == ("click", ("title.start",), {"hold": "200"}),
          "a named-point click keeps its option")
    check(nd.parse_step("click 0x10 20").args == ("0x10", "20"), "coordinates may be hex")
    check(nd.parse_step('answer "&Yes"').args == ("&Yes",), "quoted arguments survive")
    check(nd.parse_step("expect top=SC0000@0x8a9").options == {"top": "SC0000@0x8a9"},
          "an option value may contain @ and =-free text")
    check(raises(nd.parse_step, "jump 1 2", contains="unknown step"), "unknown steps are rejected")
    check(raises(nd.parse_step, "click", contains="takes 1..2"), "argument counts are checked")
    check(raises(nd.parse_step, "click 1 2 speed=3", contains="no option"), "unknown options are rejected")
    check(raises(nd.parse_step, "settle timeout=soon", contains="not a number"), "numeric options must parse")
    check(raises(nd.parse_step, "key esc", contains="WM_CLOSE"), "ESC is refused at parse time")
    check(raises(nd.parse_step, "key F1", contains="screenshot"), "F1 is refused at parse time")
    check(raises(nd.parse_step, "expect", contains="exactly one"), "expect needs a matcher")
    check(raises(nd.parse_step, "expect at=SC0000@0x1 top=SC0000@0x1", contains="exactly one"),
          "expect takes a single matcher")
    check(raises(nd.parse_step, "wait-state top=SC0000 timeout=5", contains="SCRIPT@OFFSET"),
          "locations need an offset")
    check(raises(nd.parse_step, "shot ../escape", contains="plain file names"), "shot names cannot be paths")
    check(raises(nd.parse_step, "wheel sideways", contains="up or down"), "wheel direction is checked")
    check(nd.parse_step("wait-state script=TITLE click=adv.next every=3").options["every"] == "3",
          "wait-state can click while it polls")


def test_step_files():
    steps = nd.parse_steps(["# comment", "", "click 1 2   # trailing note", "  settle timeout=3"])
    check([step.text for step in steps] == ["click 1 2", "settle timeout=3"], "comments and blanks are skipped")
    check([step.line for step in steps] == [3, 4], "steps keep their line numbers")
    check(raises(nd.parse_steps, ["settle", "bogus"], contains="line 2"), "errors name the line")
    scenarios = sorted((HERE / "native-drive" / "scenarios").glob("*.steps"))
    check(bool(scenarios), "scenario files exist")
    for path in scenarios:
        try:
            nd.parse_steps(path.read_text(encoding="utf-8").splitlines())
            parsed = True
        except nd.DriveError as error:
            print("   ", error)
            parsed = False
        check(parsed, f"{path.name} parses")


def test_keys_and_points():
    check(nd.virtual_key("enter") == 0x0D and nd.virtual_key("Ctrl") == 0x11, "key names map to VKs")
    check(nd.virtual_key("a") == 0x41 and nd.virtual_key("7") == 0x37, "letters and digits map to VKs")
    check(nd.virtual_key("0x26") == 0x26 and nd.virtual_key("f2") == 0x71, "hex VKs and function keys")
    check(raises(nd.virtual_key, "0x1b", contains="WM_CLOSE"), "ESC is refused as a hex VK too")
    check(raises(nd.virtual_key, "hyper", contains="unknown key"), "unknown key names are rejected")
    points = {"title.start": (400, 302)}
    check(nd.resolve_point(("title.start",), points) == (400, 302), "named points resolve")
    check(nd.resolve_point(("5", "6"), points) == (5, 6), "coordinates pass through")
    check(raises(nd.resolve_point, ("nowhere",), points, contains="title.start"), "unknown points list known ones")
    check(nd.lparam(800, 600) == (600 << 16) | 800, "lparam packs y over x")
    check(nd.wheel_wparam(-1) == 0xFF880000, "a wheel notch down is -120 in the high word")


def test_locations_and_state():
    check(nd.parse_location("sc0000.bin@0x8A9") == ("SC0000", 0x8A9), "locations normalize case and .BIN")
    check(nd.parse_location("TITLE@229") == ("TITLE", 229), "decimal offsets are accepted")
    raw = {"ctx": "0x327d020", "current": 1, "frames": [
        {"index": 0, "codebase": 0x1000, "pc": 0x1000 + 4 * 0x2AD, "resid": 0},
        {"index": 1, "codebase": 0x9000, "pc": 0x9000 + 4 * 0x8A9, "resid": 13190},
    ]}
    state = nd.describe_frames(raw, {"0": "SYSTEM4.BIN", "13190": "SC0000.BIN"})
    check([frame["location"] for frame in state["frames"]] == ["SYSTEM4@0x2ad", "SC0000@0x8a9"],
          "frames become script@dword-offset")
    check(state["location"] == "SC0000@0x8a9", "the top frame is the location")
    check(nd.describe_frames(raw, {})["frames"][1]["script"] == "#0x3386", "unnamed resource ids stay visible")
    check(nd.state_matches(state, {"top": "SC0000@0x8a9"}), "top= matches the running frame")
    check(not nd.state_matches(state, {"top": "SYSTEM4@0x2ad"}), "top= ignores callers")
    check(nd.state_matches(state, {"at": "SYSTEM4@0x2ad"}), "at= matches any frame")
    check(nd.state_matches(state, {"script": "sc0000.bin"}), "script= matches by name")
    check(raises(nd.describe_frames, None, {}, contains="[engine]"), "profiles without engine facts say so")
    check(raises(nd.describe_frames, {"error": "access violation"}, {}, contains="access violation"),
          "agent read errors surface")


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

        return nd.settle(capture, difference, clock=lambda: clock[0], sleep=sleep, interval=0.1, **options)

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
    check(nd.changed_fraction(black, dotted) == 0.01, "one changed pixel of 100 is 1%")
    check(nd.changed_fraction(black, Image.new("RGB", (5, 5))) == 1.0, "a size change counts as everything")


def test_ini_update():
    text = "[display]\r\nScreenMode=0\r\nFullScreenBit=32\r\n[sound]\r\nVolume0=-1\r\n"
    updated = nd.update_ini(text, {"display.screenmode": "1", "sound.Volume4": "0", "system.UseMMX": "1"})
    check(updated == "[display]\r\nScreenMode=1\r\nFullScreenBit=32\r\n[sound]\r\nVolume0=-1\r\nVolume4=0\r\n"
          "[system]\r\nUseMMX=1\r\n", "keys update in place, insert into their section, or add the section")
    check(nd.update_ini("", {"display.ScreenMode": "1"}) == "[display]\r\nScreenMode=1\r\n",
          "a missing file becomes a CRLF INI like AGE's")
    check(nd.update_ini("[a]\nx=1\n", {"a.x": "2"}) == "[a]\nx=2\n", "LF files stay LF")
    check(raises(nd.update_ini, "", {"ScreenMode": "1"}, contains="section.key"), "keys need a section")


def test_save_roots_and_backups():
    settings = [("USEAPPDATAFOLDER", "1"), ("SAVEPATH", r"Eushully\Game\SAVE"), ("REGFILEPATH", r"Eushully\Game")]
    local, game = Path("/local"), Path("/game")
    check(nd.live_profile_root(settings, local, game) == local / "Eushully" / "Game",
          "USEAPPDATAFOLDER=1 puts REGFILEPATH under LOCALAPPDATA")
    check(nd.live_profile_root([("SAVEPATH", "Save"), ("REGFILEPATH", ".")], local, game) == game,
          "otherwise the paths are relative to the game")
    check(raises(nd.live_profile_root, [("SAVEPATH", "A"), ("REGFILEPATH", "B")], local, game,
                 contains="not beneath"), "SAVEPATH must lie under REGFILEPATH")
    check(set(nd.sandbox_folders(Path("box"))) >= {0x1C, 0x10}, "the sandbox covers LocalAppData and the Desktop")
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        live = root / "live"
        (live / "SAVE").mkdir(parents=True)
        (live / "SYS4REG.INI").write_text("[display]\n", encoding="utf-8")
        (live / "SAVE" / "SAVE.DAT").write_bytes(b"x")
        first, created = nd.backup_tree(live, root / "backups", "one")
        check(created and (first / "files" / "SAVE" / "SAVE.DAT").is_file(), "a backup copies the folder")
        again, created = nd.backup_tree(live, root / "backups", "two")
        check(not created and again == first, "an unchanged folder reuses the newest backup")
        before = nd.snapshot_tree(live)
        (live / "SAVE" / "SAVE00.DAT").write_bytes(b"y")
        changes = nd.diff_snapshots(before, nd.snapshot_tree(live))
        check(changes == {"added": ["SAVE/SAVE00.DAT"], "removed": [], "changed": []}, "snapshots report additions")
        _, created = nd.backup_tree(live, root / "backups", "three")
        check(created, "a changed folder gets a new backup")
        check(nd.backup_contents(first) == first / "files" and nd.backup_contents(live) == live,
              "restores accept our backups and plain folder copies")


def test_profiles():
    engine_ctx = (HERE.parent / "vm-map" / "engine-ctx.toml").read_text(encoding="utf-8")
    offsets = nd.engine_ctx_offsets(engine_ctx)
    check(offsets == {"cur_ctx_index": 0x53D14, "frame_codebase": 0x53D28, "frame_pc": 0x53D2C,
                      "frame_script_resource_id": 0x53D64}, "frame offsets come from engine-ctx.toml")
    profile = nd.parse_profile((HERE / "native-drive" / "himegari.toml").read_text(encoding="utf-8"), engine_ctx)
    check(profile.engine["ctx_global"] == 0x5C6020 and profile.engine["frame_stride"] == 0x78,
          "the Himegari profile names g_pEngineCtx and the frame stride")
    check(profile.points["title.start"] == (400, 302) and profile.advance_point in profile.points,
          "the Himegari profile has its points and a valid advance point")
    check(profile.dialogs[0].code == 6 and profile.dialogs[0].match.startswith("データ"),
          "the registration box is answered Yes")
    check(profile.settings == {"display.ScreenMode": "1"} and profile.quiet == {"sound.Volume0": "0"},
          "the sandbox forces windowed mode and, unless --sound, silence")
    config = nd.agent_config(profile, nd.sandbox_folders(Path("C:/box")))
    check(config["dialogs"][0]["match"] == list("データが壊れています．".encode("cp932")),
          "dialog matches reach the agent as cp932 bytes")
    check(config["folders"]["28"].endswith("LocalAppData"), "folders are keyed by CSIDL for the agent")
    check(raises(nd.agent_config, profile, {0x1C: Path("C:/箱")}, contains="ASCII"), "sandbox paths must be ASCII")
    check(raises(nd.parse_profile, '[[dialog]]\nmatch = "x"\nanswer = "maybe"\n', None, contains="dialog policy"),
          "dialog answers are checked")
    check(raises(nd.parse_profile, '[points]\n"a" = [1]\n', None, contains="[x, y]"), "points need two numbers")
    check(raises(nd.parse_profile, '[sandbox.settings]\n"a.b" = 1\n', None, contains="settings_file"),
          "sandbox settings name their file")


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
    test_keys_and_points()
    test_locations_and_state()
    test_settle()
    test_ini_update()
    test_save_roots_and_backups()
    test_profiles()
    test_settings_trailer()
    print("FAILURES:", len(FAILS))
    return 1 if FAILS else 0


if __name__ == "__main__":
    sys.exit(main())
