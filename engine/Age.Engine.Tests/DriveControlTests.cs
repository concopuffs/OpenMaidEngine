using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Age.Engine.Frontend.Control;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

/// <summary>The game-driving control channel (docs/superpowers/specs/2026-11-03-game-driving-design.md).</summary>
public class DriveControlTests
{
    private sealed class FakeBackend : IDriveBackend
    {
        public string Target => "port";
        public string Profile => "test";
        public IReadOnlyList<string> Capabilities { get; set; } = new[] { "speed", "waiting" };
        public readonly List<string> Events = new();
        public long Now;
        public Func<DriveState> State = () => new DriveState(Array.Empty<DriveFrame>(), null);
        public double Speed = 1;
        public int Closes;
        public (int X, int Y)? Warp;

        public DriveState CaptureState() => State();
        public void Pointer(int x, int y) => Events.Add($"{Now}:pointer {x},{y}");
        public void Button(DriveButton button, bool down, int x, int y)
            => Events.Add($"{Now}:{button.ToString().ToLowerInvariant()} {(down ? "down" : "up")} {x},{y}");
        public void Key(int virtualKey, bool down) => Events.Add($"{Now}:key 0x{virtualKey:x} {(down ? "down" : "up")}");
        public void Wheel(int notches, int x, int y) => Events.Add($"{Now}:wheel {notches} {x},{y}");
        public (int Width, int Height) Screenshot(string path) { Events.Add($"{Now}:shot {path}"); return (800, 600); }
        public void SetSpeed(double factor) => Speed = factor;
        public void Close() => Closes++;
        public (int X, int Y)? TakeCursorWarp() { var warp = Warp; Warp = null; return warp; }
        public (int X, int Y) PointerPosition { get; set; } = (0, 0);
        public int CloseRequests;
        public string? Answered;
        public void RequestClose() => CloseRequests++;
        public JsonArray Dialogs() => new() { new JsonObject { ["title"] = "exit" } };
        public JsonObject Answer(string button) { Answered = button; return new JsonObject { ["answered"] = button }; }
        public bool? Minimized;
        public JsonObject Window(bool minimize) { Minimized = minimize; return new JsonObject { ["minimized"] = minimize }; }
        public readonly List<uint?> Seeds = new();
        public JsonObject RandSeed(uint? seed) { Seeds.Add(seed); return new JsonObject { ["seed"] = seed }; }
        public int? Chosen;
        public JsonObject Menu(int command)
        {
            if (command == 0x9c46) throw new DriveProtocolException("menu command 0x9c46 is grayed");
            Chosen = command;
            return new JsonObject { ["command"] = command, ["enabled"] = true };
        }
    }

    private static DriveState At(string location, string? waiting = null, bool busy = false, params string[] outer)
        => new(outer.Append(location).Select(DriveFrame.Parse).ToArray(), waiting, busy);

    /// <summary>Start a request at t=0 and poll every <paramref name="stepMs"/> until it replies.</summary>
    private static JsonObject Run(DriveDispatcher dispatcher, FakeBackend backend, string json, int stepMs = 10,
                                  long limitMs = 120_000, Action<long>? onTick = null)
    {
        string? line = null;
        backend.Now = 0;
        onTick?.Invoke(0);
        dispatcher.Start(DriveRequest.Parse(json), 0, reply => line = reply);
        for (long now = stepMs; line == null && now <= limitMs; now += stepMs)
        {
            backend.Now = now;
            onTick?.Invoke(now);
            dispatcher.Poll(now);
        }
        Assert.NotNull(line);
        return JsonNode.Parse(line!)!.AsObject();
    }

    // ------------------------------------------------------------------------------------------- model

    [Fact]
    public void LocationsNormalizeScriptNamesAndParseHexOrDecimalOffsets()
    {
        Assert.Equal(new DriveFrame("SC0000", 0x8a9), DriveFrame.Parse("sc0000.bin@0x8A9"));
        Assert.Equal(new DriveFrame("SYSTEM4", 1143), DriveFrame.Parse("SYSTEM4@1143"));
        Assert.Equal("SC0000@0x8a9", new DriveFrame("SC0000", 0x8a9).Location);
        Assert.Throws<DriveProtocolException>(() => DriveFrame.Parse("SC0000"));
        Assert.Throws<DriveProtocolException>(() => DriveFrame.Parse("SC0000@zz"));
    }

