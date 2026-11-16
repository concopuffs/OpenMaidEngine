namespace Ome.Mods.Launch;

/// <summary>
/// The check a profile root passes before anything in it is opened (launcher spec §5.2, §12): no network path the
/// player of this machine has not approved, since opening a Windows network path contacts the host it names and
/// offers it the player's sign-in. A link inside the root is allowed (the user's decision, 2026-11-16, launcher plan
/// §9.7): a write through it is the risk any program that writes into a folder has.
/// </summary>
public static class RootSafety
{
    /// <summary>Approves the profile file's network paths for this one run.</summary>
    public const string AllowNetworkPathsOption = "--allow-network-paths";

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Whether <paramref name="path"/> is a Windows network path: <c>\\host\share</c> (or <c>//host/share</c>,
    /// or <c>\\?\UNC\host\share</c>). A share reached through a drive letter, and any path on Linux, is an ordinary
    /// path.</summary>
    public static bool IsNetworkPath(string path, bool windows)
    {
        if (!windows) return false;
        string text = path.Replace('/', '\\');
        if (text.StartsWith(@"\\?\", StringComparison.Ordinal) || text.StartsWith(@"\\.\", StringComparison.Ordinal))
            return text[4..].StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase);
        return text.StartsWith(@"\\", StringComparison.Ordinal);
    }

    public static bool IsNetworkPath(string path) => IsNetworkPath(path, OperatingSystem.IsWindows());

    /// <summary>The full path without a trailing separator, or null when it is not a usable path.</summary>
    public static string? Normalize(string path)
    {
        try
        {
            return path.Length == 0 ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    public static bool IsSameOrUnder(string path, string location)
    {
        string trimmed = Path.TrimEndingDirectorySeparator(location);
        return string.Equals(path, trimmed, PathComparison)
               || (path.Length > trimmed.Length && path.StartsWith(trimmed, PathComparison)
                   && (path[trimmed.Length] == Path.DirectorySeparatorChar
                       || path[trimmed.Length] == Path.AltDirectorySeparatorChar));
    }

    public static bool ContainsPath(IEnumerable<string> paths, string candidate)
        => paths.Any(path => string.Equals(path, candidate, PathComparison));

    /// <summary>Stops the run when <paramref name="file"/> names a network path this machine's player has not approved,
    /// unless the command line approves the file's paths for this run (<c>--allow-network-paths</c>).</summary>
    public static void RequireApproved(ProfileFile file, IReadOnlyList<string> commandLine, Func<MachineState> machine,
                                       bool windows)
    {
        if (commandLine.Contains(AllowNetworkPathsOption)) return;
        IReadOnlyList<(string Key, string Path)> unapproved;
        try
        {
            unapproved = UnapprovedNetworkPaths(file, machine, windows);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new RunResolutionException($"this machine's state cannot be read: {error.Message}", error);
        }
        if (unapproved.Count > 0)
            throw new RunResolutionException(
                $"{file.Path}: "
                + string.Join("; ", unapproved.Select(entry => $"{entry.Key} names the network path {entry.Path}"))
                + ", which this machine's player has not approved; approve the location in the launcher, or pass "
                + $"{AllowNetworkPathsOption} for this run");
    }

    /// <summary>The network paths <paramref name="file"/> names, each with its key, that this machine has not approved.
    /// A path inside the profile root is approved with it: the command line that named the root opened it already.
    /// The machine's state is read only when there is a path to check.</summary>
    public static IReadOnlyList<(string Key, string Path)> UnapprovedNetworkPaths(
        ProfileFile file, Func<MachineState> machine, bool windows)
    {
        var named = new List<(string Key, string Path)> { ("install.root", file.Install.Root) };
        named.AddRange(file.Install.Overlays.Select(path => ("install.overlays", path)));
        if (file.Mods != null) named.AddRange(file.Mods.Folders.Select(path => ("mods.folders", path)));
        var network = named.Where(entry => IsNetworkPath(entry.Path, windows)
                                           && !IsSameOrUnder(entry.Path, file.ProfileRoot)).ToList();
        if (network.Count == 0) return [];
        MachineState state = machine();
        return network.Where(entry => !state.IsApproved(entry.Path)).ToList();
    }
}
