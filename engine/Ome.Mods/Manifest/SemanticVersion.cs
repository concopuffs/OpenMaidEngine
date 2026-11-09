using System.Globalization;
using System.Text.RegularExpressions;

namespace Ome.Mods.Manifest;

/// <summary>A semantic version (semver.org 2.0): <c>MAJOR.MINOR.PATCH[-prerelease][+build]</c>.</summary>
public sealed partial record SemanticVersion(int Major, int Minor, int Patch, string Prerelease = "", string Build = "")
    : IComparable<SemanticVersion>
{
    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$")]
    private static partial Regex Strict();

    [GeneratedRegex(@"^(0|[1-9]\d*)(?:\.(0|[1-9]\d*))?(?:\.(0|[1-9]\d*))?(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$")]
    private static partial Regex Partial();

    public static bool TryParse(string text, out SemanticVersion version)
    {
        version = new SemanticVersion(0, 0, 0);
        Match match = Strict().Match(text);
        if (!match.Success || !TryNumbers(match, out int major, out int minor, out int patch)) return false;
        version = new SemanticVersion(major, minor, patch, match.Groups[4].Value, match.Groups[5].Value);
        return true;
    }

    /// <summary>A version in a range comparator, where missing minor and patch numbers are zero (<c>&lt;2</c>).</summary>
    internal static bool TryParsePartial(string text, out SemanticVersion version)
    {
        version = new SemanticVersion(0, 0, 0);
        Match match = Partial().Match(text);
        if (!match.Success || !TryNumbers(match, out int major, out int minor, out int patch)) return false;
        version = new SemanticVersion(major, minor, patch, match.Groups[4].Value);
        return true;
    }

    private static bool TryNumbers(Match match, out int major, out int minor, out int patch)
    {
        minor = patch = 0;
        return int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out major)
               && (!match.Groups[2].Success
                   || int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out minor))
               && (!match.Groups[3].Success
                   || int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out patch));
    }

    /// <summary>Precedence by semver §11; build metadata does not take part.</summary>
    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;
        int order = Major.CompareTo(other.Major);
        if (order == 0) order = Minor.CompareTo(other.Minor);
        if (order == 0) order = Patch.CompareTo(other.Patch);
        if (order != 0) return order;
        // A release outranks any of its prereleases.
        if (Prerelease.Length == 0 && other.Prerelease.Length == 0) return 0;
        if (Prerelease.Length == 0) return 1;
        if (other.Prerelease.Length == 0) return -1;
        string[] mine = Prerelease.Split('.'), theirs = other.Prerelease.Split('.');
        for (int k = 0; k < Math.Min(mine.Length, theirs.Length); k++)
        {
            bool mineNumeric = long.TryParse(mine[k], NumberStyles.None, CultureInfo.InvariantCulture, out long a);
            bool theirsNumeric = long.TryParse(theirs[k], NumberStyles.None, CultureInfo.InvariantCulture, out long b);
            order = mineNumeric && theirsNumeric ? a.CompareTo(b)
                : mineNumeric ? -1 : theirsNumeric ? 1 : string.CompareOrdinal(mine[k], theirs[k]);
            if (order != 0) return Math.Sign(order);
        }
        return mine.Length.CompareTo(theirs.Length);
    }

    public override string ToString()
        => $"{Major}.{Minor}.{Patch}" + (Prerelease.Length > 0 ? "-" + Prerelease : "") + (Build.Length > 0 ? "+" + Build : "");
}

/// <summary>
/// A version range: comma-separated comparators that must all hold, each <c>&gt;=</c>, <c>&gt;</c>, <c>&lt;=</c>,
/// <c>&lt;</c>, <c>=</c> or a bare version (exact). <c>*</c> or an empty range admits every version.
/// </summary>
public sealed class VersionRange
{
    private readonly IReadOnlyList<(string Operator, SemanticVersion Version)> _comparators;
    public string Text { get; }

    private VersionRange(string text, IReadOnlyList<(string, SemanticVersion)> comparators)
    {
        Text = text;
        _comparators = comparators;
    }

    public static VersionRange Any { get; } = new("*", []);

    public static bool TryParse(string text, out VersionRange range, out string? reason)
    {
        range = Any;
        reason = null;
        string trimmed = text.Trim();
        if (trimmed is "" or "*") return true;
        var comparators = new List<(string, SemanticVersion)>();
        foreach (string part in trimmed.Split(','))
        {
            string item = part.Trim();
            string op = item.StartsWith(">=") ? ">=" : item.StartsWith("<=") ? "<=" : item.StartsWith('>') ? ">"
                : item.StartsWith('<') ? "<" : item.StartsWith('=') ? "=" : "";
            string versionText = item[op.Length..].Trim();
            if (!SemanticVersion.TryParsePartial(versionText, out SemanticVersion version))
            {
                reason = $"'{item}' is not a version comparator";
                return false;
            }
            comparators.Add((op.Length == 0 ? "=" : op, version));
        }
        range = new VersionRange(trimmed, comparators);
        return true;
    }

    public bool Contains(SemanticVersion version) => _comparators.All(comparator =>
    {
        int order = version.CompareTo(comparator.Version);
        return comparator.Operator switch
        {
            ">=" => order >= 0,
            ">" => order > 0,
            "<=" => order <= 0,
            "<" => order < 0,
            _ => order == 0,
        };
    });

    public override string ToString() => Text;
}
