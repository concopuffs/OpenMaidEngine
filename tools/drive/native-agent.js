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
//           window activation in a background session AGE never takes the foreground (keepInBackground)
//   winmm   joy*              no joystick is connected
//   shell32 SHGetFolderPathA  the folders in config.folders (keyed by CSIDL & 0xff) point into the sandbox
//   AGE.EXE input polling     keeppolling() stops WM_ACTIVATE from closing the polling gate: windowed AGE ignores
//                             polled buttons and the cursor while inactive, and a driven AGE always is. It is
//                             installed once the window exists, because AGE.EXE is unpacked at run time.
//   AGE.EXE op 0x60           randseed(seed) replaces the draw op 0x60 takes from the CRT's rand() with the seeded
//                             per-site stream the port computes (VirtualMachine.DeterministicRandom.cs); null
//                             gives the game its own draws back. Installed at the first seed, for the same reason.
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

// Two CModule rules for the hooks below. A CModule exports a 32-bit stdcall function under its decorated name
// (`replacement` as `_replacement@16`). Its own globals are read-only once linked, so a pointer JS fills in lives in
// Memory.alloc storage that the C code declares extern.
function cPointers(...names) {
    const symbols = {};
    for (const name of names) symbols[name] = Memory.alloc(Process.pointerSize);
    return symbols;
}

function cFunction(module, name) {
    const symbol = Object.keys(module).find(key => key === name || key === `_${name}` || key.startsWith(`_${name}@`));
    if (symbol === undefined) throw new Error(`CModule has no ${name}`);
    return module[symbol];
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
    countButtonClicks(module);
    if (config.background) keepInBackground(module);
}

// The driver presses a dialog's button by posting BM_CLICK, which AGE's thread takes only when it next dispatches a
// posted message, as it would a user's click: the 設定 dialog's first VRAM query holds it for about 2 s. This counts
// the BM_CLICKs AGE's thread takes, so the driver can wait for its press. A modal dialog's loop hands them to
// IsDialogMessage, which dispatches them without the exported DispatchMessage; other loops call DispatchMessage. C,
// since both carry every message.
let clickCounter = null;      // keeps the CModule alive

function countButtonClicks(module) {
    const pointers = cPointers('click_count', 'click_window');
    pointers.click_count.writeU32(0);
    pointers.click_window.writePointer(NULL);
    const counter = new CModule(`
#include <gum/guminterceptor.h>

typedef struct { void *window; unsigned int message; } message_head;
extern volatile unsigned int click_count;
extern void * volatile click_window;

static void count(message_head *message)
{
    if (message != 0 && message->message == 0xf5) {                                  /* BM_CLICK */
        click_window = message->window;
        click_count++;
    }
}

void on_dispatch(GumInvocationContext *context)                                        /* DispatchMessage(msg) */
{
    count(gum_invocation_context_get_nth_argument(context, 0));
}

void on_dialog_message(GumInvocationContext *context)                                  /* IsDialogMessage(dlg, msg) */
{
    count(gum_invocation_context_get_nth_argument(context, 1));
}
`, pointers);
    for (const name of ['DispatchMessageA', 'DispatchMessageW'])
        Interceptor.attach(module.getExportByName(name), { onEnter: cFunction(counter, 'on_dispatch') });
    for (const name of ['IsDialogMessageA', 'IsDialogMessageW'])
        Interceptor.attach(module.getExportByName(name), { onEnter: cFunction(counter, 'on_dialog_message') });
    clickCounter = { counter, pointers };
}

function buttonClicks() {
    if (clickCounter === null) return null;
    return { count: clickCounter.pointers.click_count.readU32(),
             window: clickCounter.pointers.click_window.readPointer().toUInt32() };
}

// A driven AGE must not take the user's foreground. AGE never asks for it: it creates its main window with WS_VISIBLE
// and shows it, and Windows makes an activated window the foreground one when its process may set the foreground. A
// process started by one that may set it gets that permission too, and keeps it until it activates a window or the
// user clicks or types. Measured 2026-11-06 with LockSetForegroundWindow(LSFW_LOCK), which succeeds only with the
// permission: a driven AGE kept it through 90 s of user idle time, took the foreground at start in three of three
// sessions, and once that was stopped, its first dialog took it instead in four of four. In a background session:
//   CreateWindowExA/W   a visible top-level window is created under LSFW_LOCK, which keeps its activation inside AGE's
//                       thread and spends the permission; LSFW_UNLOCK follows. If the permission survives, the lock is
//                       kept and retried every second until it is gone.
//   DefWindowProcA      SC_MINIMIZE leaves the minimized window inactive in its thread, as a user's minimize of the
//                       foreground window does: a hidden helper window on AGE's thread takes the activation. Windows
//                       makes a restored window that is still active in its thread the foreground one even without the
//                       permission. SC_RESTORE then restores with SW_SHOWNOACTIVATE.
// AGE's main window is then active in its thread without being the foreground one, as for a native AGE the user has
// switched away from, so Windows' keyboard menu works and nothing AGE does takes the foreground.
const WS_CHILD = 0x40000000;
const WS_VISIBLE = 0x10000000;
const LSFW_LOCK = 1;
const LSFW_UNLOCK = 2;
let lockForeground = null;  // LockSetForegroundWindow
let permission = 'unknown';  // 'none' if AGE never had it; 'held' while the lock outlives an activation; 'spent'
let systemCommandHook = null;  // keeps the CModule alive

