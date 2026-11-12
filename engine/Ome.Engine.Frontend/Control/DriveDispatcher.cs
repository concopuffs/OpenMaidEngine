using System.Text.Json;
using System.Text.Json.Nodes;
using Ome.Engine.Localization;

namespace Ome.Engine.Frontend.Control;

public enum DriveButton { Left, Right }

/// <summary>What a game offers the driving protocol. Every member except the identity properties is called on the
/// frontend's main thread, from <see cref="DriveDispatcher.Poll"/>.</summary>
public interface IDriveBackend
{
    string Target { get; }
    string Profile { get; }
    /// <summary>Optional ops and condition kinds this backend supports (§4 "hello").</summary>
    IReadOnlyList<string> Capabilities { get; }
    DriveState CaptureState();
    /// <summary>Coordinates are native canvas pixels (800×600 for Himegari).</summary>
    void Pointer(int x, int y);
    void Button(DriveButton button, bool down, int x, int y);
    void Key(int virtualKey, bool down);
    void Wheel(int notches, int x, int y);
    (int Width, int Height) Screenshot(string path);
    void SetSpeed(double factor);
    void Close();
    /// <summary>Ask to close as the window's close button does (native: an unforced WM_CLOSE and the 終了 dialog).</summary>
    void RequestClose() => throw new DriveProtocolException("close-request is not supported here");
    /// <summary>The open dialogs as {title, text, buttons} objects (capability "dialogs").</summary>
    JsonArray Dialogs() => throw new DriveProtocolException("'dialogs' has no implementation here");
    /// <summary>Press the named button of an open dialog; returns {text, answered} (capability "answer").</summary>
    JsonObject Answer(string button) => throw new DriveProtocolException("'answer' has no implementation here");
    /// <summary>Minimize the game window or restore it, as its title-bar and taskbar buttons do (native: WM_SYSCOMMAND
    /// SC_MINIMIZE / SC_RESTORE); returns {minimized} (capability "window").</summary>
    JsonObject Window(bool minimize) => throw new DriveProtocolException("'window' has no implementation here");
    /// <summary>Choose menu-bar command <paramref name="command"/> as a user does: refresh the menu, refuse a grayed or
    /// missing item, run an enabled one; returns {command, label, enabled, checked} (capability "menu").</summary>
    JsonObject Menu(int command) => throw new DriveProtocolException("'menu' has no implementation here");
    /// <summary>Set one 設定 option and commit it as OK does: the section's menu command opens it, a grayed one fails;
    /// returns {option, value, section} (capability "options").</summary>
    JsonObject Option(string name, int value) => throw new DriveProtocolException("'option' has no implementation here");
    /// <summary>The 設定 options shown, opened from ﾒｯｾｰｼﾞ and cancelled: {options: [{name, section, value, min, max,
    /// step, applies}]} (capability "options").</summary>
    JsonObject Options() => throw new DriveProtocolException("'options' has no implementation here");
    /// <summary>Choose <paramref name="tag"/> (normalized) in the options panel's language list and commit it as 決定
    /// does; returns {language, previous, languages} (capability "language", the port's).</summary>
    JsonObject Language(string tag) => throw new DriveProtocolException("'language' has no implementation here");
    /// <summary>Draw op 0x60 from <paramref name="seed"/>'s per-site streams, or from the game's own generator when
    /// null; returns what op 0x60 drew since the previous call (capability "rand").</summary>
    JsonObject RandSeed(uint? seed) => throw new DriveProtocolException("'rand-seed' has no implementation here");
    /// <summary>The last position the game itself moved the cursor to since the previous call, if any.</summary>
    (int X, int Y)? TakeCursorWarp();
    /// <summary>Where the driven cursor is now, in native canvas pixels (the last pointer, click or warp).</summary>
    (int X, int Y) PointerPosition { get; }
}

/// <summary>Runs one request at a time as a timed operation. The frontend calls <see cref="Poll"/> once per frame;
/// operations that span time (click holds, waits, settle) never block the caller.</summary>
public sealed class DriveDispatcher
{
    public const int PointerLeadMs = 50;
    public const int ReleaseTailMs = 50;
    public const int DefaultClickHoldMs = 150;
    public const int DefaultKeyHoldMs = 80;
    public const int DefaultWaitTimeoutMs = 30_000;
    public const int DefaultPokeEveryMs = 2_000;
    public const int DefaultSettleTimeoutMs = 10_000;
    public const int DefaultSettleQuietMs = 700;
    public const int CloseDelayMs = 100;

    private readonly IDriveBackend _backend;
    private Operation? _active;
    private long? _closeAtMs;

    public DriveDispatcher(IDriveBackend backend) => _backend = backend;

    public bool Busy => _active != null;
    public bool CloseRequested => _closeAtMs != null;

