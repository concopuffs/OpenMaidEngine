namespace Ome.Launcher.Core;

/// <summary>
/// How the launcher writes a path into a file under OME home (launcher spec §4.3): relative when both the file's base
/// and the path are inside OME home, so a portable home keeps working on another drive letter, and absolute
/// otherwise. A relative path is written with <c>/</c>, which both systems read.
/// </summary>
public static class HomePaths
{
    /// <summary>The text to write for <paramref name="path"/> in a file whose paths are relative to
    /// <paramref name="basePath"/>.</summary>
    public static string ForFile(string path, string basePath, OmeHome home)
    {
        string full = Path.GetFullPath(path);
        string fullBase = Path.GetFullPath(basePath);
        if (!IsInside(full, home.Root) || !IsInside(fullBase, home.Root)) return full;
        return Path.GetRelativePath(fullBase, full).Replace('\\', '/');
    }

    /// <summary>A path read from such a file: an absolute one as it is, a relative one against
    /// <paramref name="basePath"/>.</summary>
    public static string Resolve(string written, string basePath)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(written, basePath));

    /// <summary>Whether <paramref name="path"/> is <paramref name="folder"/> or inside it, compared as this system
    /// compares paths.</summary>
    public static bool IsInside(string path, string folder)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        StringComparison comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return full.Equals(root, comparison)
               || full.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }
}
