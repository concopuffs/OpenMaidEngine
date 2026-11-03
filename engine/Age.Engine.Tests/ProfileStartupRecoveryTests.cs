using System;
using System.IO;
using System.Linq;
using System.Text;
using Age.Engine.Persistence;
using Age.Engine.Sys4;
using Xunit;

// Startup must survive unreadable profile files (AGE offers to continue past "データが壊れています"), and the
// [message] settings start from AGE's registered defaults (engine_settings_register_defaults@0x46be30).
public class ProfileStartupRecoveryTests
{
    private static readonly NativeSaveIdentity Identity = new(NativeSaveMagic.S4SD, 123, "himegari-test", 3, 2);

    private static string NewDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"age-profile-recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Fact]
    public void UnreadableSaveAndReadTextFilesStartEmptyAndAreSetAside()
    {
        string root = NewDirectory();
        try
        {
            byte[] garbage = Encoding.ASCII.GetBytes("not an AGE profile file at all");
            File.WriteAllBytes(Path.Combine(root, DirectoryNativeDatStore.SharedFileName), garbage);
            File.WriteAllBytes(Path.Combine(root, DirectoryNativeDatStore.SharedBackupFileName), garbage);
            File.WriteAllBytes(Path.Combine(root, DirectoryNativeDatStore.ReadTextFileName), garbage);
            var store = new DirectoryNativeDatStore(root, Identity);
            var profile = new SharedProfile();

            Assert.ThrowsAny<Exception>(() => profile.Load(store));
            var failures = profile.LoadRecovering(store);

            Assert.Equal([NativeProfileFile.Shared, NativeProfileFile.ReadText], failures.Select(f => f.File));
            var aside = store.SetAsideUnreadable(NativeProfileFile.Shared)
                .Concat(store.SetAsideUnreadable(NativeProfileFile.ReadText)).ToArray();
            Assert.Equal(3, aside.Length);
            Assert.All(aside, name => Assert.Equal(garbage, File.ReadAllBytes(Path.Combine(root, name))));
            Assert.False(File.Exists(Path.Combine(root, DirectoryNativeDatStore.SharedFileName)));
            Assert.Empty(new SharedProfile().LoadRecovering(store));   // the next start sees a fresh profile
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MessageSettingsDefaultToNativeValuesAndRoundTripBesideOtherKeys()
    {
        string root = NewDirectory();
        string path = Path.Combine(root, Sys4RegIniStore.FileName);
        try
        {
            var empty = new Sys4RegIniStore(path, defaultMusicRouteValue: 2);
            Assert.Equal(5, empty.LoadMessageInt(Sys4RegIniStore.MessageSpeedKey, Sys4RegIniStore.DefaultMessageSpeed));
            Assert.Equal(8, empty.LoadMessageInt(Sys4RegIniStore.MesWinAlphaKey, Sys4RegIniStore.DefaultMesWinAlpha));

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Encoding cp932 = Encoding.GetEncoding(932);
            File.WriteAllText(path, "[display]\r\nScreenMode=1\r\n\r\n[message]\r\nFont=ＭＳ 明朝\r\nMessageSpeed=50\r\n", cp932);
            var store = new Sys4RegIniStore(path, defaultMusicRouteValue: 2);
            Assert.Equal(50, store.LoadMessageInt(Sys4RegIniStore.MessageSpeedKey, Sys4RegIniStore.DefaultMessageSpeed));
            store.SaveMessageInt(Sys4RegIniStore.MesWinAlphaKey, 4);
            store.SaveMessageInt(Sys4RegIniStore.MessageSpeedKey, 30);

            string text = File.ReadAllText(path, cp932);
            Assert.Contains("UseWideSurface=0\r\nScreenMode=1\r\n", text);
            Assert.Contains("Font=ＭＳ 明朝\r\nRMouseEvent=0\r\nMessageSpeed=30\r\nMesWinAlpha=4\r\n", text);
            var reloaded = new Sys4RegIniStore(path, defaultMusicRouteValue: 2);
            Assert.Equal(4, reloaded.LoadMessageInt(Sys4RegIniStore.MesWinAlphaKey, Sys4RegIniStore.DefaultMesWinAlpha));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WritingAFreshSettingsFileProducesAgesCompleteKeySet()
    {
        string root = NewDirectory();
        string path = Path.Combine(root, Sys4RegIniStore.FileName);
        try
        {
            // Himegari's SYS4INI pairs that reach SYS4REG.INI keys (docs/sys4-format-notes.md).
            var startup = Sys4StartupSettings.FromPairs([
                new("FONT", "ＭＳ 明朝"), new("ENABLEANTIFONT", "1"), new("FULLSCREENBIT", "32"),
                new("NOSETMUSIC", "3")]);
            Sys4RegIniStore.ForPath(startup, path)
                .SaveMessageInt(Sys4RegIniStore.MessageSpeedKey, Sys4RegIniStore.DefaultMessageSpeed);

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            // Native AGE's file after a first run, byte for byte (ScreenMode and Volume0 at their defaults).
            string[] nativeLines =
            [
                "[display]", "UseDirectDrawEmuration=0", "UseWideSurface=0", "ScreenMode=0", "FullScreenBit=32",
                "UseIVideoWindow=1", "DeviceType=0", "VertexProcessing=0", "PresentInterval=0", "TextureBit=32",
                "PixelShader=-1", "ForceScreen=0", "VirtualFullScreen=0", "AspectMode=0", "FullScreenWidth=0",
                "FullScreenHeight=0", "LimitAero=0",
                "[sound]", "Sound=1", "Music=2", "SE=1", "Voice=1", "Movie=1", "UseDirectSound=1",
                "UseDirectSoundEmuration=1", "DirectSoundCooperativeLevel=1",
                "Volume0=-1", "Volume1=-1", "Volume2=-1", "Volume3=-1", "Volume4=-1",
                "MusicFadeOnVoicePlaying=1", "MusicFadeOnVoicePlayingVolume=50",
                "[message]", "Font=ＭＳ 明朝", "RMouseEvent=0", "MessageSpeed=5", "MesWinAlpha=8", "UseAntiFont=1",
                "AutoMessageTime0=500", "AutoMessageTime1=2000", "ReadTextSkip=0", "AdvanceMesOnWheel=0",
                "[system]", "SaveBMPPath=", "UseMMX=1", "EffectSkipOnClick=0", "OutErrorLog=0",
            ];
            string native = string.Join("\r\n", nativeLines) + "\r\n";
            Assert.Equal(native, File.ReadAllText(path, Encoding.GetEncoding(932)));

            // A file holding a few keys out of order (as the game driver seeds it) comes back in AGE's order
            // with its values; keys AGE does not bind are kept after the bound ones.
            File.WriteAllText(path,
                "[sound]\r\nVolume0=0\r\nExtra=7\r\n\r\n[display]\r\nScreenMode=1\r\n[mod]\r\nKey=v\r\n",
                Encoding.GetEncoding(932));
            Sys4RegIniStore.ForPath(startup, path)
                .SaveMessageInt(Sys4RegIniStore.MessageSpeedKey, Sys4RegIniStore.DefaultMessageSpeed);
            string expected = native.Replace("ScreenMode=0", "ScreenMode=1").Replace("Volume0=-1", "Volume0=0")
                .Replace("MusicFadeOnVoicePlayingVolume=50\r\n", "MusicFadeOnVoicePlayingVolume=50\r\nExtra=7\r\n")
                + "[mod]\r\nKey=v\r\n";
            Assert.Equal(expected, File.ReadAllText(path, Encoding.GetEncoding(932)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
