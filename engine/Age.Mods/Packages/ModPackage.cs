using System.Security.Cryptography;
using System.Text;
using Age.Mods.Manifest;

namespace Age.Mods.Packages;

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
    public string Id => Manifest.Id;

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
        string full = Path.GetFullPath(path);
        IPackageContainer container = Directory.Exists(full) ? FolderPackageContainer.Open(full, limits)
            : File.Exists(full) && full.EndsWith(PackageExtension, StringComparison.OrdinalIgnoreCase)
                ? ZipPackageContainer.Open(full, limits)
                : throw new ModPackageException(full, $"not a {PackageExtension} file or a package folder");
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

    public static ModPackage Load(IPackageContainer container)
    {
        PackageEntry manifestEntry = container.Find(ManifestReader.FileName) is { } found
                                     && found.Path == ManifestReader.FileName
            ? found
            : throw new ModPackageException(container.Location, $"no {ManifestReader.FileName} at the package root");
        if (manifestEntry.Length > ManifestReader.MaxBytes)
            throw new ModPackageException(container.Location,
                $"{ManifestReader.FileName} is over the {ManifestReader.MaxBytes}-byte limit");
        ModManifest manifest;
        try
        {
            manifest = ManifestReader.Parse(ReadText(container, manifestEntry), ManifestReader.FileName);
        }
        catch (ModManifestException error)
        {
            throw new ModPackageException(container.Location,
                error.Problems.Select(problem => $"{ManifestReader.FileName}: {problem}").ToList());
        }
        var problems = new List<string>();
        ModLayout layout = ModLayout.Classify(manifest, container.Entries, problems);
        if (problems.Count > 0) throw new ModPackageException(container.Location, problems);
        return new ModPackage(container, manifest, layout, ComputeHash(container));
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
