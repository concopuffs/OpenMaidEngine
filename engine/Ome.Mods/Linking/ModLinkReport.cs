using System.Text;

namespace Ome.Mods.Linking;

/// <summary>The classes of spec §7. <see cref="Error"/> and <see cref="HardConflict"/> stop a link.</summary>
public enum ModReportClass
{
    /// <summary>A mod that cannot load on its own: a bad package, a failed compile, a fingerprint mismatch.</summary>
    Error,
    /// <summary>Mods that cannot load together.</summary>
    HardConflict,
    /// <summary>Several mods inserting at one site, applied in load order.</summary>
    OrderedInteraction,
    /// <summary>Several mods supplying one script or asset; the last in load order wins.</summary>
    Override,
    Note,
}

/// <summary>One report line: its class, the mods involved, the script it concerns, and the message.</summary>
public sealed record ModReportLine(ModReportClass Class, IReadOnlyList<string> Mods, string? Script, string Message)
{
    public bool Stops => Class is ModReportClass.Error or ModReportClass.HardConflict;

    public override string ToString()
    {
        string label = Class switch
        {
            ModReportClass.Error => "error",
            ModReportClass.HardConflict => "conflict",
            ModReportClass.OrderedInteraction => "interaction",
            ModReportClass.Override => "override",
            _ => "note",
        };
        string mods = Mods.Count > 0 ? $"[{string.Join(", ", Mods)}] " : "";
        string script = Script != null ? $"{Script}: " : "";
        return $"{label}: {mods}{script}{Message}";
    }
}

/// <summary>The link report of spec §7: every finding, the resolved load order, and the mods dropped by
/// <c>--skip-failed-mods</c>.</summary>
public sealed class ModLinkReport
{
    private readonly List<ModReportLine> _lines = new();

    public IReadOnlyList<ModReportLine> Lines => _lines;
    public IReadOnlyList<string> Order { get; internal set; } = [];
    public IReadOnlyList<string> Dropped { get; internal set; } = [];
    public bool Stops => _lines.Any(line => line.Stops);

    /// <summary>The mods named by lines that stop the link.</summary>
    public IReadOnlyList<string> FailedMods
        => _lines.Where(line => line.Stops).SelectMany(line => line.Mods).Distinct().Order(StringComparer.Ordinal).ToList();

    public void Add(ModReportClass kind, IEnumerable<string> mods, string? script, string message)
        => _lines.Add(new ModReportLine(kind, mods.Distinct().ToList(), script, message));

    public void Add(ModReportClass kind, string mod, string? script, string message) => Add(kind, [mod], script, message);

    internal void AddRange(IEnumerable<ModReportLine> lines) => _lines.AddRange(lines);

    /// <summary>The report as <c>diagnostics/mod-link.txt</c> holds it.</summary>
    public string Render()
    {
        var text = new StringBuilder();
        text.Append("load order: ").AppendLine(Order.Count == 0 ? "(no mods)" : string.Join(" -> ", Order));
        if (Dropped.Count > 0) text.Append("dropped: ").AppendLine(string.Join(", ", Dropped));
        foreach (ModReportClass kind in Enum.GetValues<ModReportClass>())
            foreach (ModReportLine line in _lines.Where(line => line.Class == kind))
                text.AppendLine(line.ToString());
        return text.ToString();
    }

    /// <summary>A one-line console summary.</summary>
    public string Summary()
    {
        int Count(ModReportClass kind) => _lines.Count(line => line.Class == kind);
        return $"{Order.Count} mods; {Count(ModReportClass.Error)} errors, {Count(ModReportClass.HardConflict)} conflicts, "
               + $"{Count(ModReportClass.OrderedInteraction)} interactions, {Count(ModReportClass.Override)} overrides, "
               + $"{Count(ModReportClass.Note)} notes"
               + (Dropped.Count > 0 ? $"; dropped {string.Join(", ", Dropped)}" : "");
    }
}
