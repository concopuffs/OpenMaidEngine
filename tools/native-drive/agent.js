'use strict';
// Frida agent that tools/native_drive.py loads into native AGE. The driver spawns AGE suspended, calls
// init(config) and only then resumes it, so every hook below is in place before AGE's first instruction.
// The modules are hooked as they load (the packed AGE.EXE resolves its imports itself).
//
// Inside AGE only:
//   user32  GetAsyncKeyState  reports exactly the keys and buttons the driver holds
//           GetCursorPos      reports the driver's client point, converted to the screen at call time
//           SetCursorPos      moves the driver's point, never the desktop cursor
//           MessageBoxA       answers boxes the profile's dialog policy names; shows and reports the rest
//   winmm   joy*              no joystick is connected
//   shell32 SHGetFolderPathA  the folders in config.folders (keyed by CSIDL & 0xff) point into the sandbox
//
// Facts behind each hook: docs/engine-re.md, "Where native input comes from" and "Driving native AGE".

const held = new Set();
let hwnd = ptr(0);
let cursor = null;               // the driver's client point [x, y]; null parks the cursor off-screen
let config = null;
const hooked = new Set();
let warps = { count: 0, last: null };   // SetCursorPos calls since the driver last asked
const OFFSCREEN = -32000;

function report(kind, fields) {
    send(Object.assign({ kind }, fields || {}));
}

function cstring(pointer, limit) {
    const bytes = [];
    if (pointer.isNull()) return bytes;
    for (let i = 0; i < limit; i++) {
        const value = pointer.add(i).readU8();
        if (value === 0) break;
        bytes.push(value);
    }
    return bytes;
}

function hex(bytes) {
    return bytes.map(value => ('0' + value.toString(16)).slice(-2)).join('');
}

function startsWith(bytes, prefix) {
    if (bytes.length < prefix.length) return false;
    for (let i = 0; i < prefix.length; i++) if (bytes[i] !== prefix[i]) return false;
    return true;
}

function hookUser32(module) {
    const clientToScreen = new NativeFunction(module.getExportByName('ClientToScreen'), 'int',
        ['pointer', 'pointer'], 'stdcall');
    const screenToClient = new NativeFunction(module.getExportByName('ScreenToClient'), 'int',
        ['pointer', 'pointer'], 'stdcall');

    Interceptor.replace(module.getExportByName('GetAsyncKeyState'), new NativeCallback(
        vk => held.has(vk & 0xff) ? 0x8000 : 0, 'int', ['int'], 'stdcall'));

    Interceptor.replace(module.getExportByName('GetCursorPos'), new NativeCallback(point => {
        if (point.isNull()) return 0;
        if (cursor === null || hwnd.isNull()) {
            point.writeS32(OFFSCREEN);
            point.add(4).writeS32(OFFSCREEN);
            return 1;
        }
        point.writeS32(cursor[0]);
        point.add(4).writeS32(cursor[1]);
        clientToScreen(hwnd, point);
        return 1;
    }, 'int', ['pointer'], 'stdcall'));

    // AGE glides the cursor onto a dialog's default button with a run of SetCursorPos calls; the driver collects
    // them through warps() instead of one message each.
    Interceptor.replace(module.getExportByName('SetCursorPos'), new NativeCallback((x, y) => {
        if (!hwnd.isNull()) {
            const point = Memory.alloc(8);
            point.writeS32(x);
            point.add(4).writeS32(y);
            screenToClient(hwnd, point);
            cursor = [point.readS32(), point.add(4).readS32()];
        }
        warps.count += 1;
        warps.last = cursor;
        return 1;
    }, 'int', ['int', 'int'], 'stdcall'));

    const messageBoxA = module.getExportByName('MessageBoxA');
    const showMessageBox = new NativeFunction(messageBoxA, 'int', ['pointer', 'pointer', 'pointer', 'uint'], 'stdcall');
    Interceptor.replace(messageBoxA, new NativeCallback((owner, text, caption, style) => {
        const body = cstring(text, 4096);
        const title = hex(cstring(caption, 512));
        const policy = config.dialogs.find(dialog => startsWith(body, dialog.match));
        if (policy !== undefined) {
            report('dialog', { text: hex(body), caption: title, style, answered: policy.answer });
            return policy.answer;
        }
        report('dialog', { text: hex(body), caption: title, style, shown: true });
        const result = showMessageBox(owner, text, caption, style);   // calls past the replacement
        report('dialog-closed', { text: hex(body), result });
        return result;
    }, 'int', ['pointer', 'pointer', 'pointer', 'uint'], 'stdcall'));
}