    /// <summary>Start a request; <paramref name="reply"/> receives the response line once it completes.</summary>
    public void Start(DriveRequest request, long nowMs, Action<string> reply)
    {
        if (_active != null)
        {
            reply(DriveReply.Error(request.Id, "another request is still running"));
            return;
        }
        if (_closeAtMs != null)
        {
            reply(DriveReply.Error(request.Id, "the game is closing"));
            return;
        }
        try
        {
            _active = Create(request, nowMs, reply);
        }
        catch (Exception error)
        {
            reply(DriveReply.Error(request.Id, Describe(error)));
            return;
        }
        Poll(nowMs);
    }

    public void Poll(long nowMs)
    {
        if (_closeAtMs is long closeAt && nowMs >= closeAt && _active == null)
        {
            _closeAtMs = long.MaxValue;
            _backend.Close();
            return;
        }
        Operation? active = _active;
        if (active == null) return;
        string? line;
        try
        {
            JsonNode? result = active.Poll(nowMs);
            if (!active.Done) return;
            line = DriveReply.Ok(active.Request.Id, result);
        }
        catch (Exception error)
        {
            // Any failure ends the request with an error reply: an operation that threw is not retried each frame,
            // and the client is not left waiting on a server that stays busy.
            line = DriveReply.Error(active.Request.Id, Describe(error));
        }
        _active = null;
        if (active.Request.Op == "close") _closeAtMs = nowMs + CloseDelayMs;
        active.Reply(line);
    }

    private static string Describe(Exception error)
        => error is DriveProtocolException ? error.Message : $"{error.GetType().Name}: {error.Message}";