    [Fact]
    public void VmFramesUseNativeConventionCallersAtTheirCallSiteInnermostAtResume()
    {
        var frames = DriveFrame.FromVm(new[]
        {
            new VmFrameLocation("SYSTEM4.BIN", 0x47a, 0x477),
            new VmFrameLocation("SC0000.BIN", 0x8a9, 0x8a6),
        });
        Assert.Equal(new[] { "SYSTEM4@0x477", "SC0000@0x8a9" }, frames.Select(frame => frame.Location));
    }

    [Fact]
    public void ConditionsRequireEveryKeyTheyName()
    {
        DriveState state = At("SC0000@0x8a9", DriveWaiting.Input, false, "SYSTEM4@0x477");
        static DriveCondition Condition(string json) => DriveCondition.Parse(JsonDocument.Parse(json).RootElement);

        Assert.True(Condition("""{"top":"SC0000@0x8a9"}""").Matches(state));
        Assert.True(Condition("""{"at":"SYSTEM4@0x477"}""").Matches(state));
        Assert.False(Condition("""{"top":"SYSTEM4@0x477"}""").Matches(state));
        Assert.True(Condition("""{"script":"system4.bin"}""").Matches(state));
        Assert.True(Condition("""{"top":"SC0000@0x8a9","waiting":"input"}""").Matches(state));
        Assert.False(Condition("""{"top":"SC0000@0x8a9","waiting":"menu"}""").Matches(state));
        Assert.Throws<DriveProtocolException>(() => Condition("""{"waiting":"idle"}"""));
        Assert.Throws<DriveProtocolException>(() => Condition("""{"page":3}"""));
        Assert.Throws<DriveProtocolException>(() => Condition("{}"));
    }

    [Fact]
    public void StateJsonCarriesFramesTopLocationAndWaiting()
    {
        JsonObject json = At("SC0000@0x8a9", DriveWaiting.Input, false, "SYSTEM4@0x477").ToJson();
        Assert.Equal("SC0000@0x8a9", (string?)json["location"]);
        Assert.Equal("input", (string?)json["waiting"]);
        Assert.Equal(2, json["frames"]!.AsArray().Count);
        Assert.Equal(0x477, (int)json["frames"]![0]!["offset"]!);
    }

    [Fact]
    public void RequestsNeedAnOpAndValidJson()
    {
        Assert.Throws<DriveProtocolException>(() => DriveRequest.Parse("{\"id\":1}"));
        Assert.Throws<DriveProtocolException>(() => DriveRequest.Parse("not json"));
        Assert.Equal("state", DriveRequest.Parse("{\"id\":4,\"op\":\"state\"}").Op);
    }

    // ------------------------------------------------------------------------------------------- dispatcher

    [Fact]
    public void ClickMovesFirstThenPressesHoldsAndReleases()
    {
        var backend = new FakeBackend();
        var reply = Run(new DriveDispatcher(backend), backend,
            """{"id":7,"op":"click","x":400,"y":302,"hold_ms":150}""",
            onTick: now => { if (now == 120) backend.Warp = (355, 299); });
        Assert.True((bool)reply["ok"]!);
        Assert.Equal(7, (long)reply["id"]!);
        Assert.Equal(new[] { "0:pointer 400,302", "50:left down 400,302", "200:left up 400,302" }, backend.Events);
        Assert.Equal(355, (int)reply["result"]!["cursor_moved_to"]![0]!);
    }

    [Fact]
    public void RightClickAndKeyHoldsUseTheirOwnChannels()
    {
        var backend = new FakeBackend();
        var dispatcher = new DriveDispatcher(backend);
        Run(dispatcher, backend, """{"id":1,"op":"click","x":10,"y":20,"button":"right","hold_ms":0}""");
        Run(dispatcher, backend, """{"id":2,"op":"key","vk":13,"hold_ms":80}""");
        Assert.Equal(new[]
        {
            "0:pointer 10,20", "50:right down 10,20", "50:right up 10,20", "0:key 0xd down", "80:key 0xd up",
        }, backend.Events);
        Assert.False((bool)Run(dispatcher, backend, """{"id":3,"op":"click","x":1,"y":1,"button":"middle"}""")["ok"]!);
    }