function hookWinmm(module) {
    // joyGetNumDevs is not hooked: on Windows 10 it is the 4-byte constant `push 0x10; pop eax; ret`, too short to
    // patch, and it reports slots rather than devices. A slot with no device answers JOYERR_UNPLUGGED.
    const failed = [];
    const replace = (name, value, argumentTypes) => {
        const target = module.findExportByName(name);
        if (target === null) return;
        try {
            Interceptor.replace(target, new NativeCallback(() => value, 'uint', argumentTypes, 'stdcall'));
        } catch (error) {
            failed.push(`${name}: ${error}`);
        }
    };
    replace('joyGetPosEx', 167, ['uint', 'pointer']);              // JOYERR_UNPLUGGED
    replace('joyGetPos', 167, ['uint', 'pointer']);
    replace('joyGetDevCapsA', 167, ['uint', 'pointer', 'uint']);
    if (failed.length) throw new Error(failed.join('; '));
}

function hookShell32(module) {
    const target = module.findExportByName('SHGetFolderPathA');
    if (target === null) return;
    Interceptor.attach(target, {
        onEnter(args) {
            this.csidl = args[1].toInt32();
            this.out = args[4];
        },
        onLeave(result) {
            const folder = config.folders === null ? undefined : config.folders[String(this.csidl & 0xff)];
            if (folder === undefined || this.out.isNull()) {
                report('folder', { csidl: this.csidl, redirected: false });
                return;
            }
            this.out.writeUtf8String(folder);            // the driver only passes ASCII paths
            result.replace(ptr(0));                     // S_OK
            report('folder', { csidl: this.csidl, redirected: true, path: folder });
        },
    });
}

const HOOKS = { 'user32.dll': hookUser32, 'winmm.dll': hookWinmm, 'shell32.dll': hookShell32 };

function hookModule(module) {
    const name = module.name.toLowerCase();
    const hook = HOOKS[name];
    if (hook === undefined || hooked.has(name) || config === null) return;
    hooked.add(name);
    try {
        hook(module);
        report('hooked', { module: module.name });
    } catch (error) {
        report('hook-error', { module: module.name, error: String(error) });
    }
}

function frameState() {
    const engine = config.engine;
    const ctx = ptr(engine.ctx_global).readPointer();
    if (ctx.isNull()) return { ctx: '0x0', current: null, frames: [] };
    const current = ctx.add(engine.cur_ctx_index).readU32();
    const frames = [];
    for (let index = 0; index <= Math.min(current, engine.frame_count - 1); index++) {
        const frame = ctx.add(index * engine.frame_stride);
        frames.push({
            index,
            codebase: frame.add(engine.frame_codebase).readU32(),
            pc: frame.add(engine.frame_pc).readU32(),
            resid: frame.add(engine.frame_script_resource_id).readU32(),
        });
    }
    return { ctx: ctx.toString(), current, frames };
}

rpc.exports = {
    init(value) {
        config = value;
        Process.enumerateModules().forEach(hookModule);
        Process.attachModuleObserver({ onAdded: hookModule });
        return Array.from(hooked);
    },
    hooked() {
        return Array.from(hooked);
    },
    setwindow(value) {
        hwnd = ptr(value);
    },
    setcursor(x, y) {
        cursor = [x, y];
    },
    setkey(vk, down) {
        if (down) held.add(vk); else held.delete(vk);
        return Array.from(held);
    },
    release() {
        held.clear();
    },
    warps() {
        const taken = warps;
        warps = { count: 0, last: null };
        return taken;
    },
    state() {
        if (config === null || config.engine === null) return null;
        try {
            return frameState();
        } catch (error) {
            return { error: String(error) };
        }
    },
};
