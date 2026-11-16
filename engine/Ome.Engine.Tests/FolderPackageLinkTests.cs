using System.Diagnostics;
using Ome.Mods.Packages;

/// <summary>Links and unpacked package folders (mod loader spec §3.1, §11). A link inside a package folder is refused, so
/// the package cannot reach outside its folder; the package folder itself may be a link, such as a profile's
/// <c>mods/</c> entry pointing at a mod's working copy, which a profile root may hold (the user's decision of
/// 2026-11-16, launcher plan §9.7). Links are junctions on Windows (no privilege needed) and symbolic links
/// elsewhere.</summary>
public sealed class FolderPackageLinkTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-folder-package-link-tests",
                                                 Guid.NewGuid().ToString("N"));

    public FolderPackageLinkTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        // Remove links first, so the delete never walks into a target.
        foreach (string entry in Directory.EnumerateFileSystemEntries(_root, "*", new EnumerationOptions
                 { RecurseSubdirectories = true, AttributesToSkip = 0 }).OrderByDescending(path => path.Length))
            if (new FileInfo(entry).LinkTarget != null) Directory.Delete(entry);
        Directory.Delete(_root, recursive: true);
    }

    private string Package(string parent, string id)
    {
        string folder = Path.Combine(parent, id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "manifest.toml"),
            $"omp = 1\nid = \"{id}\"\nname = \"{id}\"\nversion = \"1.0.0\"\n[game]\nprofile = \"himegari\"\ntarget = \"native\"\n");
        return folder;
    }

    [Fact]
    public void APackageFolderThatIsALinkOpens()
    {
        string working = Package(Path.Combine(_root, "work"), "example.working-copy");
        string mods = Path.Combine(_root, "profile", "mods");
        Directory.CreateDirectory(mods);
        string linked = Path.Combine(mods, "example.working-copy");
        DirectoryLink(linked, working);

        ModPackage package = ModPackage.Open(linked);

        Assert.Equal("example.working-copy", package.Manifest.Id);
        Assert.True(package.Unpacked);
    }

    [Fact]
    public void ALinkInsideAPackageFolderIsRefused()
    {
        string package = Package(_root, "example.reaching");
        string outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        DirectoryLink(Path.Combine(package, "assets"), outside);

        var error = Assert.Throws<ModPackageException>(() => ModPackage.Open(package));

        Assert.Contains("'assets' is a link or junction", error.Message);
    }

    private static void DirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        var start = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        using Process process = Process.Start(start)!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }
}
