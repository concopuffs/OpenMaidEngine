using System.Collections.Generic;
using System.Linq;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class GameSessionTests
{
    private const int T_IMM = 0, T_GINT = 3;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);

    // A minimal one-instruction script: `mov (global-int dst) src`. Halts pc-out-of-range after.
    private static Script MovScript(int dst, int srcType, long srcVal) => new()
    {
        Header = new ScriptHeader(0, 0, 0, 0, 0, 0),
        Instructions = new[]
        {
            new Instruction(0, 0x55, new[] { new Operand(T_GINT, dst), new Operand(srcType, srcVal) }),
        },
        IndexByOffset = new Dictionary<int, int> { { 0, 0 } },
        Strings = new Dictionary<int, string>(),
    };

    private sealed class VoiceCountHost : IHost
    {
        public int Voices;
        public List<int> Emitted = new();
        public void ShowText(int o, string t) => Emitted.Add(o);
        public void WaitForInput() { }
        public void Sleep(long duration) { }
        public void FrameYield() { }
        public void CreateTexture(int s, int w, int h) { }
        public void SetTexture(long r, int s) { }
        public void DrawTexture(int s, int sx, int sy, int w, int h, int dx, int dy) { }
        public (int Width, int Height) GetTextureSize(int slot) => (0, 0);
        public void PlayBgm(long id) { }
        public void PlayVoice(long id) => Voices++;
    }

    [Fact]
    public void StatePersistsAcrossScenes()
    {
        var session = new GameSession();
        // Scene A writes G[0x5000] = 42.
        session.RunScene(MovScript(0x5000, T_IMM, 0x2a), Table, new CaptureHost());
        Assert.Equal(0x2a, session.Globals[0x5000]);
        // Scene B copies G[0x5000] -> G[0x5001]; it must SEE A's write.
        session.RunScene(MovScript(0x5001, T_GINT, 0x5000), Table, new CaptureHost());
        Assert.Equal(0x2a, session.Globals[0x5001]);
    }

    [Fact]
    public void SeedIsVisibleToTheScene()
    {
        var session = new GameSession();
        session.Seed(0x5000, 99);
        session.RunScene(MovScript(0x5001, T_GINT, 0x5000), Table, new CaptureHost());
        Assert.Equal(99, session.Globals[0x5001]);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void SC0000ViaSessionMatchesSingleRun()
    {
        var script = Sys4Loader.Load(Paths.Scripts()["SC0000.BIN"], Table);
        // single run
        var host1 = new CaptureHost();
        new VirtualMachine(script, Table, host1).Run();
        var single = host1.Emitted.Select(e => e.Offset).ToList();
        // via session
        var host2 = new CaptureHost();
        var r = new GameSession().RunScene(Sys4Loader.Load(Paths.Scripts()["SC0000.BIN"], Table), Table, host2);
        var viaSession = r.Emitted.Select(e => e.Offset).ToList();
        Assert.Equal(single, viaSession);
        Assert.Equal("exit", r.Halt);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void BootingSkinitPopulatesDataTableGlobals()
    {
        // Running the SKINIT data script through the session populates the real skill table into the
        // global bank (the game's boot behavior). Cross-check vs the static extraction (build/data/SKINIT.json):
        // skill id 1 = "飛行" at global-string 0x23a3, field G[0xa6e5b] = 30.
        var session = new GameSession();
        var r = session.RunScene(Sys4Loader.Load(Paths.Scripts()["SKINIT.BIN"], Table), Table, new CaptureHost());
        Assert.Equal("exit", r.Halt);
        Assert.Equal("飛行", session.GlobalStrings[0x23a3]);
        Assert.Equal(30, session.Globals[0xa6e5b]);
        Assert.True(session.Globals.Count > 1000, $"SKINIT should populate the skill table (got {session.Globals.Count})");
    }

    [Fact]
    public void SnapshotRoundTripsState()
    {
        var s = new GameSession();
        s.Seed(0x10, 42); s.Seed(0x20, -7); s.SeedString(0x30, "リリィ");
        var t = GameSession.FromJson(s.ToJson());
        Assert.Equal(42, t.Globals[0x10]);
        Assert.Equal(-7, t.Globals[0x20]);
        Assert.Equal("リリィ", t.GlobalStrings[0x30]);
        Assert.Equal(s.Globals.Count, t.Globals.Count);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void BootedStateSurvivesSnapshot()
    {
        var s = new GameSession();
        s.RunScene(Sys4Loader.Load(Paths.Scripts()["SKINIT.BIN"], Table), Table, new CaptureHost());
        var t = GameSession.FromJson(s.ToJson());   // snapshot the expensive booted state, rebuild
        Assert.Equal("飛行", t.GlobalStrings[0x23a3]);
        Assert.Equal(30, t.Globals[0xa6e5b]);
        Assert.Equal(s.Globals.Count, t.Globals.Count);
        Assert.Equal(s.GlobalStrings.Count, t.GlobalStrings.Count);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void SeedingFormFlagChangesBehavior()
    {
        // Lily's lines are gated on form flags G[0xa57/8/9]; unseeded => all skipped (0 voices on her lines).
        // Seeding form A (0xa57=1) makes her lines execute -> more play-voice calls. Proven finding (audio).
        var scriptPath = Paths.Scripts()["SC0000.BIN"];
        var unseeded = new VoiceCountHost();
        new GameSession().RunScene(Sys4Loader.Load(scriptPath, Table), Table, unseeded);

        var s = new GameSession();
        s.Seed(0xa57, 1);
        var seeded = new VoiceCountHost();
        s.RunScene(Sys4Loader.Load(scriptPath, Table), Table, seeded);

        Assert.True(seeded.Voices > unseeded.Voices,
            $"seeding form flag should fire more voices: unseeded={unseeded.Voices} seeded={seeded.Voices}");
    }
}