    [Fact]
    public void AnAltKeyChordHoldsAltAroundTheKey()
    {
        var backend = new FakeBackend();
        var dispatcher = new DriveDispatcher(backend);
        Run(dispatcher, backend, """{"id":1,"op":"key","vk":77,"hold_ms":80,"alt":true}""");
        Assert.Equal(new[] { "0:key 0x12 down", "0:key 0x4d down", "80:key 0x4d up", "80:key 0x12 up" },
                     backend.Events);
        Assert.False((bool)Run(dispatcher, backend, """{"id":2,"op":"key","vk":18,"alt":true}""")["ok"]!);
        Assert.False((bool)Run(dispatcher, backend, """{"id":3,"op":"key","vk":77,"alt":1}""")["ok"]!);
    }

    [Fact]
    public void WaitReturnsTheMatchingStateAndPokesUntilItMatches()
    {
        var backend = new FakeBackend();
        bool advanced = false;
        backend.State = () => advanced
            ? At("SC0000@0x8a9", DriveWaiting.Input)
            : At("TITLE@0x21", DriveWaiting.Menu);
        var dispatcher = new DriveDispatcher(backend);

        var immediate = Run(dispatcher, backend, """{"id":1,"op":"wait","until":{"script":"TITLE"}}""");
        Assert.Equal("TITLE@0x21", (string?)immediate["result"]!["location"]);
        Assert.Equal(0, (long)immediate["result"]!["elapsed_ms"]!);

        backend.Events.Clear();
        var poked = Run(dispatcher, backend,
            """{"id":2,"op":"wait","until":{"top":"SC0000@0x8a9","waiting":"input"},"poke":{"x":400,"y":300,"every_ms":1000}}""",
            onTick: now => advanced = backend.Events.Count(e => e.Contains("left up")) >= 2);
        Assert.True((bool)poked["ok"]!);
        Assert.Equal(2, (int)poked["result"]!["pokes"]!);
        Assert.Contains("1000:pointer 400,300", backend.Events);
    }

    [Fact]
    public void WaitTimesOutWithTheCurrentLocation()
    {
        var backend = new FakeBackend { State = () => At("TITLE@0x21", DriveWaiting.Menu) };
        var reply = Run(new DriveDispatcher(backend), backend,
            """{"id":3,"op":"wait","until":{"script":"SC0000"},"timeout_ms":500}""");
        Assert.False((bool)reply["ok"]!);
        Assert.Contains("TITLE@0x21", (string?)reply["error"]);
        Assert.Contains("waiting=menu", (string?)reply["error"]);
    }

    [Fact]
    public void RandSeedTakesASeedOrNullAndNeedsTheCapability()
    {
        var backend = new FakeBackend { Capabilities = new[] { "rand" } };
        var dispatcher = new DriveDispatcher(backend);

        Assert.Equal(7u, (uint)Run(dispatcher, backend, """{"id":1,"op":"rand-seed","seed":7}""")["result"]!["seed"]!);
        Assert.True((bool)Run(dispatcher, backend, """{"id":2,"op":"rand-seed","seed":4294967295}""")["ok"]!);
        Assert.True((bool)Run(dispatcher, backend, """{"id":3,"op":"rand-seed","seed":null}""")["ok"]!);
        Assert.Equal(new uint?[] { 7, 0xffffffff, null }, backend.Seeds);
        foreach (string bad in new[] { "-1", "4294967296", "1.5", "\"1\"" })
            Assert.Contains("rand-seed takes a seed",
                (string?)Run(dispatcher, backend, $$"""{"id":4,"op":"rand-seed","seed":{{bad}}}""")["error"]);
        Assert.Contains("needs 'seed'", (string?)Run(dispatcher, backend, """{"id":5,"op":"rand-seed"}""")["error"]);

        var without = new FakeBackend { Capabilities = Array.Empty<string>() };
        Assert.Contains("'rand' is not supported", (string?)Run(new DriveDispatcher(without), without,
            """{"id":6,"op":"rand-seed","seed":1}""")["error"]);
        Assert.Empty(without.Seeds);
    }

    [Fact]
    public void BackendsWithoutTheCapabilityRejectWaitingConditionsAndSpeed()
    {
        var backend = new FakeBackend { Capabilities = Array.Empty<string>() };
        var dispatcher = new DriveDispatcher(backend);
        var waiting = Run(dispatcher, backend, """{"id":1,"op":"wait","until":{"waiting":"input"}}""");
        Assert.Contains("not supported by the port", (string?)waiting["error"]);
        Assert.False((bool)Run(dispatcher, backend, """{"id":2,"op":"speed","factor":2}""")["ok"]!);
        Assert.Contains("'dialogs' is not supported",
            (string?)Run(dispatcher, backend, """{"id":3,"op":"dialogs"}""")["error"]);
        Assert.Contains("unknown op 'fly'", (string?)Run(dispatcher, backend, """{"id":4,"op":"fly"}""")["error"]);
    }

