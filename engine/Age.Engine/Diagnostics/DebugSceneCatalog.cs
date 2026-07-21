using System.Globalization;
using System.Text.RegularExpressions;
using Age.Engine.Sys4;

namespace Age.Engine.Diagnostics;

public enum DebugScriptKind { Scenario, SecondaryEvent, Debug, Other }
public enum DebugScriptFilter { All, Scenario, SecondaryEvent, Debug, Other }

/// <summary>One script shown by the developer scene launcher. PackedId, rather than Name, is its identity.</summary>
public sealed record DebugSceneEntry(
    long PackedId,
    string Name,
    string Archive,
    long Size,
    int PackId,
    int RawIndex,
    DebugScriptKind Kind,
    bool Launchable);

/// <summary>Future profile-owned extension for a proven launch state; catalog rows use no extra writes.</summary>
public sealed record DebugLaunchPreset(
    string Label,
    long PackedScriptId,
    IReadOnlyDictionary<int, long> ExtraGlobalWrites,
    string Note);

/// <summary>Pure catalog/filter model shared by the Godot developer UI and unit tests.</summary>
public static partial class DebugSceneCatalog
{
    public static IReadOnlyList<DebugSceneEntry> Build(Sys4AssetCatalog catalog)
        => catalog.EnumerateScripts()
            .Select(item =>
            {
                string logicalName = StripAppendPrefix(item.Asset.Name);
                return new DebugSceneEntry(
                    item.PackedId,
                    item.Asset.Name,
                    item.Asset.Archive,
                    item.Asset.Size,
                    item.Asset.PackId,
                    item.Asset.RawIndex,
                    Classify(logicalName),
                    !logicalName.Equals("SYSTEM4.BIN", StringComparison.OrdinalIgnoreCase)
                        && !logicalName.Equals("TITLE.BIN", StringComparison.OrdinalIgnoreCase));
            })
            .OrderBy(entry => KindRank(entry.Kind))
            .ThenBy(entry => entry.Name, NaturalNameComparer.Instance)
            .ThenBy(entry => entry.PackedId)
            .ToArray();

    public static IReadOnlyList<DebugSceneEntry> Filter(
        IEnumerable<DebugSceneEntry> entries, DebugScriptFilter filter, string? query)
    {
        string needle = (query ?? "").Trim();
        return entries.Where(entry => MatchesFilter(entry, filter) && MatchesQuery(entry, needle)).ToArray();
    }

    public static DebugScriptKind Classify(string name)
    {
        string logicalName = StripAppendPrefix(Path.GetFileName(name));
        if (ScenarioName().IsMatch(logicalName)) return DebugScriptKind.Scenario;
        if (logicalName.StartsWith("SP", StringComparison.OrdinalIgnoreCase))
            return DebugScriptKind.SecondaryEvent;
        if (logicalName.StartsWith("DEBUG", StringComparison.OrdinalIgnoreCase))
            return DebugScriptKind.Debug;
        return DebugScriptKind.Other;
    }

    private static bool MatchesFilter(DebugSceneEntry entry, DebugScriptFilter filter)
        => filter == DebugScriptFilter.All || (int)entry.Kind == (int)filter - 1;

    private static bool MatchesQuery(DebugSceneEntry entry, string query)
    {
        if (query.Length == 0) return true;
        if (entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        if (query.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(query.AsSpan(2), NumberStyles.AllowHexSpecifier,
                             CultureInfo.InvariantCulture, out long hex))
            return entry.PackedId == hex;
        return long.TryParse(query, NumberStyles.Integer, CultureInfo.InvariantCulture, out long dec)
            && entry.PackedId == dec;
    }

    private static string StripAppendPrefix(string name) => AppendPrefix().Replace(name, "", 1);
    private static int KindRank(DebugScriptKind kind) => kind switch
    {
        DebugScriptKind.Scenario => 0,
        DebugScriptKind.SecondaryEvent => 1,
        DebugScriptKind.Debug => 2,
        _ => 3,
    };

    [GeneratedRegex(@"^SC\d{4}\.BIN$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScenarioName();

    [GeneratedRegex(@"^\$\d+\$", RegexOptions.CultureInvariant)]
    private static partial Regex AppendPrefix();

    private sealed class NaturalNameComparer : IComparer<string>
    {
        public static NaturalNameComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            left ??= "";
            right ??= "";
            int li = 0, ri = 0;
            while (li < left.Length && ri < right.Length)
            {
                if (char.IsDigit(left[li]) && char.IsDigit(right[ri]))
                {
                    int lstart = li, rstart = ri;
                    while (li < left.Length && char.IsDigit(left[li])) li++;
                    while (ri < right.Length && char.IsDigit(right[ri])) ri++;
                    ReadOnlySpan<char> ln = left.AsSpan(lstart, li - lstart).TrimStart('0');
                    ReadOnlySpan<char> rn = right.AsSpan(rstart, ri - rstart).TrimStart('0');
                    int length = ln.Length.CompareTo(rn.Length);
                    if (length != 0) return length;
                    int numeric = ln.CompareTo(rn, StringComparison.Ordinal);
                    if (numeric != 0) return numeric;
                    int padded = (li - lstart).CompareTo(ri - rstart);
                    if (padded != 0) return padded;
                    continue;
                }

                int character = char.ToUpperInvariant(left[li]).CompareTo(char.ToUpperInvariant(right[ri]));
                if (character != 0) return character;
                li++;
                ri++;
            }
            return (left.Length - li).CompareTo(right.Length - ri);
        }
    }
}
