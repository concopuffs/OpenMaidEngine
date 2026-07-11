#!/usr/bin/env python3
"""Capture native ADV text layout, rasterization, reveal, and presentation boundaries.

Start the native game at the title screen, arm this probe, then choose New Game and click through
the first voiced page (SC0000 0x9b2/0x9d3). Output is written to
``build/native-adv-text-trace.jsonl``. The probe is read-only.

    py -3.11 -u -X utf8 tools/frida/capture_adv_text_trace.py 45
"""
import json
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
OUT = REPO / "build" / "native-adv-text-trace.jsonl"

JS = r"""
const mod = Process.getModuleByName('AGE.EXE');
const OFF = {
  operand:0x1b940, op6e:0x1e330, op7a:0x1eba0, op204:0x22a60,
  setCursor:0x530f0, buildText:0x576c0, buildTextBlocking:0x57dc0,
  reveal:0x51220, drawString:0x50150, rasterSimple:0x59d90, rasterCached:0x5b600,
  bind:0x7e870
};
const IDX=0x53d14, PC=0x53d2c, CB=0x53d28, STRIDE=0x78;
let ctx=null, current={codebase:0,offset:-1}, seq=0;

function i32(p,o=0){try{return p.add(o).readS32();}catch(e){return null;}}
function u32(p,o=0){try{return p.add(o).readU32();}catch(e){return null;}}
function stackP(reg,n){try{return reg.esp.add(4+n*4).readPointer();}catch(e){return ptr(0);}}
function stackI(reg,n){try{return reg.esp.add(4+n*4).readS32();}catch(e){return null;}}
function bytes(p,cap=256){
  if(!p || p.isNull()) return [];
  const out=[]; try{for(let i=0;i<cap;i++){const b=p.add(i).readU8();if(b===0)break;out.push(b);}}catch(e){}
  return out;
}
function layout(mgr,slot){
  try{
    if(slot===0)slot=i32(mgr,0x4c8);
    const p=mgr.add(0x414+slot*4).readPointer();
    const begin=u32(p,0x30),end=u32(p,0x34),count=(begin&&end)?((end-begin)/0x14):0;
    let last=null;if(count>0){const q=ptr(end-0x14);last=[i32(q,4),i32(q,8),i32(q,12),i32(q,16),i32(q,0)];}
    return {slot:slot,layout:p.toString(),count:count,last:last,origin:[i32(p,0xc),i32(p,0x10)],handleBase:i32(p,0x68),
      revealLimit:i32(p,0x6c),mode:i32(p,0x70),revealIndex:i32(mgr,0x570)};
  }catch(e){return {slot:slot,error:String(e)};}
}
function state(extra={}){const c=ctx;return Object.assign({kind:'event',seq:++seq,t:Date.now(),
  codebase:current.codebase,offset:current.offset,
  frameTime:c?u32(c,0xb550):null,runFlags:c?u32(c,0xa0ce4):null,
  charDelay:c?i32(c,0x14e9c):null},extra);}
function emit(name,extra={}){send(state(Object.assign({name:name},extra)));}

Interceptor.attach(mod.base.add(OFF.operand),{onEnter(){
  const c=this.context.ecx;ctx=c;
  try{const idx=c.add(IDX).readS32();if(idx<0||idx>=64)return;
    const pc=c.add(PC+idx*STRIDE).readU32(),cb=c.add(CB+idx*STRIDE).readU32();
    current={codebase:cb>>>0,offset:((pc-cb)>>>2)};}catch(e){}
}});

for(const [name,off] of [['op-0x6e',OFF.op6e],['op-0x7a',OFF.op7a],['op-0x204',OFF.op204]])
  Interceptor.attach(mod.base.add(off),{onEnter(){emit(name);}});

Interceptor.attach(mod.base.add(OFF.setCursor),{onEnter(){
  this.mgr=this.context.ecx;this.slot=stackI(this.context,0);
  emit('cursor-set-enter',{slot:this.slot,x:stackI(this.context,1),y:stackI(this.context,2),layout:layout(this.mgr,this.slot)});
},onLeave(){emit('cursor-set-leave',{layout:layout(this.mgr,this.slot)});}});

function hookBuild(off,name){Interceptor.attach(mod.base.add(off),{onEnter(){
  this.mgr=this.context.ecx;this.slot=stackI(this.context,0);this.before=layout(this.mgr,this.slot);
  emit(name+'-enter',{slot:this.slot,textBytes:bytes(stackP(this.context,1)),rubyBytes:bytes(stackP(this.context,2)),
    flags:stackI(this.context,3),layout:this.before});
},onLeave(){emit(name+'-leave',{layout:layout(this.mgr,this.slot)});}});}
hookBuild(OFF.buildText,'build-text');hookBuild(OFF.buildTextBlocking,'build-text-blocking');

Interceptor.attach(mod.base.add(OFF.reveal),{onEnter(){
  this.mgr=this.context.ecx;this.slot=stackI(this.context,0);emit('glyph-reveal-enter',{layout:layout(this.mgr,this.slot)});
},onLeave(ret){emit('glyph-reveal-leave',{done:ret.toInt32(),layout:layout(this.mgr,this.slot)});}});

Interceptor.attach(mod.base.add(OFF.drawString),{onEnter(){emit('draw-string-worker',{
  surface:stackI(this.context,0),textBytes:bytes(stackP(this.context,1)),x:stackI(this.context,2),y:stackI(this.context,3)});}});
for(const [name,off] of [['raster-simple',OFF.rasterSimple],['raster-cached',OFF.rasterCached]])
  Interceptor.attach(mod.base.add(off),{onEnter(){emit(name,{surface:stackI(this.context,0),x:stackI(this.context,1),y:stackI(this.context,2)});}});
Interceptor.attach(mod.base.add(OFF.bind),{onEnter(){const h=stackI(this.context,0);if(h===0xe678||h>=0xe678&&h<0xe800)
  emit('bind-text-object',{handle:h,slot:stackI(this.context,1),src:[stackI(this.context,2),stackI(this.context,3),stackI(this.context,4),stackI(this.context,5)],
    dst:[stackI(this.context,6),stackI(this.context,7)]});}});
send({kind:'ready',base:mod.base.toString()});
"""