    [Fact]
    public void CloseRequestDialogsAndAnswerReachTheBackend()
    {
        var backend = new FakeBackend { Capabilities = new[] { "dialogs", "answer" } };
        var dispatcher = new DriveDispatcher(backend);
        Assert.True((bool)Run(dispatcher, backend, """{"id":1,"op":"close-request"}""")["ok"]!);
        Assert.Equal(1, backend.CloseRequests);
        var dialogs = Run(dispatcher, backend, """{"id":2,"op":"dialogs"}""");
        Assert.Equal("exit", (string?)dialogs["result"]!["dialogs"]![0]!["title"]);
        var answer = Run(dispatcher, backend, """{"id":3,"op":"answer","button":"OK"}""");
        Assert.Equal("OK", (string?)answer["result"]!["answered"]);
        Assert.Equal("OK", backend.Answered);
        Assert.Equal(0, backend.Closes);
    }

    [Fact]
    public void WindowMinimizesOrRestoresThroughTheBackendAndNeedsItsCapability()
    {
        var backend = new FakeBackend { Capabilities = new[] { "window" } };
        var dispatcher = new DriveDispatcher(backend);
        var minimized = Run(dispatcher, backend, """{"id":1,"op":"window","state":"minimize"}""");
        Assert.True((bool)minimized["result"]!["minimized"]!);
        Assert.True(backend.Minimized);
        Run(dispatcher, backend, """{"id":2,"op":"window","state":"restore"}""");
        Assert.False(backend.Minimized);
        Assert.Contains("minimize or restore",
            (string?)Run(dispatcher, backend, """{"id":3,"op":"window","state":"maximize"}""")["error"]);

        var without = new FakeBackend { Capabilities = Array.Empty<string>() };
        Assert.Contains("'window' is not supported",
            (string?)Run(new DriveDispatcher(without), without, """{"id":4,"op":"window","state":"minimize"}""")["error"]);
    }

    [Fact]
    public void MenuChoosesACommandThroughTheBackendAndNeedsItsCapability()
    {
        var backend = new FakeBackend { Capabilities = new[] { "menu" } };
        var dispatcher = new DriveDispatcher(backend);
        var chosen = Run(dispatcher, backend, """{"id":1,"op":"menu","command":40035}""");
        Assert.Equal(0x9c63, (int)chosen["result"]!["command"]!);
        Assert.Equal(0x9c63, backend.Chosen);
        Assert.Contains("grayed", (string?)Run(dispatcher, backend, """{"id":2,"op":"menu","command":40006}""")["error"]);
        Assert.Contains("1..0xffff", (string?)Run(dispatcher, backend, """{"id":3,"op":"menu","command":0}""")["error"]);

        var without = new FakeBackend { Capabilities = Array.Empty<string>() };
        Assert.Contains("'menu' is not supported",
            (string?)Run(new DriveDispatcher(without), without, """{"id":4,"op":"menu","command":40035}""")["error"]);
    }

    [Fact]
    public void SettleWaitsForAQuietInputWaitAndReportsTimeouts()
    {
        var backend = new FakeBackend();
        bool busy = true;
        backend.State = () => At("SC0000@0x8a9", DriveWaiting.Input, busy);
        var dispatcher = new DriveDispatcher(backend);
        var settled = Run(dispatcher, backend, """{"id":1,"op":"settle","quiet_ms":300}""",
            onTick: now => busy = now < 200);
        Assert.True((bool)settled["result"]!["settled"]!);
        Assert.Equal(500, (long)settled["result"]!["elapsed_ms"]!);

        backend.State = () => At("OP@0x10", DriveWaiting.Movie);
        var timedOut = Run(dispatcher, backend, """{"id":2,"op":"settle","timeout_ms":400}""");
        Assert.True((bool)timedOut["ok"]!);
        Assert.False((bool)timedOut["result"]!["settled"]!);
    }

