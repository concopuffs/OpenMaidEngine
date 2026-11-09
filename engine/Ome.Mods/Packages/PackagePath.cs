namespace Ome.Mods.Packages;

/// <summary>
/// Entry paths inside a package (mod loader spec §11). A path is relative, uses <c>/</c>, and names nothing a
/// Windows or Unix file system would read differently: no root, drive, <c>.</c> or <c>..</c> segment, colon,
/// control character, trailing dot or space, or reserved device name.
/// </summary>
public static class PackagePath
{
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Normalizes <paramref name="raw"/> (backslashes become <c>/</c>) or explains why it is unsafe.</summary>
    public static bool TryNormalize(string raw, out string normalized, out string? reason)
    {
        normalized = raw.Replace('\\', '/');
        reason = Check(normalized);
        return reason == null;
    }

    private static string? Check(string path)
    {
        if (path.Length == 0) return "empty path";
        if (path[0] == '/') return "absolute path";
        if (path.Length >= 2 && path[1] == ':' && char.IsAsciiLetter(path[0])) return "drive-letter path";
        foreach (char c in path)
        {
            if (char.IsControl(c)) return "control character in path";
            if (c == ':') return "colon in path";
        }
        foreach (string segment in path.Split('/'))
        {
            if (segment.Length == 0) return "empty path segment";
            if (segment is "." or "..") return $"'{segment}' path segment";
            if (segment.EndsWith('.') || segment.EndsWith(' ')) return "path segment ends with a dot or space";
            string stem = segment.Split('.')[0].TrimEnd(' ');
            if (ReservedDeviceNames.Contains(stem)) return $"reserved device name '{segment}'";
        }
        return null;
    }
}