def main():
    import frida
    seconds = int(sys.argv[1]) if len(sys.argv) > 1 and sys.argv[1].isdigit() else 45
    proc = sys.argv[2] if len(sys.argv) > 2 else "AGE.EXE"
    target = int(proc) if proc.isdigit() else proc
    OUT.parent.mkdir(parents=True, exist_ok=True)
    counts = {}
    with OUT.open("w", encoding="utf-8") as f:
        def on_message(msg, data):
            if msg.get("type") == "error":
                print("[frida-error]", msg.get("description")); return
            if msg.get("type") != "send": return
            row = msg["payload"]
            if row.get("kind") == "ready":
                print(f"[frida] ADV text hooks live @ {row['base']}"); return
            for key in ("textBytes", "rubyBytes"):
                if key in row:
                    row[key[:-5]] = bytes(row.pop(key)).decode("cp932", errors="replace")
            f.write(json.dumps(row, ensure_ascii=False) + "\n"); f.flush()
            name = row.get("name", "?"); counts[name] = counts.get(name, 0) + 1
            if name in {"op-0x6e", "op-0x7a", "op-0x204", "cursor-set-enter",
                        "draw-string-worker", "build-text-enter", "glyph-reveal-enter",
                        "bind-text-object", "surface-lock", "surface-unlock"}:
                print(f"  #{row['seq']:05d} off=0x{row['offset']:05x} {name:22s} delay={row['charDelay']}")

        try:
            session = frida.attach(target)
        except frida.ProcessNotFoundError:
            print("[frida] AGE.EXE not found; leave the native game at the title screen first.")
            return 2
        script = session.create_script(JS); script.on("message", on_message); script.load()
        print(f"[frida] capture armed for {seconds}s. Choose New Game and click through the first voiced page.")
        try: time.sleep(seconds)
        except KeyboardInterrupt: pass
        try: session.detach()
        except Exception: pass
    print(f"[trace] wrote {sum(counts.values())} events -> {OUT}")
    print("[trace] " + ", ".join(f"{k}={v}" for k, v in sorted(counts.items())))
    return 0 if counts else 3


if __name__ == "__main__":
    raise SystemExit(main())
