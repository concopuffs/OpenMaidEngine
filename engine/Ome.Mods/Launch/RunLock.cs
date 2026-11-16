using System.Diagnostics.CodeAnalysis;

namespace Ome.Mods.Launch;

/// <summary>Why a run lock was not taken. <see cref="HeldElsewhere"/>: another process holds it, so the profile is
/// running or the launcher is writing it; otherwise the root cannot hold the file.</summary>
public sealed record RunLockRefusal(bool HeldElsewhere, string Message);

/// <summary>
/// A data root's run lock (launcher spec §5.5): <c>&lt;data root&gt;/run.lock</c> opened for exclusive use and held
/// until the holder disposes it or exits. Only a held lock means "running": the file's presence means nothing and its
/// content is never read, so a crash leaves a file the next start opens normally.
/// <list type="bullet">
/// <item>The engine takes it before it writes any other file in the root, and marks the start ("last played" is the
/// file's last-write time, set through the held handle).</item>
/// <item>The launcher takes the same lock without marking a start, while it writes a profile's files, so a shortcut
/// cannot start the engine in the middle of a write.</item>
/// </list>
/// On Windows the open is a sharing violation for any other open; on Linux and macOS .NET emulates it with an advisory
/// <c>flock</c>, which every .NET open honours (launcher plan §3.8).
/// </summary>
public sealed class RunLock : IDisposable
{
    public const string FileName = "run.lock";

    private readonly FileStream _stream;

    /// <summary>The lock file's full path.</summary>
    public string Path { get; }

    /// <summary>Why the start could not be marked, when it could not (the lock is held all the same).</summary>
    public string? Warning { get; }

    private RunLock(string path, FileStream stream, string? warning)
    {
        Path = path;
        _stream = stream;
        Warning = warning;
    }

    /// <summary>Takes <paramref name="dataRoot"/>'s lock, creating the root when it does not exist.</summary>
    /// <param name="markStart">Sets the file's last-write time once the lock is held: the engine's start, which a
    /// refused start never reaches. The launcher's own writes leave it alone.</param>
    /// <param name="refusal">Why the lock was not taken: another holder, or a root that cannot hold the file.</param>
    public static bool TryAcquire(string dataRoot, bool markStart,
                                  [NotNullWhen(true)] out RunLock? held, [NotNullWhen(false)] out RunLockRefusal? refusal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        string root = System.IO.Path.GetFullPath(dataRoot);
        string path = System.IO.Path.Combine(root, FileName);
        FileStream stream;
        try
        {
            Directory.CreateDirectory(root);
            stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException error) when (IsHeldElsewhere(error))
        {
            held = null;
            refusal = new RunLockRefusal(true,
                $"{root} is in use: another engine is running this profile, or the launcher is changing its settings "
                + $"({path} is held); close it and start again");
            return false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            held = null;
            refusal = new RunLockRefusal(false, $"{path}: the run lock cannot be opened: {error.Message}");
            return false;
        }

        string? warning = null;
        if (markStart)
        {
            try
            {
                File.SetLastWriteTimeUtc(stream.SafeFileHandle, DateTime.UtcNow);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                warning = $"{path}: the start time could not be set: {error.Message}";
            }
        }
        held = new RunLock(path, stream, warning);
        refusal = null;
        return true;
    }

    /// <summary>A sharing or lock violation on Windows; <c>EWOULDBLOCK</c> from the <c>flock</c> elsewhere (11 on Linux,
    /// 35 on macOS). Any other failure is a root that cannot hold the file, not a holder.</summary>
    private static bool IsHeldElsewhere(IOException error)
        => OperatingSystem.IsWindows()
            ? error.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021)
            : error.HResult is 11 or 35;

    public void Dispose() => _stream.Dispose();
}
