namespace Ome.Launcher.Core;

/// <summary>Copying and removing folders of a profile root without following a link (launcher spec §12: a profile
/// root holds none, and a link there would send a write, or a delete, to another file).</summary>
internal static class Folders
{
    /// <summary>One level, hidden and system entries too, failing on an entry it cannot read.</summary>
    public static readonly EnumerationOptions Entries = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    private static OmePlatform Platform => LauncherEnvironment.Current.Platform;

    /// <summary>Copies <paramref name="from"/> into <paramref name="to"/>, keeping each file's time. A link stops the
    /// copy: it is never followed.</summary>
    /// <param name="leftOut">Entries not copied, by full path.</param>
    /// <param name="overwrite">Whether a file already in <paramref name="to"/> is replaced.</param>
    public static void Copy(string from, string to, Func<string, bool> leftOut, bool overwrite = false)
    {
        Directory.CreateDirectory(to);
        foreach (FileSystemInfo entry in new DirectoryInfo(from).EnumerateFileSystemInfos("*", Entries))
        {
            if (leftOut(entry.FullName)) continue;
            if (entry.LinkTarget != null) throw new IOException($"{entry.FullName} is a link, which is not copied");
            string target = Path.Combine(to, entry.Name);
            if (entry is DirectoryInfo folder) Copy(folder.FullName, target, leftOut, overwrite);
            else CopyFile(entry.FullName, target, overwrite);
        }
    }

    /// <summary>Copies one file, keeping its time.</summary>
    public static void CopyFile(string from, string to, bool overwrite)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(to))!);
        File.Copy(from, to, overwrite);
        File.SetLastWriteTimeUtc(to, File.GetLastWriteTimeUtc(from));
    }

    /// <summary>Removes everything in <paramref name="folder"/> but <paramref name="keep"/>, without following a link.</summary>
    public static void DeleteContents(string folder, string? keep)
    {
        foreach (FileSystemInfo entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", Entries).ToList())
        {
            if (keep != null && OmeHome.PathsEqual(entry.FullName, keep, Platform)) continue;
            Delete(entry);
        }
    }

    /// <summary>Removes a file, or a folder with what it holds; a link is removed itself.</summary>
    public static void Delete(FileSystemInfo entry)
    {
        if (entry.LinkTarget != null)
        {
            RemoveLink(entry);
            return;
        }
        if (entry is DirectoryInfo folder)
        {
            DeleteContents(folder.FullName, null);
            folder.Attributes &= ~FileAttributes.ReadOnly;
            folder.Delete();
        }
        else
        {
            entry.Attributes &= ~FileAttributes.ReadOnly;
            entry.Delete();
        }
    }

    /// <summary>Removes a link itself. A junction or a link to a folder is removed as a folder on Windows; elsewhere
    /// unlink removes any link.</summary>
    public static void RemoveLink(FileSystemInfo link)
    {
        if (OperatingSystem.IsWindows() && (link.Attributes & FileAttributes.Directory) != 0) Directory.Delete(link.FullName);
        else File.Delete(link.FullName);
    }
}
