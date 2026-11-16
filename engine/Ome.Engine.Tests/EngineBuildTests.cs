using System.Text;
using System.Text.RegularExpressions;
using Ome.Engine.Model;
using Ome.Engine.Persistence;
using Ome.Engine.Profiles;
using Ome.Engine.Sys4;
using Ome.Engine.Text;
using Ome.Engine.Vm;

/// <summary>Engine builds (docs/superpowers/specs/2026-11-12-engine-layering-design.md §7).</summary>
public sealed class EngineBuildTests
{
    [Fact]
    public void EveryProfileNamesADeclaredBuildThatRunsItsAbi()
    {
        foreach (GameProfileManifest profile in GameProfileRegistry.BuiltIn.Profiles)
        {
            EngineBuild build = EngineBuilds.ForProfile(profile);
            Assert.Equal(profile.EngineBuildId, build.Id);
            Assert.Equal(profile.EngineAbiId, build.AbiId);
            Assert.Contains(build.AbiId, OpcodeTableJson.Load(Paths.OpcodesJson, build.AbiId).ResolvedLayers);
        }
        Assert.Same(EngineBuilds.Age424J, EngineBuilds.ForProfile(GameProfileRegistry.BuiltIn.Find("himegari")!));
        Assert.Same(EngineBuilds.Age433G, EngineBuilds.ForProfile(GameProfileRegistry.BuiltIn.Find("kamidori")!));
    }

    [Fact]
    public void ABuildIsNamedByTheStampItWritesIntoSaves()
    {
        // spec §7.1: the profiles' former compatibilityId values are these stamps read as little-endian dwords.
        Assert.Equal(EngineBuilds.All.Count, EngineBuilds.All.Select(build => build.Id).Distinct().Count());
        foreach (EngineBuild build in EngineBuilds.All)
            Assert.Equal(build.Id.Replace(".", ""), build.Rules.Saves.VersionStamp);
        Assert.Equal(1244934708u, EngineBuilds.Age424J.Rules.Saves.CompatibilityId);
        Assert.Equal(1194537780u, EngineBuilds.Age433G.Rules.Saves.CompatibilityId);
        Assert.Equal(0x2d4, EngineBuilds.Age424J.Rules.Saves.NumberedGfxRecordSize);
        Assert.Equal(0x2e4, EngineBuilds.Age433G.Rules.Saves.NumberedGfxRecordSize);
    }

    [Fact]
    public void FourThirtyThreeGIsFourTwentyFourJPlusItsChanges()
    {
        EngineRules parent = EngineBuilds.Age424J.Rules, child = EngineBuilds.Age433G.Rules;
        Assert.Equal(GlyphCoverageAlpha.Sixteenths, parent.Text.GlyphCoverage);
        Assert.Equal(GlyphCoverageAlpha.Seventeenths, child.Text.GlyphCoverage);
        Assert.Same(RubyRule.FixedOffsets, parent.Text.Ruby);
        Assert.Same(RubyRule.ShiftModes, child.Text.Ruby);
        Assert.False(parent.Text.FuriganaArmsGlyphTimer);
        Assert.True(child.Text.FuriganaArmsGlyphTimer);
        Assert.False(parent.Gfx.UnboundObjectReadsAbsent);
        Assert.True(child.Gfx.UnboundObjectReadsAbsent);

        // The settings loader binds the parent's keys in order, with two more.
        IReadOnlyList<Sys4RegIniKey> keys = child.Settings.BoundKeys;
        Assert.Same(SettingsRules.LoaderKeys, parent.Settings);
        Assert.Equal(parent.Settings.BoundKeys,
            keys.Where(key => key.Key is not ("VirtualFullScreenType" or "AntiFontLevel")));
        Assert.Equal("LimitAero", keys[keys.ToList().FindIndex(key => key.Key == "VirtualFullScreenType") - 1].Key);
        Assert.Equal("UseAntiFont", keys[keys.ToList().FindIndex(key => key.Key == "AntiFontLevel") - 1].Key);
    }

