"""Plain tests for drive_mcp.py, the MCP adapter over the game driver: its tools called in-process through the MCP SDK
against test_drive_core's fake protocol server (no game, Godot or Frida). Needs the optional `mcp` package; without it
it fails and says so. Run it by hand: it is not in validate.py, because no validation layer requires `mcp`."""
import asyncio
import base64
import json
import sys
import tempfile
from pathlib import Path

try:
    import mcp  # noqa: F401
except ImportError:
    print("FAIL: needs the optional mcp package (py -3.11 -m pip install --user mcp)")
    sys.exit(1)

import drive_core as core
import drive_mcp
from mcp.server.mcpserver.exceptions import ToolError
from test_drive_core import FAILS, FakeServer, at, check

PNG = b"\x89PNG\r\n\x1a\nfake"


def call(tool, **arguments):
    return asyncio.run(drive_mcp.server.call_tool(tool, arguments))


def refused(tool, contains="", **arguments):
    try:
        call(tool, **arguments)
    except ToolError as error:
        return contains in str(error)
    return False


def texts(result):
    return [block.text for block in result.content if block.type == "text"]


def images(result):
    return [block for block in result.content if block.type == "image"]


class FakeDrive:
    """Point drive_core's session, profile and output folders at a fake server and a temporary folder."""

    def __init__(self, directory, server):
        self.directory, self.server = Path(directory), server
        self.saved = {name: getattr(core, name) for name in ("read_session", "drive_root", "load_profile")}

    def __enter__(self):
        info = {"pid": 1, "port": self.server.port, "token": "secret"} if self.server else None
        core.read_session = lambda target, *_, **__: info
        core.drive_root = lambda target, *_, **__: self.directory / target
        core.load_profile = lambda *_, **__: core.Profile(points={"adv.next": (400, 300)}, advance_point="adv.next")
        return self

    def __exit__(self, *_):
        for name, value in self.saved.items():
            setattr(core, name, value)


def test_tools_and_instructions():
    tools = asyncio.run(drive_mcp.server.list_tools())
    check(sorted(tool.name for tool in tools) ==
          ["drive_screenshot", "drive_start", "drive_state", "drive_steps", "drive_stop"], "the five tools of spec §8")
    steps = next(tool for tool in tools if tool.name == "drive_steps")
    check(set(steps.input_schema["properties"]) == {"target", "steps", "file", "screenshot"},
          "drive_steps takes target, steps or file, and screenshot")
    check("wait-state" in drive_mcp.INSTRUCTIONS and "conditions:" in drive_mcp.INSTRUCTIONS,
          "the instructions carry the step syntax (drive.py steps)")


def test_steps_screenshot_and_state():
    location = ["SC0000@0x83f"]

    def replies(request):
        op = request["op"]
        if op == "state":
            return {"ok": True, "result": at(location[0], "input")}
        if op == "click":
            location[0] = "SC0000@0x877"
            return {"ok": True, "result": {}, "events": [{"t": "now", "kind": "dialog-open", "text": "?"}]}
        if op == "settle":
            return {"ok": True, "result": {"settled": True, "elapsed_ms": 700}}
        if op == "shot":
            Path(request["path"]).write_bytes(PNG)
            return {"ok": True, "result": {"path": request["path"], "width": 800, "height": 600}}
        if op == "close":
            return {"ok": True, "result": {"exit_code": 0}}
        return {"ok": False, "error": f"unknown op '{op}'"}

    server = FakeServer(replies, connections=8)
    with tempfile.TemporaryDirectory() as directory, FakeDrive(directory, server):
        result = call("drive_steps", target="port", steps=["click adv.next", "settle", "expect top=SC0000@0x877"])
        text = texts(result)[0]
        check(text.startswith("ok: 3 of 3 steps"), "drive_steps sums up a passing run")
        check("ok   click adv.next" in text and "event dialog-open" in text, "one line per step, events included")
        shots = images(result)
        check(len(shots) == 1 and shots[0].mime_type == "image/png" and base64.b64decode(shots[0].data) == PNG,
              "the final screenshot comes back as an image")
        check("screenshot " in text and "800x600 at SC0000@0x877" in text, "and is described with its location")
        check(server.ops()[-5:] == ["click", "settle", "state", "shot", "state"],
              "steps run as drive.py runs them, then one shot on the same connection")

        failed = call("drive_steps", target="port", steps=["expect top=SC0000@0x913"])
        check(texts(failed)[0].startswith("FAILED at step 1 of 1: expected top=SC0000@0x913"),
              "a failing step is reported, not raised")
        check(len(images(failed)) == 1, "a failed run still returns its screenshot")

        file = Path(directory) / "two.steps"
        file.write_text("# comment\nsettle\n\nstate\n", encoding="utf-8")
        from_file = call("drive_steps", target="port", file=str(file), screenshot=False)
        check(texts(from_file)[0].startswith("ok: 2 of 2 steps") and not images(from_file),
              "a .steps file runs; screenshot=false returns text only")

        closed = call("drive_steps", target="native", steps=["close"])
        check(not images(closed) and "exit_code=0" in texts(closed)[0], "no screenshot after close")

        shot = call("drive_screenshot", target="port", name="title")
        check(Path(texts(shot)[0].split()[1]).name == "title.png" and len(images(shot)) == 1,
              "drive_screenshot names the file and returns the image")

        state = json.loads(texts(call("drive_state", target="port"))[0])
        check(state["state"] == "SC0000@0x877 waiting=input" and "token" not in state["session"],
              "drive_state reports the stack and hides the token")

        check(refused("drive_steps", "needs dialogs", target="port", steps=["dialogs"]),
              "a step the target cannot run is refused before it starts")


def test_refusals():
    with tempfile.TemporaryDirectory() as directory, FakeDrive(directory, None):
        check(refused("drive_steps", "no port session", target="port", steps=["state"]), "no session is an error")
        check(texts(call("drive_state", target="port"))[0].startswith("no port session"),
              "drive_state without a session says so")
        check(refused("drive_steps", "either steps or file", target="port", steps=["state"], file="x.steps"),
              "steps and file exclude each other")
        check(refused("drive_steps", "unknown step", target="port", steps=["jump 3"]), "unknown steps are refused")
        check(refused("drive_steps", target="nope", steps=["state"]), "the target is native or port")
        check(refused("drive_start", "port only", target="native", build=False),
              "build=false is refused for native before anything runs")
        check(refused("drive_start", "port only", target="native", optimize=True),
              "optimize is refused for native")
        check(refused("drive_start", "needs build=true", target="port", optimize=True, build=False),
              "optimize needs the build")


def main():
    test_tools_and_instructions()
    test_steps_screenshot_and_state()
    test_refusals()
    print("FAILURES:", len(FAILS))
    return 1 if FAILS else 0


if __name__ == "__main__":
    sys.exit(main())
