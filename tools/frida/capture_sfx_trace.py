#!/usr/bin/env python3
"""Capture native SC0000 SFX op/worker/DirectSound timing (read-only).

Start at the title, arm this probe, then choose New Game.  The opening reaches the first
load/start pair at 0xc29 without input.  Output: build/native-sfx-trace.jsonl.
"""
import json
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
OUT = REPO / "build" / "native-sfx-trace.jsonl"
TMP = REPO / "build" / "native-sfx-trace.tmp.jsonl"
LIVE = REPO / "build" / "sfx-tracer-live.flag"

JS = r"""
const mod=Process.getModuleByName('AGE.EXE');
const OFF={operand:0x1b940,b4:0x201d0,b5:0x20210,b6:0x20250,c2:0x204c0,d9:0x16da0,
 load:0x82500,start:0x825d0,release:0x82600,decode:0x83360,destroy:0x831a0,
 dsStart:0x84270,fadeArm:0x64830,fadeTick:0x64960};
const IDX=0x53d14,PC=0x53d2c,CB=0x53d28,STRIDE=0x78;
let ctx=null,current={codebase:0,offset:-1},seq=0; const hooked={};
function i32(p,o=0){try{return p.add(o).readS32();}catch(e){return null;}}
function u32(p,o=0){try{return p.add(o).readU32();}catch(e){return null;}}
function sp(reg,n){try{return reg.esp.add(4+n*4).readS32();}catch(e){return null;}}
function pp(p,o=0){try{return p.add(o).readPointer();}catch(e){return ptr(0);}}
function emit(name,x={}){send(Object.assign({kind:'event',seq:++seq,t:Date.now(),name,
 codebase:current.codebase,offset:current.offset,runFlags:ctx?u32(ctx,0xa0ce4):null},x));}
function mgrState(m,ch){return {manager:m.toString(),channel:ch,resource:i32(m,0x4bc+ch*4),
 object:pp(m,0x5a4+ch*4).toString()};}
function install(){
Interceptor.attach(mod.base.add(OFF.operand),{onEnter(){ctx=this.context.ecx;try{const n=i32(ctx,IDX);
 if(n<0||n>=64)return;const pc=u32(ctx,PC+n*STRIDE),cb=u32(ctx,CB+n*STRIDE);
 current={codebase:cb>>>0,offset:((pc-cb)>>>2)};}catch(e){}}});
for(const n of ['b4','b5','b6','c2','d9']) Interceptor.attach(mod.base.add(OFF[n]),{onEnter(){emit('op-0x'+n);}});
function hookDs(obj,ch){if(!obj||obj.isNull())return;const buf=pp(obj,0x40c);if(buf.isNull())return;
 const vt=pp(buf),key=vt.toString();if(hooked[key])return;hooked[key]=true;
 for(const [name,slot] of [['play',12],['set-position',13],['set-volume',15],['set-pan',16],['stop',18]]){
  const fn=pp(vt,slot*4),k=fn.toString();if(hooked[k])continue;hooked[k]=true;
  Interceptor.attach(fn,{onEnter(args){emit('ds-'+name,{channel:ch,buffer:args[0].toString(),arg1:args[1].toInt32(),arg2:args[2].toInt32(),arg3:args[3].toInt32()});}});
 } emit('ds-hooks',{channel:ch,object:obj.toString(),buffer:buf.toString(),vtable:vt.toString()});}
Interceptor.attach(mod.base.add(OFF.load),{onEnter(){this.m=this.context.ecx;this.ch=sp(this.context,0);this.res=sp(this.context,1);
 emit('sfx-load-enter',Object.assign({resourceArg:this.res},mgrState(this.m,this.ch)));},onLeave(ret){const s=mgrState(this.m,this.ch);
 emit('sfx-load-leave',Object.assign({ret:ret.toInt32()},s));hookDs(ptr(s.object),this.ch);}});
Interceptor.attach(mod.base.add(OFF.start),{onEnter(){this.m=this.context.ecx;this.ch=sp(this.context,0);this.mode=sp(this.context,1);
 const s=mgrState(this.m,this.ch);emit('sfx-start-enter',Object.assign({mode:this.mode},s));hookDs(ptr(s.object),this.ch);},
 onLeave(ret){emit('sfx-start-leave',Object.assign({ret:ret.toInt32()},mgrState(this.m,this.ch)));}});
Interceptor.attach(mod.base.add(OFF.release),{onEnter(){this.m=this.context.ecx;this.ch=sp(this.context,0);emit('sfx-release',mgrState(this.m,this.ch));}});
Interceptor.attach(mod.base.add(OFF.decode),{onEnter(){this.m=this.context.ecx;this.ch=sp(this.context,0);emit('sfx-decode-enter',{manager:this.m.toString(),channel:this.ch,byteLength:sp(this.context,1),fileSlot:sp(this.context,2)});},
 onLeave(){const obj=pp(this.m,0x5a4+this.ch*4);hookDs(obj,this.ch);}});
Interceptor.attach(mod.base.add(OFF.destroy),{onEnter(){emit('sfx-buffer-destroy',{manager:this.context.ecx.toString(),channel:sp(this.context,0)});}});
Interceptor.attach(mod.base.add(OFF.dsStart),{onEnter(){this.obj=this.context.ecx;hookDs(this.obj,i32(this.obj,0x408));emit('ds-start-worker-enter',{object:this.obj.toString(),buffer:pp(this.obj,0x40c).toString(),preloaded:i32(this.obj,0x245c),playing:i32(this.obj,0x2460),loop:i32(this.obj,0x2464)});},
 onLeave(ret){emit('ds-start-worker-leave',{object:this.obj.toString(),ret:ret.toInt32(),preloaded:i32(this.obj,0x245c),playing:i32(this.obj,0x2460),loop:i32(this.obj,0x2464)});}});
Interceptor.attach(mod.base.add(OFF.fadeArm),{onEnter(){emit('audio-fade-arm',{object:this.context.ecx.toString(),target:sp(this.context,0),step:sp(this.context,1),current:i32(this.context.ecx,0x420)});}});
Interceptor.attach(mod.base.add(OFF.fadeTick),{onEnter(){emit('audio-fade-tick',{object:this.context.ecx.toString(),ticks:sp(this.context,0),progress:i32(this.context.ecx,0x418),current:i32(this.context.ecx,0x420),target:i32(this.context.ecx,0x424)});}});
send({kind:'ready',base:mod.base.toString()});
}
install();
"""

