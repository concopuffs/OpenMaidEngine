using Age.Engine.Persistence;
using Age.Engine.Sys4;
using Age.Script.Modules;

/// <summary><c>save-diff A B [--profile ID] [--section NAME]... [--limit N]</c>: compare two native save containers
/// (numbered SAVEnn.DAT, or any S3SD/S4SD file) by their decoded payloads (NativeSaveDiff). With --profile, global
/// differences carry the names of that profile's registry. Exit code 0 when the saves match, 1 when they differ,
/// 2 on a usage or decode error.</summary>
internal static class SaveDiffCommand
{
    private const int DefaultLimit = 40;

    public static int Run(string[] args)
    {
        var files = new List<string>();
        var sections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? profileId = null;
        int limit = DefaultLimit;
        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "--profile" or "--section" or "--limit")
            {
                if (i + 1 >= args.Length) return Usage();
                string value = args[++i];
                if (arg == "--profile") profileId = value;
                else if (arg == "--section") sections.Add(value);
                else if (!int.TryParse(value, out limit) || limit < 0) return Usage();
            }
            else if (arg.StartsWith("--", StringComparison.Ordinal)) return Usage();
            else files.Add(arg);
        }
        if (files.Count != 2) return Usage();
        foreach (string section in sections)
            if (!NativeSaveDiff.Sections.Contains(section, StringComparer.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"save-diff: unknown section '{section}'; sections: "
                                        + string.Join(", ", NativeSaveDiff.Sections));
                return 2;
            }

        NativeSaveDocument a, b;
        try
        {
            a = NativeSaveContainerCodec.Decode(File.ReadAllBytes(files[0]));
            b = NativeSaveContainerCodec.Decode(File.ReadAllBytes(files[1]));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException
                                              or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"save-diff: {exception.Message}");
            return 2;
        }

        GameModule? names = null;
        if (profileId is not null)
        {
            string globals = Paths.GlobalsJson(profileId);
            if (File.Exists(globals)) names = GameModule.LoadGlobals(profileId, globals);
            else Console.Error.WriteLine($"save-diff: no {globals}; globals stay unnamed (globals_build.py --build)");
        }

        Console.WriteLine($"A  {Describe(files[0], a)}");
        Console.WriteLine($"B  {Describe(files[1], b)}");
        NativeSaveComparison comparison = NativeSaveDiff.Compare(a, b);
        if (!comparison.Numbered)
            Console.WriteLine($"not a numbered save ({comparison.NumberedDecodeError}); payload dwords compared");

        var shown = comparison.Differences
            .Where(d => sections.Count == 0 || sections.Contains(d.Section)).ToList();
        foreach (var group in shown.GroupBy(d => d.Section))
        {
            int printed = 0;
            foreach (NativeSaveDifference d in group)
            {
                if (limit > 0 && printed == limit) break;
                Console.WriteLine($"{d.Section,-15} {Key(d),-10} {Field(d, names),-28} {d.A}  ->  {d.B}");
                printed++;
            }
            if (printed < group.Count()) Console.WriteLine($"{group.Key,-15} ... {group.Count() - printed} more");
        }
        Console.WriteLine(shown.Count == 0
            ? "no differences"
            : $"{shown.Count} differences: "
              + string.Join(", ", shown.GroupBy(d => d.Section).Select(g => $"{g.Key} {g.Count()}")));
        return shown.Count == 0 ? 0 : 1;
    }

    private static string Describe(string path, NativeSaveDocument document)
    {
        NativeSaveMetadata m = document.Metadata;
        NativeSystemTime t = m.Timestamp;
        return $"{path}  {m.Magic} {m.GameId}  {t.Year:0000}-{t.Month:00}-{t.Day:00} {t.Hour:00}:{t.Minute:00}:{t.Second:00}"
               + $"  play {m.AccumulatedPlaySeconds} s  payload {document.Payload.Length} bytes";
    }

    private static string Key(NativeSaveDifference d) => d.Key switch
    {
        < 0 => "",
        _ when d.Section is NativeSaveDiff.Metadata or NativeSaveDiff.Header => "",
        _ when d.Section is NativeSaveDiff.Frames or NativeSaveDiff.SoundEffects => d.Key.ToString(),
        _ => $"0x{d.Key:x}",
    };

    /// <summary>The record field, or for a global its registry name: the cell's own, or the array that holds it with
    /// the cell's offset.</summary>
    private static string Field(NativeSaveDifference d, GameModule? names)
    {
        int type = d.Section switch
        {
            NativeSaveDiff.IntegerGlobals => 0x3,
            NativeSaveDiff.FloatGlobals => 0x4,
            NativeSaveDiff.StringGlobals => 0x5,
            _ => 0,
        };
        if (type == 0 || names is null || d.Key < 0) return d.Field;
        uint address = (uint)d.Key;
        if (names.TryGet(type, address, out GlobalSymbol exact)) return exact.Name;
        if (names.TryGetContaining(type, address, out GlobalSymbol array, out uint offset))
            return $"{array.Name}[{offset}]";
        return d.Field;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: save-diff A.DAT B.DAT [--profile ID] [--section NAME]... [--limit N (0 = all)]");
        Console.Error.WriteLine("sections: " + string.Join(", ", NativeSaveDiff.Sections));
        return 2;
    }
}
