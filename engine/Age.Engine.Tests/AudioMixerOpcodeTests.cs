using Age.Engine.Model;
using Age.Engine.Persistence;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using System.Text;

public class AudioMixerOpcodeTests
{
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand G(int address) => new(3, address);
    private static Operand I(long value) => new(0, value);
    private static (int, Operand[]) Exit() => (0x2, Array.Empty<Operand>());

    [Fact]
    public void NativeDefaultsUseUnconfiguredVolumesAndEnabledRoutes()
    {
        var settings = new AudioMixerSettings();

        for (int category = 0; category < AudioMixerSettings.CategoryCount; category++)
        {
            Assert.True(settings.TryGetVolume(category, out int volume));
            Assert.Equal(AudioMixerSettings.UnconfiguredVolume, volume);
        }
        for (int category = (int)AudioMixerCategory.Music;
             category < AudioMixerSettings.CategoryCount;
             category++)
        {
            Assert.True(settings.TryGetRouteEnabled(category, out bool enabled));
            Assert.True(enabled);
        }
    }

    [Fact]
    public void MixerOpcodesRoundTripCategoriesAndApplyLiveChanges()
    {
        var settings = new AudioMixerSettings();
        var host = new RecordingHost();
        Script scene = ScriptAssembler.Assemble(Table, "CONFIG_MIXER",
        [
            (0xc6, [I(0), I(8000)]),
            (0xc6, [I(3), I(4500)]),
            (0x1ba, [I(1), I(0)]),
            (0xc5, [I(0), G(10)]),
            (0xc5, [I(3), G(11)]),
            (0xc5, [I(2), G(12)]),
            (0xc7, [I(1), G(13)]),
            (0xc7, [I(2), G(14)]),
            Exit(),
        ], []);
        var vm = new VirtualMachine(scene, Table, host, audioMixerSettings: settings);

        vm.Run();

        Assert.Equal(8000, vm.Globals[10]);
        Assert.Equal(4500, vm.Globals[11]);
        Assert.Equal(AudioMixerSettings.UnconfiguredVolume, vm.Globals[12]);
        Assert.Equal(0, vm.Globals[13]);
        Assert.Equal(1, vm.Globals[14]);
        Assert.Equal([(0, 8000), (3, 4500)], host.AudioVolumeChanges);
        Assert.Equal([(1, false)], host.AudioRouteChanges);
    }

    [Fact]
    public void RouteSetterIsIdempotentLikeNativeWorkers()
    {
        var host = new RecordingHost();
        Script scene = ScriptAssembler.Assemble(Table, "CONFIG_ROUTE",
        [
            (0x1ba, [I(2), I(0)]),
            (0x1ba, [I(2), I(0)]),
            (0x1ba, [I(2), I(1)]),
            (0x1ba, [I(2), I(1)]),
            Exit(),
        ], []);

        new VirtualMachine(scene, Table, host).Run();

        Assert.Equal([(2, false), (2, true)], host.AudioRouteChanges);
    }

    [Fact]
    public void GameSessionCarriesMixerStateAcrossFreshSceneVms()
    {
        var session = new GameSession();
        Script setter = ScriptAssembler.Assemble(Table, "CONFIG_SET",
        [
            (0xc6, [I(2), I(6250)]),
            (0x1ba, [I(3), I(0)]),
            Exit(),
        ], []);
        Script getter = ScriptAssembler.Assemble(Table, "CONFIG_GET",
        [
            (0xc5, [I(2), G(20)]),
            (0xc7, [I(3), G(21)]),
            Exit(),
        ], []);

        session.RunScene(setter, Table, new RecordingHost());
        session.RunScene(getter, Table, new RecordingHost());

        Assert.Equal(6250, session.Globals[20]);
        Assert.Equal(0, session.Globals[21]);
    }

