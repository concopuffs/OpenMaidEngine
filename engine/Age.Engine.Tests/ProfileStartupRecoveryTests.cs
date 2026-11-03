using System;
using System.IO;
using System.Linq;
using System.Text;
using Age.Engine.Persistence;
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
            Assert.Contains("[display]\r\nScreenMode=1\r\n", text);
            Assert.Contains("Font=ＭＳ 明朝\r\nMessageSpeed=30\r\n", text);
            Assert.Contains("MesWinAlpha=4\r\n", text);
            var reloaded = new Sys4RegIniStore(path, defaultMusicRouteValue: 2);
            Assert.Equal(4, reloaded.LoadMessageInt(Sys4RegIniStore.MesWinAlphaKey, Sys4RegIniStore.DefaultMesWinAlpha));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
