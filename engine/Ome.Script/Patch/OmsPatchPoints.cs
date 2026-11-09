using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ome.Script.Patch;

/// <summary>Where a named patch point sits in one revision of its script: that revision's fingerprint and the
/// original dword offset of the instruction the point names.</summary>
public sealed record OmsPatchPointSite(
    [property: JsonPropertyName("fingerprint")] string Fingerprint,
    [property: JsonPropertyName("site")] int Site);

/// <summary>A named patch point (mod loader spec §5): a stable site name with one site per supported revision.</summary>
public sealed record OmsPatchPoint(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("script")] string Script,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("at")] IReadOnlyList<OmsPatchPointSite> At);

/// <summary>
/// A profile's named patch points, as <c>tools/patch_points_build.py --build</c> generates them from
/// <c>vm-map/patch-points-&lt;profile&gt;.toml</c> into <c>build/games/&lt;profile&gt;/patch-points.json</c>.
/// </summary>
public sealed class OmsPatchPoints
{
    private readonly Dictionary<string, OmsPatchPoint> _points;

    public IReadOnlyCollection<OmsPatchPoint> Points => _points.Values;

    public OmsPatchPoints(IEnumerable<OmsPatchPoint> points)
    {
        _points = new Dictionary<string, OmsPatchPoint>(StringComparer.Ordinal);
        foreach (OmsPatchPoint point in points)
            if (!_points.TryAdd(point.Name, point))
                throw new InvalidDataException($"patch point {point.Name} is defined twice");
    }

    private sealed record Document(
        [property: JsonPropertyName("profile")] string Profile,
        [property: JsonPropertyName("points")] IReadOnlyList<OmsPatchPoint> Points);

    public static OmsPatchPoints FromJson(string json)
    {
        Document document = JsonSerializer.Deserialize<Document>(json)
                            ?? throw new InvalidDataException("empty patch-point registry");
        return new OmsPatchPoints(document.Points ?? []);
    }

    public static OmsPatchPoints Load(string path) => FromJson(File.ReadAllText(path));

    public bool TryGet(string name, out OmsPatchPoint point) => _points.TryGetValue(name, out point!);

    /// <summary>The original offset <paramref name="name"/> names in the revision <paramref name="fingerprint"/> of
    /// <paramref name="script"/>, or null with the reason: an unknown point, another script's point, or a revision
    /// the point does not list (the reason lists the ones it does).</summary>
    public int? Resolve(string name, string script, string fingerprint, out string? reason)
    {
        reason = null;
        if (!_points.TryGetValue(name, out OmsPatchPoint? point))
        {
            reason = $"'{name}' is not a named patch point of this profile";
            return null;
        }
        if (!string.Equals(point.Script, script, StringComparison.OrdinalIgnoreCase))
        {
            reason = $"patch point {name} is in {point.Script}, not {script}";
            return null;
        }
        foreach (OmsPatchPointSite site in point.At)
            if (string.Equals(site.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
                return site.Site;
        reason = $"patch point {name} has no site for {script} {fingerprint}; it supports "
                 + string.Join(", ", point.At.Select(site => site.Fingerprint));
        return null;
    }
}
