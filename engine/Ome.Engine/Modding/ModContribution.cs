namespace Ome.Engine.Modding;

/// <summary>A contribution of the stack: its index in <see cref="ModRuntimeStack.Contributions"/>.</summary>
public readonly record struct ContributionId(int Index);

/// <summary>
/// A data file the link delivers to its kind's owner (data patches spec §5.2, §5.5). <paramref name="Kind"/> and its
/// <paramref name="Owner"/>; <paramref name="Mod"/>, the contributor, and <paramref name="File"/>, the file's path in its
/// package as the report and the trace print it (<c>data/slimes.toml</c>); the <paramref name="Document"/> and the
/// <paramref name="Values"/> it holds, which set the delivery's budget (spec §5.6); <paramref name="Unraised"/>, the
/// kind's events this install does not raise, in the order of the kind's <c>at</c> entries (spec §5.4); and
/// <paramref name="Requires"/>, the Lua mods of the stack the contributor requires, whose state its own follows when it
/// ships no Lua of its own (spec §5.6, "A mod is on or off as a whole").
/// </summary>
public sealed record ModContribution(string Kind, string Owner, string Mod, string File, DataTable Document, int Values,
                                     IReadOnlyList<string> Unraised, IReadOnlyList<string> Requires)
{
    /// <summary>How the log, the notices and the report name it: <c>example.a data/a.toml</c>.</summary>
    public override string ToString() => $"{Mod} {File}";
}
