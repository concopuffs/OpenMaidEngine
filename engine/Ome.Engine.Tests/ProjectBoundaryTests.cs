using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Ome.Engine.Hosting;
using Ome.Engine.Devices;
using Ome.Engine.Frontend.Shell;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Text;
using Ome.Engine.Vm;
using Xunit;

/// <summary>
/// The three engine projects (docs/superpowers/specs/2026-11-12-engine-layering-design.md §3.3): handlers in
/// <c>Ome.Engine.Vm</c>, natives in <c>Ome.Engine</c>, device interfaces in <c>Ome.Engine.Devices</c>. Calls go one
/// way, and handler code cannot name a device type. The build enforces most of it; these tests are the backstop.
/// </summary>
public class ProjectBoundaryTests
{
    private static readonly Assembly Handlers = typeof(VirtualMachine).Assembly;
    private static readonly Assembly Natives = typeof(InputAndWaits).Assembly;
    private static readonly Assembly Devices = typeof(RgbaImage).Assembly;

    [Fact]
    public void EachLayerIsItsOwnAssembly()
    {
        Assert.Equal("Ome.Engine.Vm", Handlers.GetName().Name);
        Assert.Equal("Ome.Engine", Natives.GetName().Name);
        Assert.Equal("Ome.Engine.Devices", Devices.GetName().Name);
    }

    [Theory]
    [InlineData(typeof(RgbaImage))]
    [InlineData(typeof(GlyphMask))]
    [InlineData(typeof(GlyphRasterRequest))]
    [InlineData(typeof(IGlyphMaskRasterizer))]
    [InlineData(typeof(ILanguageFontRasterizer))]
    [InlineData(typeof(IFontFamilySource))]
    [InlineData(typeof(EnumeratedFontFamily))]
    [InlineData(typeof(IMovieDecoder))]
    [InlineData(typeof(IMovieDecoderFactory))]
    [InlineData(typeof(IPaceClock))]
    public void DeviceTypesLiveInTheDeviceAssembly(Type type) => Assert.Same(Devices, type.Assembly);

    [Fact]
    public void TheHandlerAssemblyDoesNotReferenceTheDeviceAssembly()
    {
        string[] references = Handlers.GetReferencedAssemblies().Select(name => name.Name!).ToArray();
        Assert.Contains("Ome.Engine", references);
        Assert.DoesNotContain("Ome.Engine.Devices", references);
    }

    [Fact]
    public void CallsGoOneWay()
    {
        Assert.DoesNotContain("Ome.Engine.Vm", Natives.GetReferencedAssemblies().Select(name => name.Name));
        Assert.DoesNotContain(Devices.GetReferencedAssemblies(), name => name.Name!.StartsWith("Ome.", StringComparison.Ordinal));
    }

    [Fact]
    public void TheProjectsReferenceOnlyTheLayerBelow()
    {
        Assert.Equal(["Ome.Engine"], ProjectReferences("Ome.Engine.Vm"));
        Assert.Equal(["Ome.Engine.Devices"], ProjectReferences("Ome.Engine"));
        Assert.Empty(ProjectReferences("Ome.Engine.Devices"));
        // Without it, Ome.Engine.Vm would see Ome.Engine.Devices through Ome.Engine.
        Assert.Equal("true", Property("Ome.Engine.Vm", "DisableTransitiveProjectReferences"));
    }

