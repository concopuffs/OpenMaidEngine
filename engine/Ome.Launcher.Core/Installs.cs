using Ome.Engine.Frontend.Shell;
using Ome.Engine.Profiles;
using Ome.Engine.Sys4;
using Ome.Script.Patch;

namespace Ome.Launcher.Core;

/// <summary>What a folder is, as the launcher recognizes it (launcher spec §8.2).</summary>
public enum InstallKind
{
    /// <summary>A game this engine supports: its <c>SYS4INI.BIN</c> header matches a manifest.</summary>
    Supported,
    /// <summary>An AGE game this engine does not support: an <c>S4IC</c> catalog that matches no manifest.</summary>
    Unsupported,
    /// <summary>Not an AGE install.</summary>
    NotAnInstall,
}

/// <summary>
/// A recognized folder. For <see cref="InstallKind.Supported"/>: the game, its titles, the game version by the engine's
/// rule (<see cref="GameVersionRule"/>), the catalog fingerprint, and the catalog itself for the overlay candidates and
/// the save folder. For <see cref="InstallKind.Unsupported"/>: the catalog revision and title. <see cref="Problem"/>
/// says why a folder is not an install, or what is wrong with a supported one's catalog.
/// </summary>
public sealed record InstallRecognition(
    string Root,
    InstallKind Kind,
    string? Problem,
    GameCatalogIdentity? Identity = null,
    GameProfileManifest? Game = null,
    string? Fingerprint = null,
    string? GameVersion = null,
    Sys4AssetCatalog? Catalog = null)
{
    /// <summary>The game's titles: the manifest's, then the catalog's own when it differs.</summary>
    public IReadOnlyList<string> Titles => Game == null || Identity == null
        ? []
        : new[] { Game.DisplayTitle, Identity.Title }.Distinct(StringComparer.Ordinal).ToList();
}

/// <summary>A subfolder of an install offered as an overlay (spec §8.2): how many of its files carry names the catalog
/// lists, and whether one under an AGF name is a BMP, which needs <c>allow_bmp_as_agf</c>.</summary>
public sealed record OverlayCandidate(string Path, int Files, bool BmpAsAgf);

/// <summary>An install root the profiles use, with the ids of the profiles on it.</summary>
public sealed record KnownInstall(string Root, string Game, IReadOnlyList<string> Profiles);

/// <summary>Recognizing installs (launcher spec §8.2) by the engine's own <see cref="GameCatalogIdentity"/>, against the
/// built-in manifests.</summary>
public static class Installs
{
    public const string CatalogFile = "SYS4INI.BIN";
    private const string AgeCatalogMagic = "S4IC";

    private static readonly EnumerationOptions Entries = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    /// <param name="registry">The registry read of the game version rule; the machine's own when null.</param>
    public static InstallRecognition Recognize(string folder, GameProfileRegistry games,
                                               MachineRegistryReader? registry = null)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        string path = Path.Combine(root, CatalogFile);
        if (!File.Exists(path)) return new InstallRecognition(root, InstallKind.NotAnInstall, $"{root} holds no {CatalogFile}");
        GameCatalogIdentity identity;
        try
        {
            identity = GameCatalogIdentity.ReadSys4IniHeader(path);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException
                                          or UnauthorizedAccessException)
        {
            return new InstallRecognition(root, InstallKind.NotAnInstall, $"{path}: {error.Message}");
        }
        if (!identity.CatalogRevision.StartsWith(AgeCatalogMagic, StringComparison.Ordinal))
            return new InstallRecognition(root, InstallKind.NotAnInstall,
                                          $"{path} is not an AGE catalog (it starts with '{identity.CatalogRevision}')");
        GameProfileManifest? game = games.Profiles.FirstOrDefault(profile => profile.Matches(identity));
        if (game == null)
            return new InstallRecognition(root, InstallKind.Unsupported,
                                          $"{identity.Display} is an AGE game this engine does not support", identity);

        string? fingerprint = null;
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            fingerprint = OmsPatchPlan.FingerprintOf(bytes);
            Sys4AssetCatalog catalog = Sys4AssetCatalog.Load(path);
            return new InstallRecognition(root, InstallKind.Supported, null, identity, game, fingerprint,
                                          GameVersionRule.Of(catalog.StartupSettings, registry), catalog);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return new InstallRecognition(root, InstallKind.Supported, $"{path} cannot be read: {error.Message}",
                                          identity, game, fingerprint);
        }
    }

    /// <summary>The subfolders of <paramref name="installRoot"/> that hold loose files under names the catalog lists,
    /// with the count, by structure only (spec §8.2). Overlay files sit flat in the overlay root, as the engine opens
    /// them, so only each subfolder's own files count.</summary>
    public static IReadOnlyList<OverlayCandidate> OverlayCandidates(string installRoot, Sys4AssetCatalog catalog)
    {
        var candidates = new List<OverlayCandidate>();
        foreach (DirectoryInfo folder in new DirectoryInfo(installRoot).EnumerateDirectories("*", Entries)
                     .OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase))
        {
            int files = 0;
            bool bmp = false;
            foreach (FileInfo file in folder.EnumerateFiles("*", Entries))
            {
                if (catalog.ResolveMountedName(file.Name) == null) continue;
                files++;
                if (!bmp && file.Extension.Equals(".AGF", StringComparison.OrdinalIgnoreCase)) bmp = StartsWithBmp(file);
            }
            if (files > 0) candidates.Add(new OverlayCandidate(folder.FullName, files, bmp));
        }
        return candidates;
    }

    /// <summary>The install roots of the profiles (spec §8.2: there is no separate list), for each the game and the
    /// profiles on it, in the profiles' order.</summary>
    public static IReadOnlyList<KnownInstall> Known(IEnumerable<ProfileEntry> profiles)
    {
        var known = new List<KnownInstall>();
        foreach (ProfileEntry profile in profiles)
        {
            if (profile.Loaded?.Document is not { } document) continue;
            int at = known.FindIndex(install => install.Game == document.Game
                && OmeHome.PathsEqual(install.Root, document.Install.Root, LauncherEnvironment.Current.Platform));
            if (at < 0) known.Add(new KnownInstall(document.Install.Root, document.Game, [profile.Id]));
            else known[at] = known[at] with { Profiles = [.. known[at].Profiles, profile.Id] };
        }
        return known;
    }

    /// <summary>A BMP's header, which the engine's AGF decoder takes for a BMP (<c>AgfDecoder</c>): "BM".</summary>
    private static bool StartsWithBmp(FileInfo file)
    {
        try
        {
            using FileStream stream = file.OpenRead();
            Span<byte> header = stackalloc byte[2];
            return stream.ReadAtLeast(header, 2, throwOnEndOfStream: false) == 2 && header.SequenceEqual("BM"u8);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
