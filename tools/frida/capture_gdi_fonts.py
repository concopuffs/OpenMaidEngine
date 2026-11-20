#!/usr/bin/env python3
"""Record the fonts a running native AGE creates and the glyphs it asks GDI for.

It hooks three gdi32 exports, which is safe against the game's packer: CreateFontIndirectA (every LOGFONT),
GetGlyphOutlineA (the size query of each glyph, with the font selected at the time and the GLYPHMETRICS returned)
and AddFontMemResourceEx (a font the game registers for its own process). The output shows what the text code
decided: which face, height, width and weight each glyph was drawn with. That is how 4.38D's aspect-mode font
width was found to differ from 4.33G's (docs/engine-re.md, "Kamidori against Arterial, triaged").

Attach while the game runs, and drive it from another call; the game driver's native session is fine:

    py -3.11 -X utf8 tools/drive.py --game arterial native start
    py -3.11 -u -X utf8 tools/frida/capture_gdi_fonts.py --game arterial 75      (in the background)
    py -3.11 -X utf8 tools/drive.py --game arterial native run tools/drive/scenarios/arterial-opening.steps
    py -3.11 -X utf8 tools/frida/capture_gdi_fonts.py --game arterial --summary
    py -3.11 -X utf8 tools/drive.py --game arterial native stop

Output: build/games/<game>/native-gdi-fonts.jsonl, one JSON object per line: {"kind": "font", "handle", "lf"},
{"kind": "glyph", "code", "format", "font", "lf", "size", "bbx", "bby", "ox", "oy", "incx", "incy"} and
{"kind": "addfont", "size", "handle"}. A LOGFONT's face is its bytes; --summary decodes them as CP932.
"""
import collections
import json
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import paths

OUT = paths.BUILD / "native-gdi-fonts.jsonl"
GLYPH_CAP = 20000

JS = r"""
const gdi = Process.getModuleByName('gdi32.dll');
function exp(name){ return gdi.getExportByName(name); }
function logfont(p){
  const face = [];
  for (let i = 0; i < 32; i++) { const b = p.add(28 + i).readU8(); if (b === 0) break; face.push(b); }
  return {height:p.readS32(), width:p.add(4).readS32(), escapement:p.add(8).readS32(), weight:p.add(16).readS32(),
          italic:p.add(20).readU8(), charset:p.add(23).readU8(), outPrecision:p.add(24).readU8(),
          quality:p.add(26).readU8(), pitch:p.add(27).readU8(), face:face};
}
const fonts = {};
Interceptor.attach(exp('CreateFontIndirectA'), {
  onEnter(args){ this.lf = logfont(args[0]); },
  onLeave(ret){ fonts[ret.toString()] = this.lf; send({kind:'font', handle:ret.toString(), lf:this.lf}); }
});
const getCurrentObject = new NativeFunction(exp('GetCurrentObject'), 'pointer', ['pointer','uint']);
let glyphs = 0;
Interceptor.attach(exp('GetGlyphOutlineA'), {
  onEnter(args){ this.hdc=args[0]; this.ch=args[1].toInt32()>>>0; this.fmt=args[2].toInt32()>>>0; this.gm=args[3];
                 this.cb=args[4].toInt32()>>>0; },
  onLeave(ret){
    if (this.cb !== 0 || glyphs++ >= %d) return;       // the size query of each glyph only
    const font = getCurrentObject(this.hdc, 6).toString();
    const g = this.gm;
    send({kind:'glyph', code:this.ch, format:this.fmt, font:font, lf:fonts[font]||null, size:ret.toInt32(),
          bbx:g.readU32(), bby:g.add(4).readU32(), ox:g.add(8).readS32(), oy:g.add(12).readS32(),
          incx:g.add(16).readS16(), incy:g.add(18).readS16()});
  }
});
Interceptor.attach(exp('AddFontMemResourceEx'), {
  onEnter(args){ this.size=args[1].toInt32(); },
  onLeave(ret){ send({kind:'addfont', size:this.size, handle:ret.toString()}); }
});
""" % GLYPH_CAP


def face(lf) -> str:
    return bytes(lf["face"]).decode("cp932", "replace") if lf else "?"


def summarize(path: Path) -> int:
    if not path.exists():
        print(f"no capture at {path}")
        return 2
    rows = [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]
    fonts: dict = collections.OrderedDict()
    glyphs: dict = collections.OrderedDict()
    for row in rows:
        if row["kind"] == "font":
            lf = row["lf"]
            key = (face(lf), lf["height"], lf["width"], lf["weight"], lf["escapement"])
            fonts[key] = fonts.get(key, 0) + 1
        elif row["kind"] == "glyph":
            lf = row["lf"]
            key = (row["code"], row["format"], face(lf), lf and lf["height"], lf and lf["width"], lf and lf["weight"],
                   row["bbx"], row["bby"], row["ox"], row["oy"], row["incx"])
            glyphs[key] = glyphs.get(key, 0) + 1
        elif row["kind"] == "addfont":
            print(f"registered a font of {row['size']} bytes for the process")
    print(f"{len(fonts)} distinct fonts (face, height, width, weight, escapement : times created)")
    for key, count in fonts.items():
        print(f"  {key[0]}  {key[1]} {key[2]} {key[3]} {key[4]} : {count}")
    print(f"{len(glyphs)} distinct glyph queries (code format | face height width weight | "
          "blackBoxX blackBoxY originX originY cellIncX : times)")
    for key, count in glyphs.items():
        print(f"  0x{key[0]:04x} {key[1]} | {key[2]} {key[3]} {key[4]} {key[5]} | "
              f"{key[6]} {key[7]} {key[8]} {key[9]} {key[10]} : {count}")
    return 0


def capture(seconds: float, path: Path) -> int:
    from frida_runtime import frida
    device = frida.get_local_device()
    pids = [process.pid for process in device.enumerate_processes() if process.name.upper() == "AGE.EXE"]
    if len(pids) != 1:
        print(f"[frida] need exactly one running AGE.EXE, found {len(pids)}")
        return 2
    path.parent.mkdir(parents=True, exist_ok=True)
    count = 0
    with path.open("w", encoding="utf-8") as out:
        def on_message(message, data):
            nonlocal count
            if message["type"] == "send":
                out.write(json.dumps(message["payload"], ensure_ascii=False) + "\n")
                count += 1
            else:
                print("[frida] script error:", message, file=sys.stderr)

        session = device.attach(pids[0])
        script = session.create_script(JS)
        script.on("message", on_message)
        script.load()
        print(f"[frida] attached to AGE.EXE pid {pids[0]}; recording for {seconds:g} s")
        time.sleep(seconds)
        try:
            script.unload()
            session.detach()
        except Exception as error:  # the game may have exited first
            print("[frida] detach:", error, file=sys.stderr)
    print(f"{count} records -> {path}")
    return 0


def main(argv: list[str]) -> int:
    args = [arg for arg in argv if not arg.startswith("--")]
    if "--summary" in argv:
        return summarize(OUT)
    seconds = float(args[0]) if args else 60.0
    return capture(seconds, OUT)


if __name__ == "__main__":
    # --game/--game-root are read by tools/paths.py from sys.argv.
    rest = []
    skip = False
    for arg in sys.argv[1:]:
        if skip:
            skip = False
            continue
        if arg in ("--game", "--profile", "--game-root"):
            skip = True
            continue
        if arg.startswith(("--game=", "--profile=", "--game-root=")):
            continue
        rest.append(arg)
    sys.exit(main(rest))