    [Fact]
    public void OnlyTheLuaProjectNamesTheLuaLibrary()
    {
        // Lua scripting spec §14.1 and gate 14: Ome.Engine and Ome.Engine.Vm reference no Lua library; the runtime seam
        // is Ome.Engine's, and Ome.Scripting.Lua, which references Ome.Engine only, implements it. The milestone 0 spike
        // is a measuring program that the engine never references.
        var projects = Directory.EnumerateFiles(Path.Combine(Paths.Repo, "engine"), "*.csproj", SearchOption.AllDirectories)
            .Append(Path.Combine(Paths.Repo, "godot", "OME.csproj"))
            .Where(path => !path.Replace('\\', '/').Split('/').Any(part => part is "obj" or "bin"));
        var naming = new List<string>();
        foreach (string path in projects)
            if (XDocument.Load(path).Descendants("PackageReference").Any(reference =>
                    reference.Attribute("Include")?.Value is { } package
                    && (package.StartsWith("LuaCSharp", StringComparison.OrdinalIgnoreCase)
                        || package.StartsWith("MoonSharp", StringComparison.OrdinalIgnoreCase))))
                naming.Add(Path.GetFileNameWithoutExtension(path));
        Assert.Equal(["Ome.Scripting.Lua", "Ome.Scripting.Lua.Spike"], naming.Order(StringComparer.Ordinal));
        Assert.Equal(["Ome.Engine"], ProjectReferences("Ome.Scripting.Lua"));
        Assert.DoesNotContain(ProjectReferences("Ome.Engine.Vm"), name => name.Contains("Lua", StringComparison.Ordinal));
        foreach (Assembly assembly in new[] { Handlers, Natives })
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(),
                name => name.Name!.StartsWith("Lua", StringComparison.Ordinal) || name.Name == "MoonSharp.Interpreter");
    }

    /// <summary>
    /// The natives' interfaces (spec §9, acceptance 1): every interface the Ome.Engine assembly exports, except the
    /// seams the natives call out through to what the front end and the platform supply, each with its reason.
    /// </summary>
    private static readonly Dictionary<Type, string> OutwardSeams = new()
    {
        // The dialogs device in all but its place (spec §6.1): its boxes carry AGE's messages and op 0x144's name
        // entry, so it stays in Ome.Engine, and Main's modal dialogs are not restructured (spec §10, §14).
        [typeof(IShellDialogs)] = "dialogs",
        // No file device until a platform needs one (spec §6.4).
        [typeof(IAssetStore)] = "files",
        [typeof(Ome.Engine.Persistence.INativeDatStore)] = "files",
        // The scripts the interpreter loads, from the game's catalog or a self-test's own.
        [typeof(IScriptProvider)] = "scripts",
        // The game's sounds and movies, which the audio and movie natives read through the resource map
        // (ResourceAudioAssets, ResourceMovieAssets) and hand to their devices as device data; tests give their own.
        [typeof(IAudioAssets)] = "sounds",
        [typeof(IMovieAssets)] = "movies",
        // The trace consumers (the CLI, the front end's logs, the tests).
        [typeof(Ome.Engine.Diagnostics.ITraceSink)] = "trace",
        // A glyph rasterizer's metrics, which the GDI and Godot rasterizers give.
        [typeof(IFontMetricsProbe)] = "glyphs",
        // The Lua runtime (Lua scripting spec §14.1): every front end runs the same Lua, so it is no device; the
        // scripting native and the link call it, and only Ome.Scripting.Lua implements it.
        [typeof(Ome.Engine.Modding.IModRuntime)] = "lua",
        [typeof(Ome.Engine.Modding.IModSession)] = "lua",
    };

    private static Type[] NativeInterfaces()
        => Natives.GetExportedTypes().Where(type => type.IsInterface && !OutwardSeams.ContainsKey(type)).ToArray();

    [Fact]
    public void NoNativeInterfaceNamesADeviceType()
    {
        // A native interface member that carries a device type cannot be called from handler code (CS0012), so none
        // may have one (spec §3.3).
        Assert.Contains(typeof(IInputAndWaits), NativeInterfaces());
        var offenders = new List<string>();
        foreach (Type contract in NativeInterfaces())
        foreach (MemberInfo member in contract.GetMembers())
        foreach (Type used in Signature(member).SelectMany(Expand))
            if (used.Assembly == Devices)
                offenders.Add($"{contract.Name}.{member.Name}: {used.Name}");
        Assert.Empty(offenders);
    }

    [Fact]
    public void NoNativeInterfaceIsNamedHost()
    {
        // Spec §2, decision 13: the host's interfaces were renamed after their natives in milestone 12, and the lift's
        // own (EngineHost, IFrontEnd) are gone.
        Assert.Empty(NativeInterfaces().Where(type => type.Name.Contains("Host", StringComparison.Ordinal))
                         .Select(type => type.Name));
        foreach (string gone in new[] { "IHost", "IFrontEnd", "EngineHost", "IAdvHost", "IGraphicsHost", "IInputHost",
                                        "ILifecycleHost", "IMovieHost", "IDiagnosticHost", "ISurfacePixels" })
            Assert.DoesNotContain(Natives.GetExportedTypes(), type => type.Name == gone);
    }

    [Fact]
    public void GodotImplementsNoNativeInterface()
    {
        // godot/ implements device interfaces and builds the engine (spec §6.6). The scan reads base lists: Main is the
        // cursor device, which shows that it finds them.
        string[] natives = NativeInterfaces().Select(type => type.Name).ToArray();
        var declaration = new Regex(@"\b(?:class|record|struct)\s+(\w+)[^{;=]*?:\s*([^{;]+?)(?:\bwhere\b|\{|;)");
        var bases = new List<(string Type, string Base)>();
        foreach (string path in Directory.EnumerateFiles(Path.Combine(Paths.Repo, "godot"), "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(Paths.Repo, path).Replace('\\', '/');
            if (relative.Split('/').Any(part => part is "obj" or "bin" or ".godot")) continue;
            foreach (Match match in declaration.Matches(File.ReadAllText(path)))
            foreach (Match name in Regex.Matches(match.Groups[2].Value, @"[A-Za-z_][\w.]*"))
                bases.Add((match.Groups[1].Value, name.Value.Split('.')[^1]));
        }
        Assert.Contains(("Main", nameof(ICursorDevice)), bases);
        Assert.Empty(bases.Where(pair => natives.Contains(pair.Base)));
    }

    [Fact]
    public void TheVmTakesNoPaceClock()
    {
        // Milestone 5: fast-forward pacing belongs to the skip-and-auto native, which composition builds with a clock.
        IEnumerable<Type> parameters = typeof(VirtualMachine).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(constructor => constructor.GetParameters()).Select(parameter => parameter.ParameterType);
        Assert.DoesNotContain(typeof(IPaceClock), parameters);
        Assert.Contains(typeof(SkipAndAuto), parameters);
        Assert.Contains(typeof(IPaceClock), typeof(SkipAndAuto).GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType));
    }

    [Fact]
    public void SkipAndAutoStateIsPrivateToItsNative()
    {
        // The census (docs/superpowers/plans/2026-11-12-engine-layering.md §4, "Skip and auto") found this state in the
        // VM and the host; milestone 5 gave it one owner, and neither keeps a copy.
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Assert.Empty(typeof(SkipAndAuto).GetFields(instance).Where(field => !field.IsPrivate).Select(field => field.Name));
        string[] census =
        [
            "_paceClock", "_fastForwardPacing", "_fastForwardOwedMs", "_fastForwardStartMs", "_fastForwardPagePassed",
            "_advSkipServiceEnabled", "_physicalFastForwardLock", "_physicalSkipActionHeld", "_revealPhysicalSkipSuppressed",
            "_skipClickCancelState", "_skipClickCancelTriggers", "_autoMessageEnabled", "_autoMessageTime0Ms",
            "_autoMessageTime1Ms", "_autoVoicePending", "_messageSkipEnabled", "_messageSkipServiceActive",
            "_messageSkipInjected", "_advReadSkipState", "_autoMessageTime0Milliseconds", "_autoMessageTime1Milliseconds",
            "_messageSkipLock", "_scriptMessageSkipActive", "_physicalMessageSkipActive", "_messageSkipActive",
        ];
        Assert.Empty(typeof(VirtualMachine).GetFields(instance).Select(field => field.Name).Intersect(census));
    }

    [Fact]
    public void AudioAndTickStateIsPrivateToTheirNatives()
    {
        // The census (docs/superpowers/plans/2026-11-12-engine-layering.md §4, "Engine tick" and "Audio") found this
        // state in the VM and the host; milestone 6 gave it to the audio native and the engine tick. The host keeps
        // _presentationWithheldByStatementBlock, which only brackets the timeline's withheld stretches on the front
        // end's thread (spec §5.3, "As built").
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (Type native in new[] { typeof(Audio), typeof(EngineTick) })
            Assert.Empty(native.GetFields(instance).Where(field => !field.IsPrivate).Select(field => field.Name));
        string[] census =
        [
            "_statementBlockActive", "_statementBlockDepth", "_audioMixerSettings", "_currentBgmTrackId",
            "_bgmFadeTarget", "_loadedSoundEffectResourceIds", "_voiceMuteNextFlags", "_sfxNames", "_voiceBgmDuckControl",
            "_queuedSkippedVoice", "_scheduledVoiceLock", "_scheduledVoice", "_bgmFadeDeadlineMs",
        ];
        Assert.Empty(typeof(VirtualMachine).GetFields(instance).Select(field => field.Name).Intersect(census));
    }

    [Fact]
    public void GraphicsStateIsPrivateToItsNative()
    {
        // The census (docs/superpowers/plans/2026-11-12-engine-layering.md §4, "Graphics") found the surface table and
        // the publication and screen transition state in the host; milestone 7 gave them to the graphics native. The
        // host keeps the waits that block on them until milestone 11: the presentation barrier and request
        // generations, _foregroundGfx, _foregroundTransitionWaitBypassed and _transitionStartedAtMs.
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Assert.Empty(typeof(Graphics).GetFields(instance).Where(field => !field.IsPrivate).Select(field => field.Name));
        string[] census =
        [
            "_screenTransitionLock", "_renderTargetSnapshots", "_backbufferRangeLock", "_backbufferRange",
            "_backbufferPublicationPreservesExistingPixels", "_backbufferClearPending", "_screenTransition",
            "_presentRequested", "_terminalSample", "_imageLock", "_images", "_surfaceImages", "_surfaceColorKeys",
            "_surfaceResources", "_slotDims", "_screenWidth", "_screenHeight",
        ];
        Assert.Empty(typeof(VirtualMachine).GetFields(instance).Select(field => field.Name).Intersect(census));
    }

    [Fact]
    public void TheFrontEndDrawsFrameDescriptions()
    {
        // Spec §11, milestone 7: the object traversal is written once, in the graphics native's lowering
        // (Natives/Graphics.Frame.cs), and the front end's drawing reads frame descriptions, not the object model.
        // Comments may name them.
        foreach (string file in new[] { "Main.Compositor.cs", "GpuRetainedRenderer.cs" })
        {
            string[] code = File.ReadAllLines(Path.Combine(Paths.Repo, "godot", file))
                .Select(line => line.IndexOf("//", StringComparison.Ordinal) is int comment and >= 0 ? line[..comment] : line)
                .ToArray();
            foreach (string word in new[] { "RenderObject", "GfxState", ".Gfx", "SnapshotVisibleObjects", "Transform2DMath" })
                Assert.DoesNotContain(code, line => line.Contains(word, StringComparison.Ordinal));
        }
        string[] traversals = Directory.EnumerateFiles(Path.Combine(Paths.Repo, "engine"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(Paths.Repo, "godot"), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                           && !path.Contains("Ome.Engine.Tests"))
            .Where(path => File.ReadAllText(path).Contains("Transform2DMath.Build(", StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path))
            .Where(name => !name.StartsWith("GfxState", StringComparison.Ordinal))   // the object model samples its range transform
            .ToArray();
        Assert.Equal(["Graphics.Frame.cs"], traversals);
    }

    [Fact]
    public void MovieStateIsPrivateToItsNative()
    {
        // The census (docs/superpowers/plans/2026-11-12-engine-layering.md §4, "Movies" and "The script stack") found
        // the movie bindings, the mask playbacks and the host's copy of the script stack in the host; milestone 8 gave
        // the first two to the movie native and removed the third. The host keeps op 0x20f's modal wait and its three
        // fields until milestone 11.
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Assert.Empty(typeof(Movies).GetFields(instance).Where(field => !field.IsPrivate).Select(field => field.Name));
        string[] census =
        [
            "_movieSurfaces", "_movieMaskLock", "_movieMasksByPlayback", "_movieMaskPlaybackBySurface",
            "_scriptContexts", "_rootScene",
        ];
        Assert.Empty(typeof(VirtualMachine).GetFields(instance).Select(field => field.Name).Intersect(census));
    }

    [Fact]
    public void ScriptMemorySaveAndShellStateIsPrivateToItsNatives()
    {
        // The census (docs/superpowers/plans/2026-11-12-engine-layering.md §4, "Script memory", "Saves and settings"
        // and "Shell services") found this state in the VM and the host; milestone 9 gave it to the script memory, save
        // and settings, and shell-services natives. The VM keeps the banks' forwarders (Globals and the rest), the
        // saved frame its full load restores next, and ScriptError, the run's outcome.
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (Type native in new[] { typeof(ScriptMemory), typeof(SavesAndSettings), typeof(ShellServices) })
            Assert.Empty(native.GetFields(instance).Where(field => !field.IsPrivate).Select(field => field.Name));
        string[] census =
        [
            "_deterministicRandomLock", "_deterministicRandomSeed", "_deterministicRandomDraws",
            "_deterministicRandomSites", "_nativeStringEncoding", "_intQueues", "_intStacks",
            "_sharedProfile", "_nativeDatStore", "_sharedProfileShutdownLock", "_loadedNumberedState",
            "_retainedNativeNumberedState", "_playClock", "_playBaseSeconds", "_playStartSeconds", "_pendingPlaySeconds",
            "_sharedProfileShutdownHandled", "_messageSettingsStore",
            "_menuToggles", "_menuTogglesPending", "_windowCallbackPending", "_settingCallbackPending",
            "_diagnosticOutput", "_systemMenuActionsEnabled", "_systemMenuShowDelayMilliseconds", "_systemKeyDisableMask",
            "_diagnosticMessageCompleted", "_scriptErrorCompleted", "_scriptErrorChoice", "_fullwidthTextEditCompleted",
            "_fullwidthTextEditLock", "_fullwidthTextEditResult",
        ];
        Assert.Empty(typeof(VirtualMachine).GetFields(instance).Select(field => field.Name).Intersect(census));
    }

    [Fact]
    public void TextStateIsPrivateToItsNative()
    {
        // The census (docs/superpowers/plans/2026-11-12-engine-layering.md §4, "Text") found the layouts, the reveal,
        // the wait indicator, the message settings, the fonts and the presenter split between the VM and the host;
        // milestone 10 gave them to the text native, the presenter inside it (spec §8). The VM keeps the message whose
        // runs it is showing (its scene's PresentedMessage) (it names the VM's frames and script), and the host keeps the reveal's
        // wait until milestone 11.
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Assert.Empty(typeof(TextManager).GetFields(instance).Where(field => !field.IsPrivate).Select(field => field.Name));
        string[] census =
        [
            "_presenter", "_advTextRevealing", "_stringStores", "_recordsStringStores", "_advTextStyle",
            "_messageWindowAlphaSetting", "_messageGlyphDelayMilliseconds", "_textLock", "_surfaceTextMaskCache",
            "_surfaceTextBackendInfo", "_sourceText", "_presentedText", "_presentsText", "_surfaceTextRasterizer",
            "_presentedTextRasterizer", "_presentedFonts", "_retainedHistoryLayouts", "_liveText", "_retainedTextLayouts",
            "_activeLiveText", "_suspendedRetainedTextLayoutSlot", "_advText", "_layoutCursors", "_currentAdvLayout",
            "_glyphTimer", "_textAntialias", "_advTextForceComplete", "_revealsFinishedUntilWait", "_waitIndicators",
            "_waitIndicatorFollowLastGlyph", "_waitIndicatorBindings", "_retainedWaitIndicators", "_activeWaitLayout",
            "_waitIndicatorStartedMs", "_waitIndicatorEnabled", "_advPagePresentationSuspended", "_fontFamilies",
            "_fontRealization",
        ];
        string[] properties =
        [
            "Presenter", "IsTextRevealing", "Captured", "RubyRules", "FontFamilySource", "FontMetricsProbe",
            "BlankExtentMode", "PresentedTextRasterizer", "PresentedTextMaskCacheCapacity",
        ];
        Assert.Empty(typeof(VirtualMachine).GetFields(instance).Select(field => field.Name).Intersect(census));
        Assert.Empty(typeof(VirtualMachine).GetProperties(instance).Select(property => property.Name)
                         .Intersect(properties));
    }

    [Fact]
    public void TheWaitsAreTheInputAndWaitsNatives()
    {
        // The census (plan §4, "Input and waits") found the waits' handles and flags in the host, with the presentation
        // waits' fields that milestones 7 and 8 left there; milestone 11 gave them to the input and waits native, and
        // milestone 12 removed the lifted host.
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Assert.Empty(typeof(InputAndWaits).GetFields(instance).Where(field => !field.IsPrivate).Select(field => field.Name));
        string[] census =
        [
            "_gate", "_inputCallbackSignal", "_frameSignal", "_stopRequested", "_rootReloadInterrupt",
            "_frontendModalRelease", "_frontendModalHolds", "_presentationBarrier", "_presentationRequestConsumed",
            "_synchronizeExplicitPresentation", "_explicitPresentationRequestGeneration",
            "_consumedPresentationRequestGeneration", "_lastSynchronizedPresentationTimestamp", "_foregroundGfx",
            "_foregroundTransitionWaitBypassed", "_transitionStartedAtMs", "_modalMovieWaiting", "_modalMovieCancelled",
            "_modalMovieCancelPending", "_presentationWithheldByStatementBlock",
        ];
        Assert.Empty(typeof(VirtualMachine).GetFields(instance).Select(field => field.Name).Intersect(census));
        // Milestone 12: the VM calls the native through its interface, which the native implements; nothing between.
        Assert.Equal(typeof(IInputAndWaits), typeof(VirtualMachine).GetField("_waits", instance)!.FieldType);
        Assert.Contains(typeof(IInputAndWaits), typeof(InputAndWaits).GetInterfaces());
        // What a game input does is the native's (InputAndWaits.Events.cs): the front end passes device events and
        // routes none to the interpreter (the driver may still read its state). Comments may name its members.
        string[] code = Directory.EnumerateFiles(Path.Combine(Paths.Repo, "godot"), "*.cs")
            .SelectMany(File.ReadAllLines)
            .Select(line => line.IndexOf("//", StringComparison.Ordinal) is int comment and >= 0 ? line[..comment] : line)
            .ToArray();
        foreach (MethodInfo method in typeof(IInterpreterInput).GetMethods().Where(method => !method.IsSpecialName))
            Assert.DoesNotContain(code, line => line.Contains("_vm." + method.Name + "(", StringComparison.Ordinal));
    }

    [Fact]
    public void TheShellsContractsHoldNothingANativeOwns()
    {
        // Milestone 9's done-when: the screen mode, the message toggles and the 設定 values are the shell native's, so
        // the front end's shell services and the interpreter's menu services keep only what neither native owns.
        Assert.Equal(["ChooseImageFolder", "HoldVm", "OpenPanel", "Quit", "RequestClose", "ReturnToTitle", "SaveScreenshot"],
                     typeof(IShellHost).GetMethods().Select(method => method.Name).Order());
        Assert.Empty(typeof(IShellHost).GetProperties());
        Assert.Equal(["get_MenuState", "RequestMenuHideWindow"],
                     typeof(IVmMenuServices).GetMethods().Select(method => method.Name).Order());
    }

    [Fact]
    public void TheFrontEndSuppliesOnlyTheMovieDecoder()
    {
        // Spec §11, milestone 8: a front end supplies the decoder and a stream for the sound; the movie native runs the
        // decoders, paces and feeds the sound, and completes the movies. Comments may name its parts.
        string[] code = Directory.EnumerateFiles(Path.Combine(Paths.Repo, "godot"), "*.cs")
            .SelectMany(File.ReadAllLines)
            .Select(line => line.IndexOf("//", StringComparison.Ordinal) is int comment and >= 0 ? line[..comment] : line)
            .ToArray();
        foreach (string word in new[]
                 {
                     "IMovieDecoder ", "MovieRuntime", "MovieAudioFeed", "MovieAudioTimeline", "MovieSurfaceRegistry",
                     "TryTakeFrame", "TryTakeAudioChunk", "AdvancePlaybackClock",
                 })
            Assert.DoesNotContain(code, line => line.Contains(word, StringComparison.Ordinal));
    }

    [Fact]
    public void TheGodotAudioDeviceRunsNoEnvelope()
    {
        // Spec §11, milestone 6: the BGM fade, the voice duck and its release are the audio native's; the device
        // (godot/Main.Audio.cs) plays what it is told. Comments may name them.
        string[] code = File.ReadAllLines(Path.Combine(Paths.Repo, "godot", "Main.Audio.cs"))
            .Select(line => line.IndexOf("//", StringComparison.Ordinal) is int comment and >= 0 ? line[..comment] : line)
            .ToArray();
        foreach (string word in new[] { "Tween", "Duck", "Fade" })
            Assert.DoesNotContain(code, line => line.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<Type> Signature(MemberInfo member) => member switch
    {
        MethodInfo method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType),
        PropertyInfo property => [property.PropertyType],
        EventInfo { EventHandlerType: { } handler } => [handler],
        _ => [],
    };

    private static IEnumerable<Type> Expand(Type type)
    {
        yield return type;
        if (type.HasElementType)
            foreach (Type inner in Expand(type.GetElementType()!)) yield return inner;
        if (type.IsGenericType)
            foreach (Type argument in type.GetGenericArguments())
            foreach (Type inner in Expand(argument)) yield return inner;
    }

    private static XDocument Project(string name)
        => XDocument.Load(Path.Combine(Paths.Repo, "engine", name, name + ".csproj"));

    private static string[] ProjectReferences(string name)
        => Project(name).Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value.Replace('\\', '/')))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string? Property(string name, string property)
        => Project(name).Descendants(property).SingleOrDefault()?.Value;
}
