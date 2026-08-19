namespace Age.Engine.Sys4;

/// <summary>Explicit runtime selection of mod/translation asset overlays and opt-in payload codecs.</summary>
public sealed class AssetLaunchOptions
{
    public const string OverlayRootOptionName = "--overlay-root";
    public const string AllowBmpAsAgfOptionName = "--allow-bmp-as-agf";

    private readonly string[] _overlayRoots;
    private readonly string[] _looseRoots;

    public IReadOnlyList<string> OverlayRoots => _overlayRoots;
    public IReadOnlyList<string> LooseRoots => _looseRoots;
    public bool AllowBmpAsAgf { get; }

    private AssetLaunchOptions(string[] overlayRoots, string[] looseRoots, bool allowBmpAsAgf)
    {
        _overlayRoots = overlayRoots;
        _looseRoots = looseRoots;
        AllowBmpAsAgf = allowBmpAsAgf;
    }

    public static AssetLaunchOptions Resolve(IReadOnlyList<string> arguments, string gameRoot)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);

        string root = Path.GetFullPath(gameRoot);
        var overlays = new List<string>();
        bool allowBmpAsAgf = false;
        for (int index = 0; index < arguments.Count; index++)
        {
            if (arguments[index] == AllowBmpAsAgfOptionName)
            {
                allowBmpAsAgf = true;
                continue;
            }
            if (arguments[index] != OverlayRootOptionName) continue;
            if (index + 1 >= arguments.Count
                || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"{OverlayRootOptionName} requires a directory path");

            string requested = arguments[++index];
            string resolved = Path.GetFullPath(requested, root);
            if (!Directory.Exists(resolved))
                throw new ArgumentException(
                    $"{OverlayRootOptionName} '{requested}' is not a directory (resolved to '{resolved}')");
            if (!ContainsPath(overlays, resolved)) overlays.Add(resolved);
        }

        var looseRoots = new List<string>(overlays);
        if (!ContainsPath(looseRoots, root)) looseRoots.Add(root);
        return new AssetLaunchOptions(overlays.ToArray(), looseRoots.ToArray(), allowBmpAsAgf);
    }

    private static bool ContainsPath(IEnumerable<string> paths, string candidate)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string normalized = Path.TrimEndingDirectorySeparator(candidate);
        return paths.Any(path => string.Equals(
            Path.TrimEndingDirectorySeparator(path), normalized, comparison));
    }
}
