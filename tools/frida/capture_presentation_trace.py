#!/usr/bin/env python3
"""Correlate SC0000 bytecode offsets with native retained-state presentation.

This read-only Frida probe records the narrow boundary needed to distinguish object mutation from
displayed output: draw/color workers, object composition, surface-command consumption, gfx frame render,
command-queue clear, and the D3D9 Present count.  Each event carries the most recent VM codebase/word
offset plus the native frame clock and dirty/queue fields.

Start the game at the title screen, start this probe, then choose New Game so the hook sees SC0000 from
entry.  Stop after the first dialogue page:

    py -3.11 -u -X utf8 tools/frida/capture_presentation_trace.py 30

Output: build/native-presentation-trace.jsonl.  No values are patched and the game is never slowed.
"""
import json
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
OUT = REPO / "build" / "native-presentation-trace.jsonl"
LIVE = REPO / "build" / "presentation-tracer-live.flag"

JS = r"""
const mod = Process.getModuleByName('AGE.EXE');
const OFF = {
  operand: 0x1b940, bind: 0x7e870, colorAnim: 0x7ea00, colorStatic: 0x7e9b0,
  render: 0x820b0, commands: 0x7fbc0, composite: 0x7f650, clear: 0x7cb10
};
const IDX=0x53d14, PC=0x53d2c, CB=0x53d28, STRIDE=0x78;
let ctx=null, current={codebase:0,offset:-1}, seq=0, presentCount=0, d3dHooked=false;

function i32(p,o){ try{return p.add(o).readS32();}catch(e){return null;} }
function u32(p,o){ try{return p.add(o).readU32();}catch(e){return null;} }
function state(extra={}) {
  const c=ctx;
  return Object.assign({kind:'event',seq:++seq,t:Date.now(),codebase:current.codebase,
    offset:current.offset,presents:presentCount,frameTime:c?u32(c,0xb550):null,
    dirty:c?i32(c,0xb558):null,commandDirty:c?i32(c,0xb560):null,
    commandCount:c?i32(c,0x41c):null},extra);
}
function emit(name,extra={}){ send(state(Object.assign({name:name},extra))); }
function stackI(reg,n){ try{return reg.esp.add(4+n*4).readS32();}catch(e){return null;} }

function hookD3D(c) {
  if (d3dHooked) return;
  d3dHooked=true;
  let d3d; try{d3d=Process.getModuleByName('d3d9.dll');}catch(e){emit('d3d-missing');return;}
  const lo=d3d.base, hi=d3d.base.add(d3d.size), inside=p=>p.compare(lo)>=0&&p.compare(hi)<0;
  let best=null,bestN=0;
  for(let off=0;off<0x200000;off+=4){
    let obj,vt; try{obj=c.add(off).readPointer();vt=obj.readPointer();}catch(e){continue;}
    if(obj.isNull()||!inside(vt))continue;
    let n=0; for(let s=0;s<120;s++){let fn;try{fn=vt.add(s*4).readPointer();}catch(e){break;}
      if(inside(fn))n++;else if(s>3)break;}
    if(n>bestN){bestN=n;best=vt;}
  }
  if(!best||bestN<60){emit('d3d-device-not-found',{methodRun:bestN});return;}
  const fn=best.add(17*4).readPointer();
  Interceptor.attach(fn,{onEnter(){presentCount++;}});
  emit('d3d-present-hooked',{address:fn.toString(),methodRun:bestN});
}

Interceptor.attach(mod.base.add(OFF.operand),{onEnter(){
  const c=this.context.ecx; ctx=c; hookD3D(c);
  try{const idx=c.add(IDX).readS32(); if(idx<0||idx>=64)return;
    const pc=c.add(PC+idx*STRIDE).readU32(), cb=c.add(CB+idx*STRIDE).readU32();
    current={codebase:cb>>>0,offset:((pc-cb)>>>2)};
  }catch(e){}
}});

Interceptor.attach(mod.base.add(OFF.bind),{onEnter(){emit('bind-draw',{
  handle:stackI(this.context,0),slot:stackI(this.context,1),src:[stackI(this.context,2),stackI(this.context,3),stackI(this.context,4),stackI(this.context,5)],
  dst:[stackI(this.context,6),stackI(this.context,7)]});}});
Interceptor.attach(mod.base.add(OFF.colorAnim),{onEnter(){emit('color-anim',{
  handle:stackI(this.context,0),delay:stackI(this.context,1),duration:stackI(this.context,2),argb:stackI(this.context,3)>>>0});}});
Interceptor.attach(mod.base.add(OFF.colorStatic),{onEnter(){emit('color-static',{
  handle:stackI(this.context,0),mode:stackI(this.context,1),argb:stackI(this.context,2)>>>0});}});
Interceptor.attach(mod.base.add(OFF.commands),{onEnter(){emit('surface-commands-enter');},onLeave(){emit('surface-commands-leave');}});
Interceptor.attach(mod.base.add(OFF.render),{onEnter(){ctx=this.context.ecx;emit('render-enter');},onLeave(){emit('render-leave');}});
Interceptor.attach(mod.base.add(OFF.clear),{onEnter(){emit('queue-clear-enter');},onLeave(){emit('queue-clear-leave');}});
Interceptor.attach(mod.base.add(OFF.composite),{onEnter(args){
  const h=args[0].toUInt32();
  if(h>=0xcb00 && h<=0xd400) emit('composite',{handle:h});
}});
send({kind:'ready',base:mod.base.toString()});
"""


def main():
    import frida
    seconds = int(sys.argv[1]) if len(sys.argv) > 1 and sys.argv[1].isdigit() else 30
    proc = sys.argv[2] if len(sys.argv) > 2 else "AGE.EXE"
    target = int(proc) if proc.isdigit() else proc
    OUT.parent.mkdir(parents=True, exist_ok=True)
    f = OUT.open("w", encoding="utf-8")
    counts = {}

    def on_message(msg, data):
        if msg.get("type") == "error":
            print("[frida-error]", msg.get("description")); return
        if msg.get("type") != "send": return
        row = msg["payload"]
        if row.get("kind") == "ready":
            print(f"[frida] presentation hooks live @ {row['base']}"); return
        f.write(json.dumps(row, ensure_ascii=False) + "\n"); f.flush()
        name = row.get("name", "?"); counts[name] = counts.get(name, 0) + 1
        if name in {"bind-draw", "color-anim", "color-static", "render-enter", "render-leave",
                    "surface-commands-enter", "queue-clear-enter"}:
            print(f"  #{row['seq']:05d} off=0x{row['offset']:05x} {name:22s} "
                  f"h={('0x%x' % row['handle']) if row.get('handle') is not None else '-':>8s} "
                  f"present={row['presents']} q={row['commandCount']}")

    try:
        session = frida.attach(target)
    except frida.ProcessNotFoundError:
        print("[frida] AGE.EXE not found; leave the native game at the title screen first.")
        f.close(); return 2
    script = session.create_script(JS); script.on("message", on_message); script.load()
    LIVE.write_text("live", encoding="utf-8")
    print(f"[frida] capture armed for {seconds}s. Choose New Game now; stop after the first page.")
    try:
        time.sleep(seconds)
    except KeyboardInterrupt:
        pass
    try: session.detach()
    except Exception: pass
    f.close()
    try: LIVE.unlink()
    except OSError: pass
    print(f"[trace] wrote {sum(counts.values())} events -> {OUT}")
    print("[trace] " + ", ".join(f"{k}={v}" for k,v in sorted(counts.items())))
    return 0 if counts else 3


if __name__ == "__main__":
    raise SystemExit(main())
