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

    /// <summary>A version in a range comparator, where missing minor and patch numbers are zero (<c>&lt;2</c>);
    /// <paramref name="parts"/> is how many numbers were written (1-3).</summary>
    internal static bool TryParsePartial(string text, out SemanticVersion version, out int parts)
    {
        version = new SemanticVersion(0, 0, 0);
        parts = 0;
        Match match = Partial().Match(text);
        if (!match.Success || !TryNumbers(match, out int major, out int minor, out int patch)) return false;
        version = new SemanticVersion(major, minor, patch, match.Groups[4].Value);
        parts = match.Groups[3].Success ? 3 : match.Groups[2].Success ? 2 : 1;
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
/// <c>&lt;</c>, <c>=</c> or a bare version. Missing minor and patch numbers count as zero, except that a bare or
/// <c>=</c> version written with fewer than three numbers admits every release starting with them (<c>1.2</c>:
/// 1.2.x). <c>&lt;X</c> for a release X admits no prerelease of X (<c>&lt;2</c> rejects 2.0.0-rc.1). <c>*</c> or an
/// empty range admits every version.
/// </summary>
public sealed class VersionRange
{
    private readonly IReadOnlyList<(string Operator, SemanticVersion Version, int Parts)> _comparators;
    public string Text { get; }

    private VersionRange(string text, IReadOnlyList<(string, SemanticVersion, int)> comparators)
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
        var comparators = new List<(string, SemanticVersion, int)>();
        foreach (string part in trimmed.Split(','))
        {
            string item = part.Trim();
            string op = item.StartsWith(">=") ? ">=" : item.StartsWith("<=") ? "<=" : item.StartsWith('>') ? ">"
                : item.StartsWith('<') ? "<" : item.StartsWith('=') ? "=" : "";
            string versionText = item[op.Length..].Trim();
            if (!SemanticVersion.TryParsePartial(versionText, out SemanticVersion version, out int parts))
            {
                reason = $"'{item}' is not a version comparator";
                return false;
            }
            comparators.Add((op.Length == 0 ? "=" : op, version, parts));
        }
        range = new VersionRange(trimmed, comparators);
        return true;
    }

    public bool Contains(SemanticVersion version) => _comparators.All(comparator =>
    {
        SemanticVersion bound = comparator.Version;
        int order = version.CompareTo(bound);
        bool sameRelease = version.Major == bound.Major && version.Minor == bound.Minor && version.Patch == bound.Patch;
        return comparator.Operator switch
        {
            ">=" => order >= 0,
            ">" => order > 0,
            "<=" => order <= 0,
            // A prerelease of the release bound sorts below it but is that release, which the range excludes.
            "<" => order < 0 && !(sameRelease && bound.Prerelease.Length == 0 && version.Prerelease.Length > 0),
            _ when comparator.Parts < 3 && bound.Prerelease.Length == 0 =>
                version.Prerelease.Length == 0 && version.Major == bound.Major
                && (comparator.Parts < 2 || version.Minor == bound.Minor),
            _ => order == 0,
        };
    });

    public override string ToString() => Text;
}
