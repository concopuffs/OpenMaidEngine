'use strict';
// Frida agent that tools/drive_native.py loads into native AGE. The driver spawns AGE suspended, calls
// init(config) and only then resumes it, so every hook below is in place before AGE's first instruction.
// The modules are hooked as they load (the packed AGE.EXE resolves its imports itself).
//
// Inside AGE only:
//   user32  GetAsyncKeyState  reports exactly the keys and buttons the driver holds
//           GetCursorPos      reports the driver's client point, converted to the screen at call time
//           SetCursorPos      moves the driver's point, never the desktop cursor, and posts the WM_MOUSEMOVE
//                             Windows would send for it
//           MessageBoxA       answers boxes the profile's dialog policy names; shows and reports the rest
//   winmm   joy*              no joystick is connected
//   shell32 SHGetFolderPathA  the folders in config.folders (keyed by CSIDL & 0xff) point into the sandbox
//   AGE.EXE input polling     keeppolling() stops WM_ACTIVATE from closing the polling gate: windowed AGE ignores
//                             polled buttons and the cursor while inactive, and a driven AGE always is. It is
//                             installed once the window exists, because AGE.EXE is unpacked at run time.
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
    const getClientRect = new NativeFunction(module.getExportByName('GetClientRect'), 'int',
        ['pointer', 'pointer'], 'stdcall');
    const postMessage = new NativeFunction(module.getExportByName('PostMessageA'), 'int',
        ['pointer', 'uint', 'uint', 'uint'], 'stdcall');
    const WM_MOUSEMOVE = 0x200;
    const insideClient = point => {
        const rect = Memory.alloc(16);
        if (!getClientRect(hwnd, rect)) return false;
        return point[0] >= rect.readS32() && point[0] < rect.add(8).readS32()
            && point[1] >= rect.add(4).readS32() && point[1] < rect.add(12).readS32();
    };
    // MK_LBUTTON, MK_RBUTTON, MK_SHIFT, MK_CONTROL from the keys the driver holds
    const mouseKeyFlags = () => (held.has(0x01) ? 0x1 : 0) | (held.has(0x02) ? 0x2 : 0)
        | (held.has(0x10) ? 0x4 : 0) | (held.has(0x11) ? 0x8 : 0);
    const lparam = point => ((point[1] & 0xffff) << 16 | (point[0] & 0xffff)) >>> 0;

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
    // them through warps() instead of one message each. Windows also sends a WM_MOUSEMOVE to the window under a
    // moved cursor. AGE's hotspots are hit-tested only on WM_MOUSEMOVE, and its ADV callbacks nudge the cursor by
    // a pixel to re-hit-test, so a moved point inside the client area posts one too.
    Interceptor.replace(module.getExportByName('SetCursorPos'), new NativeCallback((x, y) => {
        if (!hwnd.isNull()) {
            const point = Memory.alloc(8);
            point.writeS32(x);
            point.add(4).writeS32(y);
            screenToClient(hwnd, point);
            const moved = [point.readS32(), point.add(4).readS32()];
            const changed = cursor === null || cursor[0] !== moved[0] || cursor[1] !== moved[1];
            cursor = moved;
            if (changed && insideClient(moved)) postMessage(hwnd, WM_MOUSEMOVE, mouseKeyFlags(), lparam(moved));
        }
        warps.count += 1;
        warps.last = cursor;
        return 1;
    }, 'int', ['int', 'int'], 'stdcall'));

    hookMessageBoxA(module.getExportByName('MessageBoxA'));
}

// MessageBoxA's replacement is C, not a JS NativeCallback: a shown box runs its modal loop inside the call, and
// with a JS frame around that loop AGE died (0xC000041D) as soon as the box painted. JS runs only for the policy
// lookup before the call and the report after it.
let messageBoxHook = null;    // keeps the CModule and its callbacks alive

function hookMessageBoxA(messageBoxA) {
    const lookup = new NativeCallback((text, caption, style) => {
        const body = cstring(text, 4096);
        const title = hex(cstring(caption, 512));
        const policy = config.dialogs.find(dialog => startsWith(body, dialog.match));
        if (policy !== undefined) {
            report('dialog', { text: hex(body), caption: title, style, answered: policy.answer });
            return policy.answer;
        }
        report('dialog', { text: hex(body), caption: title, style, shown: true });
        return 0;
    }, 'int', ['pointer', 'pointer', 'uint']);
    const closed = new NativeCallback((text, result) => {
        report('dialog-closed', { text: hex(cstring(text, 4096)), result });
    }, 'void', ['pointer', 'int']);
    const module = new CModule(`
typedef int (__attribute__((stdcall)) *box_fn)(void *, const char *, const char *, unsigned int);
extern int dialog_lookup(const char *text, const char *caption, unsigned int style);
extern void dialog_closed(const char *text, int result);
box_fn original;

int __attribute__((stdcall)) replacement(void *owner, const char *text, const char *caption, unsigned int style)
{
    int answer = dialog_lookup(text, caption, style);
    if (answer != 0)
        return answer;
    int result = original(owner, text, caption, style);
    dialog_closed(text, result);
    return result;
}
`, { dialog_lookup: lookup, dialog_closed: closed });
    module.original.writePointer(Interceptor.replaceFast(messageBoxA, module.replacement));
    messageBoxHook = { module, lookup, closed };
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

let pollingKept = null;    // { count } once keeppolling() has run

function keepPolling() {
    const facts = config.keep_polling;
    if (facts === null || facts === undefined) return null;
    if (pollingKept === null) {
        const activateReturn = ptr(facts.activate_return);
        pollingKept = { count: 0 };
        // input_set_polling_enabled(this = input manager, disable): thiscall, so args[0] is the stack argument.
        Interceptor.attach(ptr(facts.setter), {
            onEnter(args) {
                if (this.returnAddress.equals(activateReturn) && !args[0].isNull()) {
                    args[0] = ptr(0);
                    pollingKept.count += 1;
                }
            },
        });
    }
    const ctx = ptr(config.engine.ctx_global).readPointer();
    if (!ctx.isNull()) ctx.add(facts.gate).writeU32(1);   // undo a deactivation that came before the hook
    return pollingKept.count;
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
    keeppolling() {
        return keepPolling();
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