    private Operation Create(DriveRequest request, long nowMs, Action<string> reply)
    {
        switch (request.Op)
        {
            case "state":
                return Immediate(request, reply, () => _backend.CaptureState().ToJson());
            case "pointer":
            {
                int x = request.Int("x"), y = request.Int("y");
                return Immediate(request, reply, () => { _backend.Pointer(x, y); return null; });
            }
            case "click":
                return new ClickOperation(request, reply, _backend, nowMs, request.Int("x"), request.Int("y"),
                    ParseButton(request.Text("button", "left")), request.Int("hold_ms", DefaultClickHoldMs));
            case "key":
                return new KeyOperation(request, reply, _backend, nowMs, request.Int("vk"),
                    request.Int("hold_ms", DefaultKeyHoldMs), request.Bool("alt"));
            case "wheel":
            {
                // x/y default to the driven cursor, which the game may have moved since the client last saw it.
                (int pointerX, int pointerY) = _backend.PointerPosition;
                int notches = request.Int("notches"), x = request.Int("x", pointerX), y = request.Int("y", pointerY);
                return Immediate(request, reply, () => { _backend.Wheel(notches, x, y); return null; });
            }
            case "wait":
            {
                DriveCondition condition = DriveCondition.Parse(request.Element("until"));
                RequireCapabilities(condition);
                (int X, int Y, int EveryMs)? poke = null;
                if (request.Has("poke"))
                {
                    var pokeRequest = request with { Body = request.Element("poke") };
                    poke = (pokeRequest.Int("x"), pokeRequest.Int("y"),
                            pokeRequest.Int("every_ms", DefaultPokeEveryMs));
                    if (poke.Value.EveryMs <= 0) throw new DriveProtocolException("poke every_ms must be positive");
                }
                return new WaitOperation(request, reply, _backend, nowMs, condition,
                    request.Int("timeout_ms", DefaultWaitTimeoutMs), poke);
            }
            case "settle":
                return new SettleOperation(request, reply, _backend, nowMs,
                    request.Int("timeout_ms", DefaultSettleTimeoutMs), request.Int("quiet_ms", DefaultSettleQuietMs));
            case "shot":
            {
                string path = request.Text("path");
                // The game's working directory is its own business (Godot runs from the project folder).
                if (!Path.IsPathFullyQualified(path))
                    throw new DriveProtocolException($"shot needs an absolute path, got '{path}'");
                return Immediate(request, reply, () =>
                {
                    (int width, int height) = _backend.Screenshot(path);
                    return new JsonObject { ["path"] = path, ["width"] = width, ["height"] = height };
                });
            }
            case "speed":
            {
                RequireCapability("speed");
                double factor = request.Number("factor");
                if (!double.IsFinite(factor) || factor < 0.05 || factor > 8)
                    throw new DriveProtocolException("speed factor must be between 0.05 and 8");
                return Immediate(request, reply, () => { _backend.SetSpeed(factor); return null; });
            }
            case "rand-seed":
            {
                RequireCapability("rand");
                JsonElement value = request.Element("seed");
                uint? seed = value.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.Number when value.TryGetUInt32(out uint number) => number,
                    _ => throw new DriveProtocolException("rand-seed takes a seed from 0 to 0xffffffff, or null"),
                };
                return Immediate(request, reply, () => _backend.RandSeed(seed));
            }
            case "close":
                return Immediate(request, reply, () => null);
            case "close-request":
                return Immediate(request, reply, () => { _backend.RequestClose(); return null; });
            case "dialogs":
                RequireCapability("dialogs");
                return Immediate(request, reply, () => new JsonObject { ["dialogs"] = _backend.Dialogs() });
            case "answer":
            {
                RequireCapability("answer");
                string button = request.Text("button");
                return Immediate(request, reply, () => _backend.Answer(button));
            }
            case "window":
            {
                RequireCapability("window");
                bool minimize = request.Text("state") switch
                {
                    "minimize" => true,
                    "restore" => false,
                    var other => throw new DriveProtocolException($"window state must be minimize or restore, not '{other}'"),
                };
                return Immediate(request, reply, () => _backend.Window(minimize));
            }
            case "menu":
            {
                RequireCapability("menu");
                int command = request.Int("command");
                if (command is <= 0 or > 0xffff) throw new DriveProtocolException("menu command ids are 1..0xffff");
                return Immediate(request, reply, () => _backend.Menu(command));
            }
            case "option":
            {
                RequireCapability("options");
                string name = request.Text("name");
                int value = request.Int("value");
                return Immediate(request, reply, () => _backend.Option(name, value));
            }
            case "options":
                RequireCapability("options");
                return Immediate(request, reply, () => _backend.Options());
            case "language":
            {
                RequireCapability("language");
                string tag = LanguageTag.Normalize(request.Text("tag"));
                if (!LanguageTag.IsWellFormed(tag))
                    throw new DriveProtocolException($"'{tag}' is not a language tag such as ja, en or pt-br");
                return Immediate(request, reply, () => _backend.Language(tag));
            }
            default:
                throw new DriveProtocolException(
                    $"unknown op '{request.Op}' (the {_backend.Target} supports state, pointer, click, key, wheel, " +
                    $"wait, settle, shot, close, close-request{string.Concat(_backend.Capabilities.Select(c => ", " + c))})");
        }
    }

    private void RequireCapabilities(DriveCondition condition)
    {
        if (condition.Waiting != null) RequireCapability("waiting");
    }

    private void RequireCapability(string capability)
    {
        if (!_backend.Capabilities.Contains(capability))
            throw new DriveProtocolException($"'{capability}' is not supported by the {_backend.Target}");
    }

    private static DriveButton ParseButton(string text) => text switch
    {
        "left" => DriveButton.Left,
        "right" => DriveButton.Right,
        _ => throw new DriveProtocolException($"button must be left or right, got '{text}'"),
    };

    private static Operation Immediate(DriveRequest request, Action<string> reply, Func<JsonNode?> run)
        => new ImmediateOperation(request, reply, run);

    // ------------------------------------------------------------------------------------------- operations

    private abstract class Operation(DriveRequest request, Action<string> reply)
    {
        public DriveRequest Request { get; } = request;
        public Action<string> Reply { get; } = reply;
        public bool Done { get; protected set; }
        public abstract JsonNode? Poll(long nowMs);
    }

    private sealed class ImmediateOperation(DriveRequest request, Action<string> reply, Func<JsonNode?> run)
        : Operation(request, reply)
    {
        public override JsonNode? Poll(long nowMs)
        {
            Done = true;
            return run();
        }
    }

    /// <summary>Pointer to the target, a short lead so hover logic sees it, press, hold, release, short tail.</summary>
    private sealed class ClickTimeline(IDriveBackend backend, long startMs, int x, int y, DriveButton button,
                                       int holdMs)
    {
        private int _phase;
        public bool Done => _phase == 4;

        public void Poll(long nowMs)
        {
            long pressAt = startMs + PointerLeadMs, releaseAt = pressAt + Math.Max(0, holdMs);
            if (_phase == 0) { backend.Pointer(x, y); _phase = 1; }
            if (_phase == 1 && nowMs >= pressAt) { backend.Button(button, true, x, y); _phase = 2; }
            if (_phase == 2 && nowMs >= releaseAt) { backend.Button(button, false, x, y); _phase = 3; }
            if (_phase == 3 && nowMs >= releaseAt + ReleaseTailMs) _phase = 4;
        }
    }

    private sealed class ClickOperation : Operation
    {
        private readonly IDriveBackend _backend;
        private readonly ClickTimeline _timeline;
        private readonly int _x, _y;

        public ClickOperation(DriveRequest request, Action<string> reply, IDriveBackend backend, long nowMs,
                              int x, int y, DriveButton button, int holdMs) : base(request, reply)
        {
            if (holdMs < 0) throw new DriveProtocolException("hold_ms must not be negative");
            _backend = backend;
            _x = x; _y = y;
            backend.TakeCursorWarp();
            _timeline = new ClickTimeline(backend, nowMs, x, y, button, holdMs);
        }

        public override JsonNode? Poll(long nowMs)
        {
            _timeline.Poll(nowMs);
            if (!_timeline.Done) return null;
            Done = true;
            var result = new JsonObject { ["x"] = _x, ["y"] = _y };
            if (_backend.TakeCursorWarp() is (int warpX, int warpY))
                result["cursor_moved_to"] = new JsonArray(warpX, warpY);
            return result;
        }
    }

    /// <summary>A key press. With <c>alt</c>, Alt goes down first and up last, as a chord (Alt+Enter, Alt+M).</summary>
    private sealed class KeyOperation : Operation
    {
        private const int VirtualKeyMenu = 0x12;   // VK_MENU, Alt
        private readonly IDriveBackend _backend;
        private readonly int _virtualKey;
        private readonly bool _alt;
        private readonly long _releaseAt;
        private int _phase;

        public KeyOperation(DriveRequest request, Action<string> reply, IDriveBackend backend, long nowMs,
                            int virtualKey, int holdMs, bool alt) : base(request, reply)
        {
            if (virtualKey is <= 0 or > 0xff) throw new DriveProtocolException($"vk 0x{virtualKey:x} is not a key");
            if (holdMs < 0) throw new DriveProtocolException("hold_ms must not be negative");
            if (alt && virtualKey is VirtualKeyMenu or 0xa4 or 0xa5)
                throw new DriveProtocolException("alt holds Alt already: press Alt alone without it");
            _backend = backend;
            _virtualKey = virtualKey;
            _alt = alt;
            _releaseAt = nowMs + holdMs;
        }

        public override JsonNode? Poll(long nowMs)
        {
            if (_phase == 0)
            {
                if (_alt) _backend.Key(VirtualKeyMenu, true);
                _backend.Key(_virtualKey, true);
                _phase = 1;
            }
            if (_phase == 1 && nowMs >= _releaseAt)
            {
                _backend.Key(_virtualKey, false);
                if (_alt) _backend.Key(VirtualKeyMenu, false);
                _phase = 2;
            }
            if (_phase == 2 && nowMs >= _releaseAt + ReleaseTailMs) Done = true;
            return null;
        }
    }

    private sealed class WaitOperation(DriveRequest request, Action<string> reply, IDriveBackend backend,
                                       long startMs, DriveCondition condition, int timeoutMs,
                                       (int X, int Y, int EveryMs)? poke) : Operation(request, reply)
    {
        private ClickTimeline? _poke;
        private long _nextPokeAt = long.MinValue;
        private int _pokes;

        public override JsonNode? Poll(long nowMs)
        {
            if (_poke != null)
            {
                _poke.Poll(nowMs);
                if (_poke.Done) _poke = null;
            }
            DriveState state = backend.CaptureState();
            if (condition.Matches(state) && _poke == null)
            {
                Done = true;
                JsonObject result = state.ToJson();
                result["elapsed_ms"] = nowMs - startMs;
                if (poke != null) result["pokes"] = _pokes;
                return result;
            }
            if (nowMs - startMs >= timeoutMs)
                throw new DriveProtocolException(
                    $"timed out after {timeoutMs} ms; now at {state.Top?.Location ?? "no frame"} " +
                    $"waiting={state.Waiting ?? "running"}");
            if (poke is (int x, int y, int everyMs) && _poke == null && nowMs >= _nextPokeAt)
            {
                _poke = new ClickTimeline(backend, nowMs, x, y, DriveButton.Left, DefaultClickHoldMs);
                _poke.Poll(nowMs);
                _pokes++;
                _nextPokeAt = nowMs + everyMs;
            }
            return null;
        }
    }

    /// <summary>Settled = waiting for the player (an input wait or a polled menu) with no text reveal or screen
    /// transition running, held for quiet_ms. A timeout is not an error: the result says settled=false.</summary>
    private sealed class SettleOperation(DriveRequest request, Action<string> reply, IDriveBackend backend,
                                         long startMs, int timeoutMs, int quietMs) : Operation(request, reply)
    {
        private long? _calmSince;

        public override JsonNode? Poll(long nowMs)
        {
            DriveState state = backend.CaptureState();
            bool calm = state.Waiting is DriveWaiting.Input or DriveWaiting.Menu && !state.Busy;
            _calmSince = calm ? _calmSince ?? nowMs : null;
            bool settled = _calmSince is long since && nowMs - since >= quietMs;
            if (!settled && nowMs - startMs < timeoutMs) return null;
            Done = true;
            return new JsonObject
            {
                ["settled"] = settled,
                ["elapsed_ms"] = nowMs - startMs,
                ["state"] = state.ToJson(),
            };
        }
    }
}