// LSFW_LOCK succeeds only while AGE may set the foreground, so a fresh lock means the permission survives.
function permissionSpent() {
    lockForeground(LSFW_UNLOCK);
    if (lockForeground(LSFW_LOCK)) return false;
    permission = 'spent';
    report('foreground', { permission });
    return true;
}

function keepInBackground(module) {
    lockForeground = new NativeFunction(module.getExportByName('LockSetForegroundWindow'), 'int', ['uint'], 'stdcall');

    for (const name of ['CreateWindowExA', 'CreateWindowExW']) {
        Interceptor.attach(module.getExportByName(name), {
            onEnter(args) {
                const style = args[3].toUInt32();
                this.locked = false;
                if (permission !== 'unknown' || (style & WS_CHILD) !== 0 || (style & WS_VISIBLE) === 0) return;
                this.locked = lockForeground(LSFW_LOCK) !== 0;
                if (!this.locked) {
                    permission = 'none';
                    report('foreground', { permission });
                }
            },
            onLeave() {
                if (!this.locked || permissionSpent()) return;
                permission = 'held';
                report('foreground', { permission });
                const timer = setInterval(() => { if (permissionSpent()) clearInterval(timer); }, 1000);
            },
        });
    }

    // Windows' own minimize and restore run inside DefWindowProc, in the kernel, so DefWindowProcA is wrapped before
    // they get there. AGE's window procedure has already seen the command (it clears run-state 0x1000000 on SC_RESTORE). C, not
    // JS, since DefWindowProc carries every message and runs Windows' modal loops.
    const pointers = cPointers('original', 'is_iconic', 'show_window', 'create_window', 'get_active', 'set_active',
        'helper');
    const commands = new CModule(`
typedef long (__attribute__((stdcall)) *defproc_fn)(void *, unsigned int, unsigned int, long);
typedef int (__attribute__((stdcall)) *iconic_fn)(void *);
typedef int (__attribute__((stdcall)) *show_fn)(void *, int);
typedef void *(__attribute__((stdcall)) *create_fn)(unsigned long, const char *, const char *, unsigned long, int, int,
                                                    int, int, void *, void *, void *, void *);
typedef void *(__attribute__((stdcall)) *active_fn)(void);
typedef void *(__attribute__((stdcall)) *set_active_fn)(void *);
extern defproc_fn original;
extern iconic_fn is_iconic;
extern show_fn show_window;
extern create_fn create_window;
extern active_fn get_active;
extern set_active_fn set_active;
extern void *helper;

long __attribute__((stdcall)) replacement(void *window, unsigned int message, unsigned int wparam, long lparam)
{
    unsigned int command = wparam & 0xfff0;
    if (message == 0x112 && command == 0xf020) {                                      /* WM_SYSCOMMAND SC_MINIMIZE */
        long result = original(window, message, wparam, lparam);
        if (is_iconic(window) && get_active() == window) {
            if (helper == 0)                                   /* hidden WS_POPUP, WS_EX_TOOLWINDOW, on AGE's thread */
                helper = create_window(0x80, "STATIC", "", 0x80000000, 0, 0, 0, 0, 0, 0, 0, 0);
            if (helper != 0)
                set_active(helper);
        }
        return result;
    }
    if (message == 0x112 && command == 0xf120 && is_iconic(window)) {                /* WM_SYSCOMMAND SC_RESTORE */
        show_window(window, 4);                                                     /* SW_SHOWNOACTIVATE */
        return 0;
    }
    return original(window, message, wparam, lparam);
}
`, pointers);
    pointers.is_iconic.writePointer(module.getExportByName('IsIconic'));
    pointers.show_window.writePointer(module.getExportByName('ShowWindow'));
    pointers.create_window.writePointer(module.getExportByName('CreateWindowExA'));
    pointers.get_active.writePointer(module.getExportByName('GetActiveWindow'));
    pointers.set_active.writePointer(module.getExportByName('SetActiveWindow'));
    pointers.helper.writePointer(NULL);
    pointers.original.writePointer(Interceptor.replaceFast(module.getExportByName('DefWindowProcA'),
        cFunction(commands, 'replacement')));
    systemCommandHook = { commands, pointers };
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
    const pointers = cPointers('original');
    const module = new CModule(`
typedef int (__attribute__((stdcall)) *box_fn)(void *, const char *, const char *, unsigned int);
extern int dialog_lookup(const char *text, const char *caption, unsigned int style);
extern void dialog_closed(const char *text, int result);
extern box_fn original;

int __attribute__((stdcall)) replacement(void *owner, const char *text, const char *caption, unsigned int style)
{
    int answer = dialog_lookup(text, caption, style);
    if (answer != 0)
        return answer;
    int result = original(owner, text, caption, style);
    dialog_closed(text, result);
    return result;
}
`, Object.assign({ dialog_lookup: lookup, dialog_closed: closed }, pointers));
    pointers.original.writePointer(Interceptor.replaceFast(messageBoxA, cFunction(module, 'replacement')));
    messageBoxHook = { module, lookup, closed, pointers };
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

// Op 0x60's deterministic mode (docs/engine-re.md, "Op 0x60 randomness"). Each call site, the running frame's script
// resource id P and the op's dword offset O, draws MSVC rand() from its own state, first
// seed ^ (P * 0x9E3779B1 + O * 0x85EBCA77) mod 2^32. The handler calls rand() before it fetches an operand, so the
// frame's pc still holds the op's own offset; the probe sits on the instruction after that call and replaces EAX.
let rand = null;          // { seed, draws, sites: Map("P:O" -> { resid, offset, state, draws }) } while seeded
let randProbe = null;
const OP60_RETURN_BYTES = [0x6a, 0x02, 0x8b, 0xce, 0xa3];   // push 2; mov ecx, esi; mov [draw], eax

function randDraw() {
    const engine = config.engine;
    const ctx = ptr(engine.ctx_global).readPointer();
    const frame = ctx.add(ctx.add(engine.cur_ctx_index).readU32() * engine.frame_stride);
    const resid = frame.add(engine.frame_script_resource_id).readU32();
    const offset = ((frame.add(engine.frame_pc).readU32() - frame.add(engine.frame_codebase).readU32()) >>> 2) >>> 0;
    const key = `${resid}:${offset}`;
    let site = rand.sites.get(key);
    if (site === undefined) {
        const mixed = (Math.imul(resid, 0x9e3779b1) + Math.imul(offset, 0x85ebca77)) >>> 0;
        site = { resid, offset, state: (rand.seed ^ mixed) >>> 0, draws: 0 };
        rand.sites.set(key, site);
    }
    site.state = (Math.imul(site.state, 0x343fd) + 0x269ec3) >>> 0;
    site.draws += 1;
    rand.draws += 1;
    return (site.state >>> 16) & 0x7fff;
}

function randSeed(seed) {
    const facts = config.rand;
    if (facts === null || facts === undefined || config.engine === null)
        throw new Error('the profile has no [native.rand] and [native.engine] facts');
    const drawn = rand === null ? { seed: null, draws: 0, sites: [] }
        : { seed: rand.seed, draws: rand.draws,
            sites: Array.from(rand.sites.values(), site => [site.resid, site.offset, site.draws]) };
    if (seed !== null && randProbe === null) {
        const site = ptr(facts.op60_rand_return);
        const found = Array.from(new Uint8Array(site.readByteArray(OP60_RETURN_BYTES.length)));
        if (found.some((byte, index) => byte !== OP60_RETURN_BYTES[index]))
            throw new Error(`op 0x60's rand() call does not return to the expected code at ${site}: ${hex(found)}`);
        // A probe (a bare function), not an onEnter object: Frida would take the site for a function entry and swap
        // [esp], which here is the handler's saved ESI, for its leave trampoline.
        randProbe = Interceptor.attach(site, function () {
            if (rand !== null) this.context.eax = ptr(randDraw());
        });
    }
    rand = seed === null ? null : { seed: seed >>> 0, draws: 0, sites: new Map() };
    return drawn;
}

// Windows' keyboard menu starts only in the active window of AGE's thread. This joins that thread's input queue from
// inside AGE's process and activates the window there. In a background session it runs under LSFW_LOCK, so the
// activation stays inside AGE's thread even if AGE could still set the foreground.
function activateInThread(window) {
    const user32 = Process.getModuleByName('user32.dll');
    const call = (name, ret, args) => new NativeFunction(user32.getExportByName(name), ret, args, 'stdcall');
    const thread = call('GetWindowThreadProcessId', 'uint', ['pointer', 'pointer'])(window, NULL);
    const own = new NativeFunction(Process.getModuleByName('kernel32.dll').getExportByName('GetCurrentThreadId'),
        'uint', [], 'stdcall')();
    const attach = call('AttachThreadInput', 'int', ['uint', 'uint', 'int']);
    const locked = lockForeground !== null && lockForeground(LSFW_LOCK) !== 0;
    try {
        if (!thread || !attach(own, thread, 1)) return false;
        try {
            call('SetActiveWindow', 'pointer', ['pointer'])(window);
        } finally {
            attach(own, thread, 0);
        }
        return true;
    } finally {
        if (locked && permission !== 'held') lockForeground(LSFW_UNLOCK);
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
    activate(value) {
        return activateInThread(ptr(value));
    },
    clicks() {
        return buttonClicks();
    },
    keeppolling() {
        return keepPolling();
    },
    randseed(seed) {
        return randSeed(seed);
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