    [Fact]
    public void NativeSys4RegIniRoundTripsAudioAndPreservesOtherOptions()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"age-audio-settings-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, Sys4RegIniStore.FileName);
        try
        {
            Directory.CreateDirectory(directory);
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Encoding cp932 = Encoding.GetEncoding(932);
            const string original =
                "[display]\r\nScreenMode=1\r\n\r\n" +
                "[sound]\r\nSound=1\r\nMusic=2\r\nSE=7\r\nVoice=1\r\nMovie=1\r\n" +
                "Volume0=5500\r\nVolume1=2500\r\nVolume2=3500\r\nVolume3=3500\r\nVolume4=-1\r\n" +
                "UseDirectSound=1\r\n\r\n" +
                "[message]\r\nFont=ＭＳ 明朝\r\nMessageSpeed=50\r\n";
            File.WriteAllText(path, original, cp932);

            var store = new Sys4RegIniStore(path, defaultMusicRouteValue: 2);
            AudioMixerSettings settings = store.Load();
            settings.Changed += store.Save;
            Assert.True(settings.TrySetVolume((int)AudioMixerCategory.Master, 9000));
            Assert.True(settings.TrySetVolume((int)AudioMixerCategory.Movie, 3750));
            Assert.True(settings.TrySetRouteEnabled(
                (int)AudioMixerCategory.Voice, false, out bool changed));
            Assert.True(changed);
            Assert.True(settings.TrySetRouteEnabled(
                (int)AudioMixerCategory.Music, false, out changed));
            Assert.True(changed);
            Assert.Contains("Music=-1\r\n", File.ReadAllText(path, cp932));
            Assert.True(settings.TrySetRouteEnabled(
                (int)AudioMixerCategory.Music, true, out changed));
            Assert.True(changed);

            AudioMixerSettings loaded = store.Load();
            string rewritten = File.ReadAllText(path, cp932);

            Assert.True(loaded.TryGetVolume((int)AudioMixerCategory.Master, out int master));
            Assert.True(loaded.TryGetVolume((int)AudioMixerCategory.Movie, out int movie));
            Assert.True(loaded.TryGetRouteEnabled((int)AudioMixerCategory.Voice, out bool voice));
            Assert.Equal(9000, master);
            Assert.Equal(3750, movie);
            Assert.False(voice);
            Assert.Contains("[display]\r\nScreenMode=1\r\n", rewritten);
            Assert.Contains("Sound=1\r\n", rewritten);
            Assert.Contains("Music=2\r\n", rewritten);
            Assert.Contains("SE=7\r\n", rewritten);
            Assert.Contains("UseDirectSound=1\r\n", rewritten);
            Assert.Contains("[message]\r\nFont=ＭＳ 明朝\r\nMessageSpeed=50\r\n", rewritten);
            Assert.DoesNotContain("engine-settings", rewritten);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void NativePersistencePathsComeFromIndependentSys4IniProfileValues()
    {
        Sys4AssetCatalog catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        string localAppData = Path.Combine(Path.GetTempPath(), "native-appdata-root");

        Sys4PersistencePaths paths = Sys4PersistencePaths.ResolveNative(
            catalog.StartupSettings, "C:\\unused-game-root", localAppData);

        Assert.Equal(
            Path.Combine(localAppData, "Eushully", "姫狩りダンジョンマイスター", "SAVE"),
            paths.SaveDirectory);
        Assert.Equal(
            Path.Combine(localAppData, "Eushully", "姫狩りダンジョンマイスター", "SYS4REG.INI"),
            paths.Sys4RegIniPath);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void ProfileOverrideRedirectsSaveAndSettingsAsOneNativeLayout()
    {
        Sys4AssetCatalog catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        string profileRoot = Path.Combine(Path.GetTempPath(), "redirected-profile-root");

        Sys4PersistencePaths paths = Sys4PersistencePaths.ResolveProfileOverride(
            catalog.StartupSettings, profileRoot);
        Sys4RegIniStore store = Sys4RegIniStore.ForPath(
            catalog.StartupSettings, paths.Sys4RegIniPath);

        Assert.Equal(Path.Combine(profileRoot, "SAVE"), paths.SaveDirectory);
        Assert.Equal(Path.Combine(profileRoot, "SYS4REG.INI"), paths.Sys4RegIniPath);
        Assert.Equal(paths.Sys4RegIniPath, store.FilePath);
    }

    [Fact]
    public void InvalidMixerCategoriesWarnAndLeaveOutputsUntouched()
    {
        var host = new RecordingHost();
        Script scene = ScriptAssembler.Assemble(Table, "CONFIG_INVALID",
        [
            (0xc5, [I(5), G(30)]),
            (0xc6, [I(-1), I(5000)]),
            (0xc7, [I(0), G(31)]),
            (0x1ba, [I(5), I(1)]),
            Exit(),
        ], []);
        var vm = new VirtualMachine(scene, Table, host);
        vm.Globals[30] = 123;
        vm.Globals[31] = 456;

        vm.Run();

        Assert.Equal(123, vm.Globals[30]);
        Assert.Equal(456, vm.Globals[31]);
        Assert.Equal(4, host.Warnings.Count);
        Assert.Empty(host.AudioVolumeChanges);
        Assert.Empty(host.AudioRouteChanges);
    }
}