    [Fact]
    public void AVmGivenNoBuildRunsTheBuildOfItsTablesAbi()
    {
        OpcodeTable sys4433 = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        Assert.Same(EngineBuilds.Age433G, EngineBuilds.ForVm(null, sys4433));
        Assert.Same(EngineBuilds.Age424J, EngineBuilds.ForVm(null, OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4422")));
        // The opcode catalog is no build's table: tests that run it get the base build.
        Assert.Same(EngineBuilds.Age424J, EngineBuilds.ForVm(null, OpcodeTableJson.Load(Paths.OpcodesJson)));
        Assert.Same(EngineBuilds.Age433G, EngineBuilds.ForVm(EngineBuilds.Age433G, sys4433));
        var error = Assert.Throws<ArgumentException>(() => EngineBuilds.ForVm(EngineBuilds.Age424J, sys4433));
        Assert.Contains("4.24J", error.Message);
    }

    [Fact]
    public void AProfileWhoseAbiDisagreesWithItsBuildIsRejected()
    {
        GameProfileManifest himegari = GameProfileRegistry.BuiltIn.Find("himegari")!;
        var wrongAbi = new GameProfileManifest(
            "wrong-abi", "wrong", himegari.CatalogIdentities, himegari.ScriptRevisions, himegari.SysFrontendId,
            "4.24J", "SYS4433", himegari.NaturalBootScript, null, himegari.Persistence);
        var unknown = new GameProfileManifest(
            "unknown-build", "unknown", himegari.CatalogIdentities, himegari.ScriptRevisions, himegari.SysFrontendId,
            "9.99Z", "SYS4422", himegari.NaturalBootScript, null, himegari.Persistence);

        Assert.Contains("runs 'SYS4422'",
            Assert.Throws<InvalidDataException>(() => EngineBuilds.ForProfile(wrongAbi)).Message);
        Assert.Contains("not declared", Assert.Throws<InvalidDataException>(() => EngineBuilds.ForProfile(unknown)).Message);
    }

    [Theory]
    [InlineData("42J")]
    [InlineData("424JX")]
    [InlineData("42éJ")]
    public void AVersionStampIsFourPrintableAsciiCharacters(string stamp)
        => Assert.Throws<ArgumentException>(() => new SaveRules(stamp, NativeGfxSaveLayout.BaseRecordSize));

    [Fact]
    public void AnUnsupportedNumberedGfxRecordSizeIsRejected()
        => Assert.Throws<ArgumentException>(() => new SaveRules("424J", 0x2d0));

    // Spec §7.6: the backstop. What reads a script ABI id or an engine build id; everything else takes rules.
    private static readonly string[] AllowedReaders =
    [
        // The builds themselves.
        "engine/Ome.Engine.Vm/EngineBuilds.cs",
        // Profile loading and the registry loader.
        "engine/Ome.Engine/Profiles/",
        "engine/Ome.Engine/Sys4/Sys4Loader.cs",
        "engine/Ome.Engine/Model/OpcodeTable.cs",
        // The script toolchain: the table's ABI chooses opcodes and writes a script header's magic, a fact about the
        // file. ScriptAssembler is the engine's own synthetic-script writer.
        "engine/Ome.Script/",
        "engine/Ome.Mods/Linking/ModLinkBase.cs",
        "engine/Ome.Mods/Linking/ModCatalogBuilder.cs",
        "engine/Ome.Engine/Sys4/ScriptAssembler.cs",
        // Composition: the Godot front end and the command-line tools put the engine together, and the launcher's
        // link check links as Ome.Cli does (launcher spec §5.9).
        "godot/Main.cs",
        "godot/Main.Mods.cs",
        "engine/Ome.Cli/",
        "engine/Ome.Launcher.Core/LinkComposition.cs",
        // Diagnostics that print the id.
        "engine/Ome.Engine/Sys4/Sys4OpcodeDecodeException.cs",
        "engine/Ome.Engine/Diagnostics/UnsupportedOpcodeDiagnostic.cs",
        "engine/Ome.Engine.Frontend/GodotTraceSink.cs",
        "engine/Ome.Engine/Localization/MessageSegmenter.cs",
        "engine/Ome.Mods/Localization/Import/ComposedStrings.cs",
    ];

    private static readonly Regex IdRead = new(
        @"\.AbiId\b|\bEngineAbiId\b|\bEngineBuildId\b|""SYS4\d{3}|""\d\.\d\d[A-Z]""|\b\w*[Bb]uild\.Id\b"
        + @"|\bEngineBuilds\.(?:Find|ForProfile|All|Age\w+)\b|\bIsSys44\d\d\b",
        RegexOptions.Compiled);

    [Fact]
    public void NothingBelowCompositionReadsAnAbiOrBuildId()
    {
        var offenders = new List<string>();
        int files = 0;
        foreach (string root in new[] { "engine", "godot" })
        foreach (string file in Directory.EnumerateFiles(Path.Combine(Paths.Repo, root), "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(Paths.Repo, file).Replace('\\', '/');
            if (relative.Contains("/bin/") || relative.Contains("/obj/") || relative.Contains("/.godot/")
                || relative.StartsWith("engine/Ome.Engine.Tests/", StringComparison.Ordinal))
                continue;
            files++;
            if (AllowedReaders.Any(allowed => allowed.EndsWith('/')
                    ? relative.StartsWith(allowed, StringComparison.Ordinal)
                    : relative == allowed))
                continue;
            string[] lines = WithoutComments(File.ReadAllText(file)).Split('\n');
            for (int index = 0; index < lines.Length; index++)
                if (IdRead.Match(lines[index]) is { Success: true } match)
                    offenders.Add($"{relative}:{index + 1}: {match.Value}");
        }
        Assert.True(files > 300, $"only {files} sources were found");
        Assert.Empty(offenders);
    }

    [Fact]
    public void EveryAllowedReaderExists()
    {
        // A moved file would otherwise leave its entry pointing at nothing and its reads flagged, or a directory
        // entry silently allowing nothing.
        string[] missing = AllowedReaders
            .Where(allowed => allowed.EndsWith('/')
                ? !Directory.Exists(Path.Combine(Paths.Repo, allowed))
                : !File.Exists(Path.Combine(Paths.Repo, allowed)))
            .ToArray();
        Assert.Empty(missing);
    }

    [Fact]
    public void TheScanSeesAReadInCodeButNotInAComment()
    {
        string source = "// profile.EngineAbiId\nvar a = \"//\"; var b = t.AbiId; /* build.Id */ var c = \"SYS4433\";\n"
                        + "var d = \"\"\"\n  .AbiId\n  \"\"\";";
        string[] lines = WithoutComments(source).Split('\n');
        Assert.DoesNotMatch(IdRead, lines[0]);
        Assert.Equal([".AbiId", "\"SYS4433"], IdRead.Matches(lines[1]).Select(match => match.Value));
        Assert.Matches(IdRead, lines[3]);   // inside a raw string literal, which is code's data, not a comment
    }

    /// <summary>C# source with its comments blanked (line breaks kept), strings and characters left as they are.</summary>
    private static string WithoutComments(string source)
    {
        var output = new StringBuilder(source.Length);
        int i = 0;
        while (i < source.Length)
        {
            char c = source[i];
            char next = i + 1 < source.Length ? source[i + 1] : '\0';
            if (c == '/' && next == '/')
            {
                while (i < source.Length && source[i] != '\n') i++;
            }
            else if (c == '/' && next == '*')
            {
                int end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? source.Length : end + 2;
                output.Append('\n', source[i..end].Count(character => character == '\n'));
                i = end;
            }
            else if (c == '"' && next == '"' && i + 2 < source.Length && source[i + 2] == '"')
            {
                int quotes = 0;
                while (i + quotes < source.Length && source[i + quotes] == '"') quotes++;
                string fence = new('"', quotes);
                int end = source.IndexOf(fence, i + quotes, StringComparison.Ordinal);
                end = end < 0 ? source.Length : end + quotes;
                output.Append(source, i, end - i);
                i = end;
            }
            else if (c == '"' || c == '\'')
            {
                bool verbatim = c == '"' && i > 0 && source[i - 1] == '@'
                                || c == '"' && i > 1 && source[i - 2] == '@' && source[i - 1] == '$';
                int j = i + 1;
                while (j < source.Length)
                {
                    if (!verbatim && source[j] == '\\') { j += 2; continue; }
                    if (source[j] == c)
                    {
                        if (verbatim && j + 1 < source.Length && source[j + 1] == '"') { j += 2; continue; }
                        break;
                    }
                    if (source[j] == '\n' && !verbatim) break;
                    j++;
                }
                j = Math.Min(j + 1, source.Length);
                output.Append(source, i, j - i);
                i = j;
            }
            else
            {
                output.Append(c);
                i++;
            }
        }
        return output.ToString();
    }
}
