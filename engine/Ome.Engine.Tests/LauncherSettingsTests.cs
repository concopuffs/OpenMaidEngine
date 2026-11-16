using Ome.Engine.Frontend.Shell;
using Ome.Launcher.Core;
using Xunit;

namespace Ome.Engine.Tests;

/// <summary>Launcher spec §8.8 (launcher plan §5.2): <c>launcher.toml</c>, read with a type for every key and written
/// so that what was read comes back.</summary>
public sealed class LauncherSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-launcher-settings-" + Guid.NewGuid().ToString("N"));
    private readonly OmeHome _home;

    public LauncherSettingsTests()
    {
        Directory.CreateDirectory(_root);
        _home = new OmeHome(Path.Combine(_root, "home"), OmeHomeKind.Explicit);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void AMissingFileIsTheDefaults()
    {
        LauncherSettingsFile file = LauncherSettingsStore.Load(_home);
        Assert.Same(LauncherSettings.Defaults, file.Settings);
        Assert.Empty(file.Warnings);
        Assert.True(file.Writable);
    }

    [Fact]
    public void WhatIsWrittenReadsBack()
    {
        string outside = Path.Combine(_root, "elsewhere", "kami");
        string inside = Path.Combine(_home.Root, "more", "hime");
        var settings = new LauncherSettings
        {
            Language = "en",
            Theme = LauncherTheme.Dark,
            Window = new WindowPlacement(-8, 40, 1100, 720, true),
            WhileRunning = WhileRunning.Minimize,
            Art = ArtConsent.Never,
            LastProfile = "kami",
            Roots = [new ExternalRoot(outside, "kami"), new ExternalRoot(inside, "hime")],
            Reviewed = [outside],
            NewProfile = new PortOptions("en-US", 1280, 960, true, false),
        };
        LauncherSettingsStore.Save(_home, LauncherSettingsStore.Load(_home), settings);
        string text = File.ReadAllText(_home.SettingsPath);
        // Inside OME home a path is relative (spec §4.3); outside it is absolute.
        Assert.Contains("path = \"more/hime\"", text);
        Assert.Contains(TextQuoted(outside), text);
        Assert.Empty(Directory.GetFiles(_home.Root, "*.tmp-*"));

        LauncherSettingsFile read = LauncherSettingsStore.Load(_home);
        Assert.Empty(read.Warnings);
        LauncherSettings back = read.Settings;
        // A language tag reads back normalized, by the rule the engine reads OME.INI with (LanguageTag).
        Assert.Equal(settings with
        {
            Roots = back.Roots,
            Reviewed = back.Reviewed,
            NewProfile = settings.NewProfile with { Language = "en-us" },
        }, back);
        Assert.Equal(settings.Roots, back.Roots);
        Assert.Equal(settings.Reviewed, back.Reviewed);
    }

    [Fact]
    public void AWrongKeyIsAWarningAndKeepsItsDefault()
    {
        Directory.CreateDirectory(_home.Root);
        File.WriteAllText(_home.SettingsPath, """
            ome_launcher = 1
            theme = "purple"
            art = 3
            last_profile = "Not An Id"
            future_key = true

            [new_profile]
            window_width = 800
            fps_counter = true

            [[roots]]
            path = "a"
            id = "x"

            [[roots]]
            path = "b"
            id = "x"

            [[roots]]
            path = "c"
            """);
        LauncherSettingsFile file = LauncherSettingsStore.Load(_home);
        Assert.True(file.Writable);
        LauncherSettings settings = file.Settings;
        Assert.Equal(LauncherTheme.System, settings.Theme);
        Assert.Equal(ArtConsent.Ask, settings.Art);
        Assert.Null(settings.LastProfile);
        // Half a window size is the canvas size, as in OME.INI; the other keys of the table still count.
        Assert.Equal(PortOptions.Defaults with { FpsCounter = true }, settings.NewProfile);
        Assert.Equal([new ExternalRoot(Path.Combine(_home.Root, "a"), "x")], settings.Roots);
        string warnings = string.Join("\n", file.Warnings);
        foreach (string expected in new[] { "theme: 'purple'", "art: expected a string", "last_profile: 'Not An Id'",
                                            "future_key: unknown key", "a window size needs both",
                                            "roots[1].id: 'x' is listed twice", "roots[2].id: required" })
            Assert.Contains(expected, warnings);

        // An unknown key is dropped on the next write.
        LauncherSettingsStore.Save(_home, file, settings);
        Assert.DoesNotContain("future_key", File.ReadAllText(_home.SettingsPath));
        Assert.Empty(LauncherSettingsStore.Load(_home).Warnings);
    }

    [Fact]
    public void AnotherVersionIsNotUsedOrWrittenOver()
    {
        Directory.CreateDirectory(_home.Root);
        const string newer = "ome_launcher = 2\ntheme = \"dark\"\n";
        File.WriteAllText(_home.SettingsPath, newer);
        LauncherSettingsFile file = LauncherSettingsStore.Load(_home);
        Assert.Same(LauncherSettings.Defaults, file.Settings);
        Assert.False(file.Writable);
        Assert.Contains("format version 2; this launcher reads version 1", Assert.Single(file.Warnings));
        Assert.Throws<InvalidOperationException>(() => LauncherSettingsStore.Save(_home, file, LauncherSettings.Defaults));
        Assert.Equal(newer, File.ReadAllText(_home.SettingsPath));
    }

    [Fact]
    public void AnUnreadableFileIsKeptBesideTheNewOne()
    {
        Directory.CreateDirectory(_home.Root);
        const string broken = "ome_launcher = 1\ntheme = \"dark\n";
        File.WriteAllText(_home.SettingsPath, broken);
        LauncherSettingsFile file = LauncherSettingsStore.Load(_home);
        Assert.True(file.Unreadable);
        Assert.Contains("not valid TOML", Assert.Single(file.Warnings));
        LauncherSettingsStore.Save(_home, file, LauncherSettings.Defaults with { Theme = LauncherTheme.Light });
        Assert.Equal(broken, File.ReadAllText(_home.SettingsPath + LauncherSettingsStore.UnreadableSuffix));
        Assert.Equal(LauncherTheme.Light, LauncherSettingsStore.Load(_home).Settings.Theme);
    }

    [Fact]
    public void AFileWithNoVersionIsReadAsTheFirst()
    {
        Directory.CreateDirectory(_home.Root);
        File.WriteAllText(_home.SettingsPath, "theme = \"light\"\n");
        LauncherSettingsFile file = LauncherSettingsStore.Load(_home);
        Assert.Equal(LauncherTheme.Light, file.Settings.Theme);
        Assert.Contains("ome_launcher: missing", Assert.Single(file.Warnings));
    }

    [Fact]
    public void ProfileIdsAreLowercaseLettersDigitsAndDashes()
    {
        foreach (string valid in new[] { "kamidori", "kami-2", "a", "x1-y2" })
            Assert.True(ProfileId.IsValid(valid), valid);
        foreach (string? invalid in new[] { null, "", "Kami", "kami_2", "-kami", "kami-", "kämi", "a b", new string('a', 65) })
            Assert.False(ProfileId.IsValid(invalid), invalid);
    }

    private static string TextQuoted(string path) => Ome.Mods.Localization.TextCatalogFileWriter.Quote(path);
}