    [Fact]
    public void SpeedShotWheelAndCloseReachTheBackend()
    {
        var backend = new FakeBackend();
        var dispatcher = new DriveDispatcher(backend);
        Run(dispatcher, backend, """{"id":1,"op":"speed","factor":4}""");
        Assert.Equal(4, backend.Speed);
        Assert.False((bool)Run(dispatcher, backend, """{"id":2,"op":"speed","factor":20}""")["ok"]!);
        // The port's working directory is not the client's, so a relative path would land somewhere unexpected.
        var relative = Run(dispatcher, backend, """{"id":3,"op":"shot","path":"a.png"}""");
        Assert.False((bool)relative["ok"]!);
        Assert.Contains("absolute", (string?)relative["error"]);
        Assert.DoesNotContain(backend.Events, e => e.Contains("shot"));
        string absolute = Path.Combine(Path.GetTempPath(), "a.png");
        var shot = Run(dispatcher, backend,
            $$"""{"id":3,"op":"shot","path":{{JsonSerializer.Serialize(absolute)}}}""");
        Assert.Equal(800, (int)shot["result"]!["width"]!);
        Assert.Contains($"0:shot {absolute}", backend.Events);
        Run(dispatcher, backend, """{"id":4,"op":"wheel","notches":-2,"x":5,"y":6}""");
        Assert.Contains("0:wheel -2 5,6", backend.Events);
        backend.PointerPosition = (355, 299);           // e.g. where the game glided the cursor
        Run(dispatcher, backend, """{"id":4,"op":"wheel","notches":1}""");
        Assert.Contains("0:wheel 1 355,299", backend.Events);

        var close = Run(dispatcher, backend, """{"id":5,"op":"close"}""");
        Assert.True((bool)close["ok"]!);
        Assert.Equal(0, backend.Closes);
        dispatcher.Poll(50);
        Assert.Equal(0, backend.Closes);
        dispatcher.Poll(200);
        dispatcher.Poll(300);
        Assert.Equal(1, backend.Closes);
        Assert.True(dispatcher.CloseRequested);
    }

    // ------------------------------------------------------------------------------------------- server

    [Fact]
    public void ServerRequiresTheTokenThenAnswersThroughThePump()
    {
        var backend = new FakeBackend { State = () => At("SC0000@0x8a9", DriveWaiting.Input) };
        using var server = new DriveServer(backend, "secret");
        server.Start();
        using var stop = new CancellationTokenSource();
        var pump = new Thread(() =>
        {
            while (!stop.IsCancellationRequested) { server.Pump(Environment.TickCount64); Thread.Sleep(2); }
        }) { IsBackground = true };
        pump.Start();
        try
        {
            using (var rejected = Connect(server.Port))
            {
                JsonObject denied = Exchange(rejected, """{"id":1,"op":"hello","token":"wrong"}""");
                Assert.False((bool)denied["ok"]!);
                Assert.Null(rejected.Reader.ReadLine());
            }

            using var client = Connect(server.Port);
            JsonObject hello = Exchange(client, """{"id":1,"op":"hello","token":"secret"}""");
            Assert.Equal("port", (string?)hello["result"]!["target"]);
            Assert.Equal(DriveProtocol.Version, (int)hello["result"]!["version"]!);
            Assert.Contains("waiting", hello["result"]!["capabilities"]!.AsArray().Select(c => (string?)c));
            bool optimizerOff = typeof(Age.Engine.Vm.VirtualMachine).Assembly
                .GetCustomAttributes(typeof(System.Diagnostics.DebuggableAttribute), false)
                .Cast<System.Diagnostics.DebuggableAttribute>().Any(d => d.IsJITOptimizerDisabled);
            Assert.Equal(!optimizerOff, (bool)hello["result"]!["jit_optimized"]!);
            Assert.True(server.ClientConnected);

            JsonObject state = Exchange(client, """{"id":2,"op":"state"}""");
            Assert.Equal(2, (long)state["id"]!);
            Assert.Equal("SC0000@0x8a9", (string?)state["result"]!["location"]);

            JsonObject bad = Exchange(client, "{oops");
            Assert.False((bool)bad["ok"]!);
            JsonObject click = Exchange(client, """{"id":3,"op":"click","x":1,"y":2,"hold_ms":10}""");
            Assert.True((bool)click["ok"]!);
            Assert.Contains(backend.Events, e => e.EndsWith("left up 1,2"));
        }
        finally
        {
            stop.Cancel();
            pump.Join();
        }
    }