def main():
    import frida
    args = sys.argv[1:]
    numbers = [int(a) for a in args if a.isdigit()]
    seconds = numbers[0] if numbers else 30
    target = numbers[1] if len(numbers)>1 else "AGE.EXE"
    OUT.parent.mkdir(parents=True,exist_ok=True); counts={}
    try:
        session=frida.attach(target)
    except frida.ProcessNotFoundError:
        print("[frida] AGE.EXE not found; prior trace preserved")
        return 2
    with TMP.open("w",encoding="utf-8") as f:
        def on_message(msg,data):
            if msg.get("type")=="error": print("[frida-error]",msg.get("description")); return
            if msg.get("type")!="send": return
            row=msg["payload"]
            if row.get("kind")=="ready": print(f"[frida] SFX hooks armed @ {row['base']}"); LIVE.write_text("live",encoding="utf-8"); return
            f.write(json.dumps(row,ensure_ascii=False)+"\n"); f.flush(); n=row.get("name","?");counts[n]=counts.get(n,0)+1
            if n.startswith("op-") or n in {"sfx-load-enter","sfx-start-enter","sfx-release","ds-play","ds-stop","ds-set-volume","ds-set-pan","audio-fade-arm"}:
                print(f"  #{row['seq']:04d} off=0x{row['offset']:05x} {n} ch={row.get('channel','-')} res={row.get('resourceArg',row.get('resource','-'))}")
        try:
            script=session.create_script(JS);script.on("message",on_message);script.load()
        except Exception:
            raise
        print(f"[frida] ARMED for {seconds}s -- start New Game when the title appears.")
        try: time.sleep(seconds)
        except KeyboardInterrupt: pass
        try: session.detach()
        except Exception: pass
    try: LIVE.unlink()
    except OSError: pass
    if counts:
        TMP.replace(OUT)
    else:
        try: TMP.unlink()
        except OSError: pass
    print(f"[trace] wrote {sum(counts.values())} events -> {OUT if counts else '(prior trace preserved)'}")
    print("[trace] "+", ".join(f"{k}={v}" for k,v in sorted(counts.items())))
    return 0 if counts else 3
if __name__=="__main__": raise SystemExit(main())
