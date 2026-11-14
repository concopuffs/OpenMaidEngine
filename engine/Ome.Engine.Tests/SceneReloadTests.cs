using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Ome.Engine.Diagnostics;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Xunit;

/// <summary>
/// No hand reset (docs/superpowers/specs/2026-11-12-engine-layering-design.md, acceptance 6): each owner of scene
/// state keeps it in one private <c>Scene</c> object that op 0x9's reset builds again, so a reload assigns no field by
/// hand, and a scene leaves nothing behind for the next.
/// </summary>
public class SceneReloadTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                                          | BindingFlags.DeclaredOnly;

    /// <summary>
    /// Every owner of scene state, with the fields it keeps across a reload: the run's, the process's or the front
    /// end's, each group with its reason. Every other mutable field is its scene's. A field whose value is a part (a
    /// native, a device, a lock) rather than state needs no entry: it is read-only and not a collection. The movie
    /// native has no scene: each movie belongs to a surface, and the reset deletes them all with the release worker.
    /// </summary>
    private static readonly Dictionary<Type, string[]> Kept = new()
    {
        [typeof(VirtualMachine)] =
        [
            // The run's outcome and counters.
            "HaltReason", "ScriptError", "CompatibilityFailure", "Steps", "CallScriptDispatches",
            // The frame stack, empty at a reload: op 0x9 has unwound every frame.
            "_cur", "_depth", "_activeFrameNames", "_activeExecutionFrames", "_debugNextFrameId", "_restoreFrameIndex",
            // The handler table.
            "_handlers",
            // A wait's flags, set and cleared within the wait; no wait runs at a reload.
            "_advTextWaitPolling", "_advInputPollPageless", "_advWaitCoroutineYieldRequested", "_hiddenWindowWaiting",
            "_hiddenWindowWheelTriggers",
            // Input: the pointer stays where it is, and the reset polls the latch and discards it (TakeHeldTriggers).
            "_pointerX", "_pointerY", "_heldTriggers",
            // Read only with a mouse callback target (the scene's), and set when one registers.
            "_mouseCallbackIntervalMs", "_mouseCallbackArmedAtMs",
        ],
        [typeof(ScriptMemory)] =
        [
            // A queue lives for the run; the driver's deterministic random draws are the run's.
            "_queues", "_deterministicRandomSeed", "_deterministicRandomSites", "_deterministicRandomDraws",
        ],
        [typeof(Audio)] =
        [
            // What plays keeps playing (a reset stops no channel and unloads none), with its BGM fade and duck.
            "_playing", "_playIds", "_lastPlayId", "_soundEffectNames", "_soundEffectResourceIds", "_voicePlaybackVariant",
            "_bgmTrack", "_bgmLoop", "_bgmLoaded", "_bgmLevel", "_bgmChannelLevel", "_fade", "_duckActive",
            "_duckReleaseArmed", "_duckRestoreLevel",
            // Whether anything is timed: the reset works it out again (UpdateTimedWork).
            "_timedWork",
        ],
        [typeof(EngineTick)] = [],
        [typeof(Graphics)] =
        [
            // The surfaces stay, with their pixels, keys, resources and a render target's snapshot (op 0x9 releases
            // them only under set:AutoFreeTex).
            "_images", "_surfaceImages", "_surfaceColorKeys", "_surfaceResources", "_slotDims", "_renderTargetSnapshots",
            // The front end's frames and the presentation request (the reset makes one).
            "_frames", "_frameObjects", "_frameScreenTransitions", "_nextFrame", "_presentRequested", "_terminalSample",
            // Parts set after construction, and tracing.
            "_movies", "_listeners", "TraceOps", "OpTrace",
        ],
        [typeof(GfxState)] =
        [
            // The surfaces stay; the display background and the tiled-surface edge are the process's.
            "_surfaces", "_createdSurfaces", "DisplayBackgroundColor", "TiledSurfaceEdgeLength",
            // The animation clock: the reset runs the object clear's part (ClearAnimationServiceForObjectClear), which
            // keeps the persistent flags and counts the generation on.
            "AnimClockDurationTicks", "AnimClockGeneration", "_animClockStartMs", "AnimationServiceFlags",
            // Count up so the renderer republishes.
            "_mutationGeneration", "_publishedMutationGeneration",
        ],
        [typeof(InputAndWaits)] =
        [
            // The front end's frame turns, input queue, requests and modal holds, and the engine thread.
            "_turn", "_turnRequested", "_turnAbandoned", "_turnPerf", "_turnDecisions", "_turnLowering", "_published",
            "_publishedLate", "_workQueued", "_requests", "_input", "_advanceQueued", "_frontendModalHolds",
            "_blockHandles", "_engineThreadId", "SleepScale",
            // The run's page counts (the reset posts a notice).
            "_pages", "_pageNotices",
            // The interpreter input goes to, and its script frames (none at a reload).
            "_interpreter", "_scriptDepth",
            // Presentation's generations and burst, which count on across scenes.
            "_explicitPresentationRequestGeneration", "_consumedPresentationRequestGeneration",
            "_lastSynchronizedPresentationTimestamp", "_presentationWithheldByStatementBlock", "_burstSuspended",
            // A transition wait's flag, set and cleared within the wait.
            "_foregroundTransitionWaitBypassed",
        ],
        [typeof(SavesAndSettings)] =
        [
            // A full load's state until its frames are restored, the last load's records, and the run's shutdown.
            "_loaded", "_retained", "_shutdownHandled",
        ],
        [typeof(ShellServices)] =
        [
            // The window's screen mode is the process's; the reset cancels the open dialogs (CancelDialogs).
            "_windowed", "_forceScreen", "_requestedScreenMode", "_openDialogs",
            // The engine thread's wake, set once at composition.
            "_wake",
        ],
        [typeof(SkipAndAuto)] =
        [
            // The fast-forward channels: the reset ends them through their setters, which tell the listeners.
            "_scriptFastForward", "_physicalFastForward", "_fastForward", "_listeners",
            // The pace and the action-6 history are kept, as are the Auto delays (settings).
            "_fastForwardPacing", "_fastForwardStartMs", "_fastForwardOwedMs", "_fastForwardPagePassed",
            "_physicalSkipActionHeld", "_revealPhysicalSkipSuppressed", "_autoMessageTime0Ms", "_autoMessageTime1Ms",
        ],
        [typeof(TextManager)] =
        [
            // The settings, the current layout, the fonts and the language's presenter stay.
            "_messageGlyphDelayMilliseconds", "_messageWindowAlphaSetting", "_textAntialias", "_currentAdvLayout",
            "_presenter", "_presentedFonts", "_presentedTextRasterizer", "_presentsText", "_recordsStringStores",
            // One run's geometry and cut, set and read around the run.
            "_runGeometry", "_runCut",
            // Read only while the wait indicator is on, which enabling it sets.
            "_waitIndicatorStartedMs",
            // The runs shown (a tool's record) and parts set after construction.
            "Emitted", "_inputWaiting", "_listeners",
        ],
    };

    [Fact]
    public void EveryOwnersStateIsItsSceneOrKeptAcrossAReload()
    {
        var unclassified = new List<string>();
        var stale = new List<string>();
        foreach ((Type owner, string[] kept) in Kept)
        {
            FieldInfo[] fields = owner.GetFields(Instance);
            Assert.Contains(fields, field => field.Name == "_scene");
            Type scene = owner.GetNestedType("Scene", BindingFlags.NonPublic)!;
            Assert.Equal(scene, owner.GetField("_scene", Instance)!.FieldType);
            foreach (FieldInfo field in fields)
            {
                string name = MemberName(field);
                if (name == "_scene" || kept.Contains(name) || IsPart(field)) continue;
                unclassified.Add($"{owner.Name}.{name}");
            }
            stale.AddRange(kept.Where(name => !fields.Any(field => MemberName(field) == name))
                               .Select(name => $"{owner.Name}.{name}"));
        }
        Assert.True(unclassified.Count == 0,
                    "state neither in a Scene nor kept across a reload: " + string.Join(", ", unclassified));
        Assert.True(stale.Count == 0, "kept entries that name no field: " + string.Join(", ", stale));
    }

    /// <summary>The reset methods op 0x9 runs, each in the file that holds it.</summary>
    private static readonly (string File, string Signature)[] Resets =
    [
        ("engine/Ome.Engine.Vm/VirtualMachine.cs", "private void ResetSceneContextForRootReload()"),
        ("engine/Ome.Engine/Model/GfxState.cs", "public void ResetSceneContext()"),
        ("engine/Ome.Engine/Natives/Audio.cs", "public void ResetScene()"),
        ("engine/Ome.Engine/Natives/EngineTick.cs", "public void ResetScene()"),
        ("engine/Ome.Engine/Natives/Graphics.Transitions.cs", "public void ResetScene()"),
        ("engine/Ome.Engine/Natives/InputAndWaits.cs", "public void ResetScene()"),
        ("engine/Ome.Engine/Natives/Movies.cs", "public void ResetScene()"),
        ("engine/Ome.Engine/Natives/SavesAndSettings.cs", "public void ResetScene()"),
        ("engine/Ome.Engine/Natives/ScriptMemory.cs", "public void ResetScene()"),
        ("engine/Ome.Engine/Natives/ShellServices.cs", "public void ResetScene()"),
        ("engine/Ome.Engine/Natives/SkipAndAuto.cs", "public void ResetScene()"),
        ("engine/Ome.Engine/Natives/TextManager.cs", "public void ResetScene()"),
    ];

    [Fact]
    public void AReloadAssignsNoFieldByHand()
    {
        // A reset builds its scene again and runs native's actions (a release worker, a presentation request, the
        // other natives' resets); it assigns, increments or clears no field of its own.
        Assert.Equal(["_x =", "Style =", "++", ".Clear("],
                     ByHand("{ _scene = new(); _x = 1; Style = AdvTextStyle.Default; _n++; _y.Clear(); a == b; }"));
        var found = new List<string>();
        foreach ((string file, string signature) in Resets)
            found.AddRange(ByHand(Body(File.ReadAllText(Path.Combine(Paths.Repo, file)), signature))
                               .Select(match => $"{Path.GetFileName(file)}: {match.Trim()}"));
        Assert.Empty(found);
    }

    /// <summary>What a method body assigns, increments or clears by hand, other than its scene.</summary>
    private static List<string> ByHand(string body)
    {
        var assignment = new Regex(@"([A-Za-z_][\w.]*)\s*(?:[-+*/%|&^]|<<|>>|\?\?)?=(?![=>])");
        var mutation = new Regex(@"\+\+|--|\.Clear\(|Array\.Clear\(|Interlocked\.|Volatile\.Write\(");
        return assignment.Matches(body).Where(match => match.Groups[1].Value != "_scene").Select(match => match.Value)
                         .Concat(mutation.Matches(body).Select(match => match.Value)).ToList();
    }

    [Fact]
    public void AReloadKeepsNoSceneState()
    {
        // A script leaves state in each native's scene and reloads the root; the reloaded root ends the run. Every
        // scene then holds what it holds after a reload from a script that left nothing.
        static Operand I(long value) => new(0, value);
        static Operand G(long address) => new(3, address);
        List<(int, Operand[])> dirt =
        [
            (0xd3, []),                                                     // the timed callback sequence
            (0x55, [G(0x200), I(7)]),                                       // an integer global
            (0x138, [I(0), I(11)]),                                         // a push onto stack 0
            (0x142, [I(0)]),                                                // the system menu's actions grayed
            (0x149, [I(1000)]),                                             // its show delay
            (0x10f, [I(0x201)]),                                            // the system-key disable mask
            (0x19b, []),                                                    // the skip service suspended
            (0x1cf, [I(1)]),                                                // the voice duck control
            (0x1d5, []),                                                    // the next voice muted
            (0x1f4, []),                                                    // a statement block opened
            (0x75, [I(31)]),                                                // the font size
            (0x1f8, [I(1), I(4), I(3), I(0)]),                              // a surface (kept: surfaces stay)
            (0x1fb, [I(0x10), I(1), I(0), I(0), I(4), I(3), I(5), I(6)]),   // an object drawn from it
            (0x222, [I(0x10), I(1)]),                                       // the backbuffer's published range
            (0x229, [I(0x10), I(1), I(2), I(3), I(4)]),                     // the range transform
            (0x20d, [I(1)]),                                                // a render target selected
            (0x21b, [I(1)]),                                                // tick presentation on
            (0x80, [I(7)]),                                                 // the default object slot
            (0x13a, [I(0), I(1), I(0), I(0), I(8), I(8)]),                  // a numeric glyph style
            (0x1b1, [I(1)]),                                                // the wait indicator follows the last glyph
        ];

        Dictionary<string, string> clean = SceneDumps([(0x9, [])], reload: true);
        Dictionary<string, string> left = SceneDumps([.. dirt, (0x2, [])], reload: false);
        Dictionary<string, string> reloaded = SceneDumps([.. dirt, (0x9, [])], reload: true);

        // The script left state in every scene but the save native's playtime, which only a loaded save moves.
        Assert.Equal([nameof(SavesAndSettings)], left.Keys.Where(owner => left[owner] == clean[owner]));
        foreach (string owner in clean.Keys)
            Assert.True(clean[owner] == reloaded[owner],
                        $"{owner}'s scene after the reload differs from a clean reload's:\n{reloaded[owner]}\n" +
                        $"clean:\n{clean[owner]}");
    }

    /// <summary>Each owner's scene once the run ends: the root script given, then (when it reloads) a root that ends
    /// the run at once.</summary>
    private static Dictionary<string, string> SceneDumps(List<(int, Operand[])> root, bool reload)
    {
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson);
        Script first = ScriptAssembler.Assemble(table, "ROOT", root, []);
        Script end = ScriptAssembler.Assemble(table, "END", [(0x2, [])], []);
        var clock = new FrameClock();
        using var locator = new PageLocatorState("ROOT", null);
        var saves = new SavesAndSettings(playClock: new StoppedClock());
        HeadlessNativesTests.NativeSet natives = HeadlessNativesTests.Natives(locator, clock, saves: saves);
        VirtualMachine vm = natives.Vm(first, table, provider: new ScriptMap(new Dictionary<long, Script> { [0] = end }));
        ((IInputAndWaits)natives.Waits).CancelNextModalMovie();   // the waits' scene: the next modal movie cancelled

        natives.Waits.RunEngine(() => vm.Run());

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(reload ? 1 : 0, natives.Waits.PageNotices);   // the reset's notice: the root was reloaded
        object memory = typeof(VirtualMachine).GetField("_memory", Instance)!.GetValue(vm)!;
        return new Dictionary<string, string>
        {
            [nameof(VirtualMachine)] = SceneOf(vm),
            [nameof(ScriptMemory)] = SceneOf(memory),
            [nameof(Audio)] = SceneOf(natives.Audio),
            [nameof(EngineTick)] = SceneOf(natives.Tick),
            [nameof(Graphics)] = SceneOf(natives.Graphics),
            [nameof(GfxState)] = SceneOf(natives.Graphics.State),
            [nameof(InputAndWaits)] = SceneOf(natives.Waits),
            [nameof(SavesAndSettings)] = SceneOf(natives.Saves),
            [nameof(ShellServices)] = SceneOf(natives.Shell),
            [nameof(SkipAndAuto)] = SceneOf(natives.Skip),
            [nameof(TextManager)] = SceneOf(natives.Text),
        };
    }

    private sealed class StoppedClock : TimeProvider
    {
        public override long GetTimestamp() => 1_000 * TimestampFrequency;
    }

    private static string SceneOf(object owner)
        => Dump(owner.GetType().GetField("_scene", Instance)!.GetValue(owner), 0, new HashSet<object>(ReferenceEqualityComparer.Instance));

    /// <summary>A value as text: its fields, a collection's items, a dictionary's entries in key order.</summary>
    private static string Dump(object? value, int depth, HashSet<object> seen)
    {
        if (value == null) return "null";
        Type type = value.GetType();
        if (value is string text) return '"' + text + '"';
        if (type.IsPrimitive || type.IsEnum || value is decimal) return Convert.ToString(value, CultureInfo.InvariantCulture)!;
        if (value is Delegate) return "delegate";
        if (value is Script script) return "script " + script.Name;
        if (depth > 8) return "…";
        if (!type.IsValueType && !seen.Add(value)) return "(seen)";
        if (value is IDictionary dictionary)
        {
            var entries = new List<string>();
            foreach (DictionaryEntry entry in dictionary)
                entries.Add(Dump(entry.Key, depth + 1, seen) + ": " + Dump(entry.Value, depth + 1, seen));
            entries.Sort(StringComparer.Ordinal);
            return "{" + string.Join(", ", entries) + "}";
        }
        if (value is IEnumerable items)
        {
            var list = new List<string>();
            foreach (object? item in items) list.Add(Dump(item, depth + 1, seen));
            return "[" + string.Join(", ", list) + "]";
        }
        var builder = new StringBuilder(type.Name).Append('(');
        bool firstField = true;
        for (Type? level = type; level != null && level != typeof(object); level = level.BaseType)
            foreach (FieldInfo field in level.GetFields(Instance))
            {
                if (!firstField) builder.Append(", ");
                firstField = false;
                builder.Append(MemberName(field)).Append('=').Append(Dump(field.GetValue(value), depth + 1, seen));
            }
        return builder.Append(')').ToString();
    }

    /// <summary>A field's name, or its property's for an auto-property's backing field.</summary>
    private static string MemberName(FieldInfo field)
    {
        Match backing = Regex.Match(field.Name, "^<(.+)>k__BackingField$");
        return backing.Success ? backing.Groups[1].Value : field.Name;
    }

    /// <summary>A read-only field that holds a part (a native, a device, a lock, a setting), not a collection of state.
    /// </summary>
    private static bool IsPart(FieldInfo field)
        => field.IsInitOnly && !typeof(IEnumerable).IsAssignableFrom(field.FieldType)
           || field.IsInitOnly && field.FieldType == typeof(string);

    /// <summary>The body of the method with <paramref name="signature"/>, without its comments and strings.</summary>
    private static string Body(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"no {signature}");
        int index = start + signature.Length;
        string rest = source[index..].TrimStart();
        string body;
        if (rest.StartsWith("=>", StringComparison.Ordinal))
            body = rest[..rest.IndexOf(';')];
        else
        {
            int open = source.IndexOf('{', index), depth = 0, at = open;
            do
            {
                if (source[at] == '{') depth++;
                else if (source[at] == '}') depth--;
                at++;
            } while (depth > 0);
            body = source[open..at];
        }
        body = Regex.Replace(body, @"//[^\n]*", "");
        return Regex.Replace(body, "\"(?:[^\"\\\\]|\\\\.)*\"", "\"\"");
    }
}
