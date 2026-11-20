using Ome.Engine.Persistence;
using Ome.Engine.Profiles;
using Ome.Engine.Vm;

/// <summary><c>save-info FILE [--game ID]</c>: what one native save file says about itself (NativeSaveInfo): its
/// identity and version stamp, and for a numbered save its frames, bank counts and graphics record size, and whether
/// the codec carries it through an encode. With --game (or its alias --profile) it is set against that game's profile
/// and engine build. Exit code 0 when the file reads and agrees, 1 when it differs from the profile or does not
/// survive the codec, 2 on a usage or decode error.</summary>
internal static class SaveInfoCommand
{
    public static int Run(string[] args)
    {
        string? file = null, profileId = null;
        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i];
            if (GameProfileSelection.IsGameOption(arg))
            {
                if (i + 1 >= args.Length) return Usage();
                profileId = args[++i];
            }
            else if (arg.StartsWith("--", StringComparison.Ordinal) || file != null) return Usage();
            else file = arg;
        }
        if (file == null) return Usage();

        NativeSaveIdentity? identity = null;
        string? buildId = null;
        if (profileId != null)
        {
            if (GameProfileRegistry.BuiltIn.Find(profileId) is not { } profile)
            {
                Console.Error.WriteLine($"save-info: no profile '{profileId}'");
                return 2;
            }
            EngineBuild build = EngineBuilds.ForProfile(profile);
            buildId = build.Id;
            identity = profile.Persistence.CreateExpectedNativeIdentity(build.Rules.Saves);
        }

        NativeSaveInfo info;
        try
        {
            info = NativeSaveInfo.Read(File.ReadAllBytes(file));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException
                                              or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"save-info: {exception.Message}");
            return 2;
        }

        NativeSaveMetadata m = info.Metadata;
        NativeSystemTime t = m.Timestamp;
        Console.WriteLine($"file       {file}");
        Console.WriteLine($"identity   {m.Magic} {m.GameId}  stamp \"{info.VersionStamp}\" (0x{m.CompatibilityId:x8})  "
                          + $"save version {m.SaveVersion1}.{m.SaveVersion2}");
        Console.WriteLine($"written    {t.Year:0000}-{t.Month:00}-{t.Day:00} {t.Hour:00}:{t.Minute:00}:{t.Second:00}  "
                          + $"play {m.AccumulatedPlaySeconds} s");
        Console.WriteLine($"payload    {info.PayloadBytes} bytes, then {info.TailBytes} bytes after the container");
        bool survives = true;
        if (info.Numbered is { } state)
        {
            Console.WriteLine($"numbered   {state.Frames.Count} frames (owner {state.SavedFrameOwner}), bgm {state.BgmTrackId}");
            for (int index = 0; index < state.Frames.Count; index++)
            {
                NativeSavedScriptFrame frame = state.Frames[index];
                Console.WriteLine($"frame {index,-4} script 0x{frame.ScriptId:x} parent {frame.ParentContext} resume "
                                  + $"{frame.ResumeIndex} call target {frame.CallTargetIndex} returns "
                                  + $"[{string.Join(",", frame.ReturnIndices)}]"
                                  + (frame.StoppedAtUnmatchedReturn ? " (stopped at an unmatched return)" : ""));
            }
            int[] counts = info.BankCounts!;
            Console.WriteLine($"banks      integers {counts[0]}, floats {counts[1]}, strings {counts[2]}, integer pointers "
                              + $"{counts[3]}, float pointers {counts[4]}, string pointers {counts[5]}");
            Console.WriteLine($"graphics   record size 0x{state.GraphicsRecordSize:x}, {state.GfxObjects.Count} objects"
                              + (state.GfxObjects.Count == 0 ? ""
                                  : $" (handles 0x{state.GfxObjects.Min(o => o.Handle):x} to 0x{state.GfxObjects.Max(o => o.Handle):x})")
                              + $", range first 0x{state.RangeTransformFirst:x} count {state.RangeTransformCount}");
            survives = info.ReencodedSectionsAgree;
            Console.WriteLine($"codec      decoded and encoded again: sections {(info.ReencodedSectionsAgree ? "agree" : "DIFFER")}, "
                              + $"bytes {(info.ReencodedBytesAgree ? "agree" : "differ")}");
        }
        else Console.WriteLine($"numbered   no ({info.NumberedDecodeError})");

        if (identity == null) return survives ? 0 : 1;
        IReadOnlyList<string> mismatches = info.MismatchesWith(identity);
        if (mismatches.Count == 0)
            Console.WriteLine($"profile    agrees with '{profileId}' on engine build {buildId}");
        foreach (string mismatch in mismatches) Console.WriteLine($"profile    DIFFERS: {mismatch}");
        return mismatches.Count == 0 && survives ? 0 : 1;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: save-info FILE [--game ID]");
        return 2;
    }
}