    [Fact]
    public void SessionFileCarriesPortTokenAndIdentity()
    {
        var backend = new FakeBackend();
        using var server = new DriveServer(backend, "tok");
        string path = Path.Combine(Path.GetTempPath(), $"drive-session-{Guid.NewGuid():N}.json");
        try
        {
            server.WriteSessionFile(path, 1234, "tok", "godot.log");
            JsonObject session = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            Assert.Equal(server.Port, (int)session["port"]!);
            Assert.Equal("tok", (string?)session["token"]);
            Assert.Equal(1234, (int)session["pid"]!);
            Assert.Equal("port", (string?)session["target"]);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            File.Delete(path);
        }
        Assert.Equal(32, DriveServer.NewToken().Length);
    }

    private sealed class Client(TcpClient tcp) : IDisposable
    {
        private readonly NetworkStream _stream = tcp.GetStream();
        public StreamReader Reader { get; } = new(tcp.GetStream(), new UTF8Encoding(false));
        public StreamWriter Writer { get; } =
            new(tcp.GetStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        public void Dispose() { Reader.Dispose(); Writer.Dispose(); _stream.Dispose(); tcp.Dispose(); }
    }

    private static Client Connect(int port)
    {
        var tcp = new TcpClient("127.0.0.1", port) { ReceiveTimeout = 5000 };
        return new Client(tcp);
    }

    private static JsonObject Exchange(Client client, string line)
    {
        client.Writer.WriteLine(line);
        string? reply = client.Reader.ReadLine();
        Assert.NotNull(reply);
        return JsonNode.Parse(reply!)!.AsObject();
    }

    // ------------------------------------------------------------------------------------------- VM frames

    private sealed class CapturingHost : RecordingHost
    {
        public VirtualMachine Vm = null!;
        public readonly List<IReadOnlyList<VmFrameLocation>> Captures = new();

        public override void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback)
        {
            Captures.Add(Vm.CaptureFrameLocations());
            base.WaitForInput(layoutSlot, serviceInputCallback);
        }
    }

    private sealed class MapProvider(Dictionary<long, Script> scripts) : IScriptProvider
    {
        public Script? GetById(long id) => scripts.TryGetValue(id, out Script? script) ? script : null;
    }

    private static readonly (int, Operand[]) WaitForInput = (0x72, new[] { new Operand(0, 0) });
    private static readonly (int, Operand[]) Exit = (0x2, Array.Empty<Operand>());

    [Fact]
    public void CapturedFramesAtAWaitResumeAfterTheWaitAndAfterTheCall()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        Script callee = ScriptAssembler.Assemble(table, "CALLEE.BIN",
            new List<(int, Operand[])> { WaitForInput, Exit }, Array.Empty<string>());
        Script caller = ScriptAssembler.Assemble(table, "CALLER.BIN",
            new List<(int, Operand[])> { WaitForInput, (0x3, new[] { new Operand(0, 5) }), Exit },
            Array.Empty<string>());
        var host = new CapturingHost();
        var vm = new VirtualMachine(caller, table, host, provider: new MapProvider(new() { [5] = callee }));
        host.Vm = vm;
        vm.Run();

        Assert.Equal(2, host.Captures.Count);
        VmFrameLocation top = Assert.Single(host.Captures[0]);
        Assert.Equal(caller.Instructions[0].Offset, top.CurrentOffset);
        Assert.Equal(caller.Instructions[1].Offset, top.ResumeOffset);

        IReadOnlyList<VmFrameLocation> nested = host.Captures[1];
        Assert.Equal(new[] { "CALLER", "CALLEE" }, nested.Select(f => DriveFrame.NormalizeScript(f.Script)));
        Assert.Equal(caller.Instructions[1].Offset, nested[0].CurrentOffset);
        Assert.Equal(caller.Instructions[2].Offset, nested[0].ResumeOffset);
        Assert.Equal(callee.Instructions[1].Offset, nested[1].ResumeOffset);
        // Driving reports the caller at its call-script instruction and the waiting callee past its wait.
        Assert.Equal(
            new[] { $"CALLER@0x{caller.Instructions[1].Offset:x}", $"CALLEE@0x{callee.Instructions[1].Offset:x}" },
            DriveFrame.FromVm(nested).Select(f => f.Location));
        Assert.Empty(vm.CaptureFrameLocations());
    }
}
