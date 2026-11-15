using System.Text.Json;
using System.Xml.Linq;

namespace Ome.Scripting.Lua.Spike;

/// <summary>Inputs the cases read from the repository and the NuGet cache.</summary>
public static class SpikeLayout
{
    static string Repo()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "global.json")) && Directory.Exists(Path.Combine(dir.FullName, "engine")))
                return dir.FullName;
        throw new DirectoryNotFoundException("the repository root");
    }

    /// <summary>The named globals and tables of a profile's generated registry (build/games/&lt;profile&gt;/globals.json),
    /// standing in for a base mod's layout (spec §9.1): name to address.</summary>
    public static Dictionary<string, int> Load(string profile)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Repo(), "build", "games", profile, "globals.json")));
        var layout = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (JsonProperty global in document.RootElement.GetProperty("globals").EnumerateObject())
            if (global.Value.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String)
                layout.TryAdd(name.GetString()!, Convert.ToInt32(global.Value.GetProperty("address").GetString(), 16));
        if (document.RootElement.TryGetProperty("tables", out JsonElement tables))
        {
            IEnumerable<JsonElement> entries = tables.ValueKind == JsonValueKind.Object
                ? tables.EnumerateObject().Select(p => p.Value)
                : tables.EnumerateArray();
            foreach (JsonElement table in entries)
                if (table.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String
                    && table.TryGetProperty("base", out JsonElement at) && at.ValueKind == JsonValueKind.String)
                    layout.TryAdd(name.GetString()!, Convert.ToInt32(at.GetString(), 16));
        }
        return layout;
    }

    /// <summary>Criterion 9: what the package's nuspec says about its licence and its dependencies.</summary>
    public static CaseResult Licence(string library, string packageId, string version)
    {
        string root = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
                      ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        string nuspec = Path.Combine(root, packageId, version, packageId + ".nuspec");
        XDocument document = XDocument.Load(nuspec);
        XNamespace ns = document.Root!.GetDefaultNamespace();
        XElement metadata = document.Root!.Element(ns + "metadata")!;
        string? expression = metadata.Element(ns + "license")?.Value;
        string? url = metadata.Element(ns + "licenseUrl")?.Value;
        var groups = metadata.Element(ns + "dependencies")?.Elements(ns + "group")
            .Select(g => $"{g.Attribute("targetFramework")?.Value}: [{string.Join(", ", g.Elements(ns + "dependency").Select(d => d.Attribute("id")?.Value))}]")
            .ToArray() ?? [];
        string verdict = expression is "MIT" or "BSD-2-Clause" or "BSD-3-Clause" or "Apache-2.0" ? "pass" : "partial";
        return new CaseResult(library, "c09-licence", 9, verdict,
            $"{packageId} {version}: licence {(expression != null ? $"expression {expression}" : $"given only as a URL, {url}")}; dependencies {string.Join("; ", groups)}");
    }
}
