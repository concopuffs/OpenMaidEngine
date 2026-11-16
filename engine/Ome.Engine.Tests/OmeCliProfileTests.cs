using Ome.Mods.Launch;

/// <summary>
/// Launcher spec §5.7 (gate 6): every <c>Ome.Cli</c> command that takes <c>--data-root</c> reads the root's
/// <c>profile.toml</c> through the shared resolver, so <c>omp check</c>, <c>export</c>, <c>explain</c>,
/// <c>lua-stubs</c> and <c>text</c> need no game option. Each part of the profile is made to matter, since this
/// machine may hold both games at their conventional places:
/// <list type="bullet">
/// <item>the game is Kamidori, not the commands' default;</item>
/// <item>the enabled mod runs a script only the synthetic install lists, so another install fails the link;</item>
/// <item>the root's own <c>mods/</c> holds a mod that fails the link, which only the <c>[mods]</c> table leaves out.</item>
/// </list>
/// </summary>
public sealed class OmeCliProfileTests : IDisposable
{
    private const string KamidoriRevision = "S4IC433 ", KamidoriTitle = "神採りアルケミーマイスター";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-cli-profile-" + Guid.NewGuid().ToString("N"));

    private string ProfileRoot => Path.Combine(_root, "profile");

    public OmeCliProfileTests()
    {
        string install = Path.Combine(_root, "install");
        Directory.CreateDirectory(install);
        File.WriteAllBytes(Path.Combine(install, "SYS4INI.BIN"), Sys4StartupSettingsTests.BuildCatalog(
            [.. SharedLinkBaseTests.ScriptsTheModulesRun("kamidori"), "GATESIX.BIN"], KamidoriRevision, KamidoriTitle));
        Package(Path.Combine(_root, "library"), "gate.one", "GATESIX");
        Package(Path.Combine(ProfileRoot, ModLaunch.ModsFolder), "gate.stray", "NOSUCHSCRIPT");
        File.WriteAllText(Path.Combine(ProfileRoot, ProfileFile.FileName), """
            ome_profile = 1
            name = "Gate 6"
            game = "kamidori"
            mode = "extended"

            [install]
            root = "../install"

            [mods]
            folders = ["../library"]
            order = ["gate.one"]
            """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>A native Kamidori mod whose one new script runs <paramref name="runs"/>.</summary>
    private static void Package(string parent, string id, string runs)
    {
        string folder = Path.Combine(parent, id);
        string script = "S" + new string(id.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        Directory.CreateDirectory(Path.Combine(folder, "scripts"));
        File.WriteAllText(Path.Combine(folder, "manifest.toml"),
            $"omp = 1\nid = \"{id}\"\nname = \"{id}\"\nversion = \"1.0.0\"\n[scripts]\nnames = [\"{script}\"]\n"
            + "[game]\nprofile = \"kamidori\"\ntarget = \"native\"\n");
        File.WriteAllText(Path.Combine(folder, "scripts", script + ".oms"),
            $"oms 1\nscript \"{script}\"\nuse kamidori.scripts\n\n    run {runs}\n    exit\n");
    }

    private (int Exit, string Output, string Error) Cli(params string[] arguments)
        => OmeCliProcess.Run(_root, [.. arguments, RunResolver.DataRootOption, ProfileRoot]);

    private static void Linked((int Exit, string Output, string Error) run)
    {
        Assert.True(run.Exit == 0, $"exit {run.Exit}\n{run.Output}\n{run.Error}");
        Assert.Contains("[profile-file]", run.Error);
        Assert.DoesNotContain("gate.stray", run.Output);
    }

    [Fact]
    public void CheckLinksTheProfilesStack()
    {
        var run = Cli("omp", "check");
        Linked(run);
        Assert.Contains("load order: gate.one", run.Output);
        Assert.Contains("omp check: links: 1 mods", run.Output);
    }

    [Fact]
    public void ExportWritesTheProfilesStack()
    {
        string output = Path.Combine(_root, "export");
        var run = Cli("omp", "export", "--output-dir", output);
        Linked(run);
        Assert.Contains(Directory.EnumerateFiles(output), file => file.EndsWith(".AAI", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExplainFindsTheProfilesMod()
    {
        var run = Cli("omp", "explain", "gate.one:SGATEONE");
        Linked(run);
        Assert.Contains("gate.one's append catalog", run.Output);
    }

    [Fact]
    public void LuaStubsLinkTheProfilesStack()
    {
        var run = Cli("omp", "lua-stubs", "--output", Path.Combine(_root, "stubs"));
        Linked(run);
        Assert.Contains("omp lua-stubs:", run.Output);
    }

    [Fact]
    public void TextCheckLinksTheProfilesStack()
    {
        var run = Cli("omp", "text", "check", "--no-scripts");
        Linked(run);
        Assert.DoesNotContain("does not link", run.Output);
    }
}
