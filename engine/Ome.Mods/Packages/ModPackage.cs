using System.Security.Cryptography;
using System.Text;
using Ome.Engine.Modding;
using Ome.Mods.Manifest;
using Ome.Script.Patch;

namespace Ome.Mods.Packages;

/// <summary>
/// An opened, validated mod (spec §3): its container, manifest, classified layout and package hash. Opening
/// checks everything that needs no game install; the linker checks the rest against the base.
/// </summary>
public sealed class ModPackage : IDisposable
{
    public const string PackageExtension = ".omp";

    public IPackageContainer Container { get; }
    public ModManifest Manifest { get; }
    public ModLayout Layout { get; }
    /// <summary><c>sha256:</c> over the sorted (path, content digest) list, so the <c>.omp</c> and folder forms of
    /// one tree hash the same.</summary>
    public string Hash { get; }

    public string Location => Container.Location;

    /// <summary>A folder, not a packed <c>.omp</c>: what <c>--lua-dev</c> applies to (Lua scripting spec §9.3).</summary>
    public bool Unpacked => Container is FolderPackageContainer;
    public string Id => Manifest.Id;

    /// <summary>A base mod's layout (Lua scripting spec §9.1), read and checked when the package opens; null for any
    /// other package.</summary>
    public GameLayout? BaseLayout { get; private init; }
    /// <summary>A base mod's copy of the game's patch points (<c>layout/points.json</c>, spec §9.1), so an exported
    /// build resolves them; null without one.</summary>
    public OmsPatchPoints? BasePoints { get; private init; }

    /// <summary>Whether the package only presents (localization spec §8, save stamps): it changes no script or plain
    /// asset, and carries text, fonts, language assets or a language. Adding or removing it is reported as presentation
    /// only.</summary>
    public bool PresentationOnly => Layout.ChangesNoContent
                                    && (Layout.Text.Count > 0 || Layout.Fonts.Count > 0 || Layout.LanguageAssets.Count > 0
                                        || Manifest.Languages.Count > 0);

    private ModPackage(IPackageContainer container, ModManifest manifest, ModLayout layout, string hash)
    {
        Container = container;
        Manifest = manifest;
        Layout = layout;
        Hash = hash;
    }

    /// <summary>Opens a <c>.omp</c> file or a folder holding <c>manifest.toml</c>.</summary>
    public static ModPackage Open(string path, PackageLimits? limits = null)
    {
        IPackageContainer container = OpenContainer(path, limits);
        try
        {
            return Load(container);
        }
        catch
        {
            container.Dispose();
            throw;
        }
    }

    /// <summary>A <c>.omp</c> file's or a package folder's listing, under the reader's path and size limits. Opening
    /// reads no file's content: a folder is walked, an <c>.omp</c>'s central directory read.</summary>
    internal static IPackageContainer OpenContainer(string path, PackageLimits? limits)
    {
        string full = Path.GetFullPath(path);
        return Directory.Exists(full) ? FolderPackageContainer.Open(full, limits)
            : File.Exists(full) && full.EndsWith(PackageExtension, StringComparison.OrdinalIgnoreCase)
                ? ZipPackageContainer.Open(full, limits)
                : throw new ModPackageException(full, $"not a {PackageExtension} file or a package folder");
    }

    /// <summary>The container's <c>manifest.toml</c>, parsed; a problem is the package's.</summary>
    internal static ModManifest ReadManifest(IPackageContainer container)
    {
        PackageEntry manifestEntry = container.Find(ManifestReader.FileName) is { } found
                                     && found.Path == ManifestReader.FileName
            ? found
            : throw new ModPackageException(container.Location, $"no {ManifestReader.FileName} at the package root");
        if (manifestEntry.Length > ManifestReader.MaxBytes)
            throw new ModPackageException(container.Location,
                $"{ManifestReader.FileName} is over the {ManifestReader.MaxBytes}-byte limit");
        try
        {
            return ManifestReader.Parse(ReadText(container, manifestEntry), ManifestReader.FileName);
        }
        catch (ModManifestException error)
        {
            throw new ModPackageException(container.Location,
                error.Problems.Select(problem => $"{ManifestReader.FileName}: {problem}").ToList());
        }
    }

    public static ModPackage Load(IPackageContainer container)
    {
        ModManifest manifest = ReadManifest(container);
        var problems = new List<string>();
        ModLayout layout = ModLayout.Classify(manifest, container.Entries, problems);
        if (problems.Count > 0) throw new ModPackageException(container.Location, problems);
        var (baseLayout, basePoints) = layout.BaseLayout is { } files ? ReadBaseLayout(container, manifest, files) : (null, null);
        return new ModPackage(container, manifest, layout, ComputeHash(container))
        {
            BaseLayout = baseLayout,
            BasePoints = basePoints,
        };
    }

    /// <summary>A base mod's layout and points (Lua scripting spec §9.1). The layout names the cells its dependents
    /// reach by name, which is raw access, so a base mod declares <c>vm</c>; and it names one game's, its own.</summary>
    private static (GameLayout, OmsPatchPoints?) ReadBaseLayout(IPackageContainer container, ModManifest manifest,
                                                               ModLayoutFiles files)
    {
        var problems = new List<string>();
        if (manifest.Lua?.HasVm != true)
            problems.Add("layout/: a base mod names cells for the mods that require it, which is raw access: it needs "
                         + "[lua] capabilities = [\"vm\"]");
        GameLayout? layout = null;
        try
        {
            layout = GameLayout.Parse(ReadText(container, files.Globals));
            if (layout.Profile != manifest.Profile)
                problems.Add($"{files.Globals.Path}: the layout is {layout.Profile}'s, and the package targets {manifest.Profile}");
        }
        catch (FormatException error)
        {
            problems.Add($"{files.Globals.Path}: {error.Message}");
        }
        OmsPatchPoints? points = null;
        if (files.Points is { } pointsFile)
            try
            {
                points = OmsPatchPoints.FromJson(ReadText(container, pointsFile));
            }
            catch (Exception error) when (error is System.Text.Json.JsonException or FormatException or InvalidDataException
                                              or InvalidOperationException or KeyNotFoundException)
            {
                problems.Add($"{pointsFile.Path}: {error.Message}");
            }
        if (problems.Count > 0) throw new ModPackageException(container.Location, problems);
        return (layout!, points);
    }

    public byte[] ReadAll(PackageEntry entry)
    {
        using Stream stream = Container.Open(entry);
        var bytes = new byte[checked((int)entry.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException($"{entry.Path}: payload is longer than listed");
        return bytes;
    }

    /// <summary>A text entry as UTF-8 (a byte-order mark is accepted); invalid UTF-8 is an error.</summary>
    public string ReadText(PackageEntry entry) => ReadText(Container, entry);

    private static string ReadText(IPackageContainer container, PackageEntry entry)
    {
        using Stream stream = container.Open(entry);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        try
        {
            string text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
        }
        catch (DecoderFallbackException)
        {
            throw new ModPackageException(container.Location, $"{entry.Path} is not UTF-8 text");
        }
    }

    public static string ComputeHash(IPackageContainer container)
    {
        using var outer = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (PackageEntry entry in container.Entries)
        {
            using Stream stream = container.Open(entry);
            byte[] digest = SHA256.HashData(stream);
            outer.AppendData(Encoding.UTF8.GetBytes(entry.Path));
            outer.AppendData([0]);
            outer.AppendData(digest);
        }
        return "sha256:" + Convert.ToHexString(outer.GetHashAndReset()).ToLowerInvariant();
    }

    public void Dispose() => Container.Dispose();
}
