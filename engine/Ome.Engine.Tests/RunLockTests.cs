using System.Diagnostics;
using Ome.Engine.Sys4;
using Ome.Mods.Launch;

/// <summary>Launcher spec §5.5 (gate 4): only a held run lock means "running". The second process is
/// <c>Ome.Cli run-lock</c>, which takes the lock as the engine's start does; a holder says it holds the lock on its
/// standard output, so no test sleeps or polls.</summary>
public sealed class RunLockTests : IDisposable
{
    private static readonly TimeSpan Patience = OmeCliProcess.Patience;
    private static readonly DateTime LongAgo = new(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-run-lock-tests", Guid.NewGuid().ToString("N"));
    private readonly List<Process> _helpers = [];

    public void Dispose()
    {
        foreach (Process helper in _helpers)
        {
            if (!helper.HasExited) helper.Kill(entireProcessTree: true);
            helper.WaitForExit();
            helper.Dispose();
        }
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string LockFile => Path.Combine(_root, RunLock.FileName);

    private Process Start(params string[] arguments)
    {
        Process process = Process.Start(OmeCliProcess.StartInfo(null, ["run-lock", .. arguments]))!;
        _helpers.Add(process);
        return process;
    }

    /// <summary>A holder: returns once it says it holds the lock.</summary>
    private Process Hold()
    {
        Process holder = Start(_root, "--hold");
        Task<string?> line = holder.StandardOutput.ReadLineAsync();
        Assert.True(line.Wait(Patience), "the holder never said it held the lock");
        Assert.Equal($"held {LockFile}", line.Result);
        return holder;
    }

    private (int Exit, string Error) RunToEnd(params string[] arguments)
    {
        Process process = Start(arguments);
        process.StandardInput.Close();
        Task<string> error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(Patience), "run-lock did not finish");
        return (process.ExitCode, error.Result);
    }

    private Dictionary<string, (long Length, DateTime Written)> Snapshot()
        => new DirectoryInfo(_root).EnumerateFiles("*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(_root, file.FullName), file => (file.Length, file.LastWriteTimeUtc));

    private void Profile()
    {
        Directory.CreateDirectory(Path.Combine(_root, "SAVE"));
        File.WriteAllText(Path.Combine(_root, "SAVE", "SAVE01.DAT"), "slot");
        File.WriteAllText(Path.Combine(_root, "OME.INI"), "[ome]\n");
        File.WriteAllText(LockFile, "");
        File.SetLastWriteTimeUtc(LockFile, LongAgo);
    }

    [Fact]
    public void ASecondStartOnARunningRootIsRefusedAndChangesNothing()
    {
        Profile();
        Process holder = Hold();
        var held = Snapshot();
        Assert.True(held[RunLock.FileName].Written > LongAgo, "taking the lock marks the start");

        (int exit, string error) = RunToEnd(_root);

        Assert.Equal(3, exit);
        Assert.Contains($"{_root} is in use", error);
        Assert.Equal(held, Snapshot());

        holder.StandardInput.Close();
        Assert.True(holder.WaitForExit(Patience));
        Assert.Equal(0, holder.ExitCode);
        Assert.Equal(0, RunToEnd(_root).Exit);
    }

    [Fact]
    public void AKilledHoldersFileIsNotRunning()
    {
        Profile();
        Process holder = Hold();
        holder.Kill(entireProcessTree: true);
        Assert.True(holder.WaitForExit(Patience));
        Assert.True(File.Exists(LockFile), "a crash leaves the file");
        File.SetLastWriteTimeUtc(LockFile, LongAgo);

        Assert.True(RunLock.TryAcquire(_root, markStart: true, out RunLock? taken, out RunLockRefusal? refusal),
                    refusal?.Message);
        using (taken)
            Assert.True(File.GetLastWriteTimeUtc(LockFile) > LongAgo, "the next start opens the file and marks it");
    }

    [Fact]
    public void TheLaunchersHoldLeavesTheStartTimeAlone()
    {
        Profile();
        Assert.True(RunLock.TryAcquire(_root, markStart: false, out RunLock? launcher, out _));
        using (launcher)
        {
            Assert.Equal(LongAgo, File.GetLastWriteTimeUtc(LockFile));
            (int exit, string error) = RunToEnd(_root);
            Assert.Equal(3, exit);
            Assert.Contains("is in use", error);
        }
        Assert.Equal(LongAgo, File.GetLastWriteTimeUtc(LockFile));
    }

    [Fact]
    public void TheLaunchersLockOnANewRootReadsAsNeverStartedUntilTheEngineMarksAStart()
    {
        string root = Path.Combine(_root, "made-by-the-launcher");
        Assert.True(RunLock.TryAcquire(root, markStart: false, out RunLock? launcher, out _));
        using (launcher)
        {
            Assert.Null(RunLock.LastStart(root));
        }
        Assert.Equal(RunLock.NeverStarted, File.GetLastWriteTimeUtc(Path.Combine(root, RunLock.FileName)));
        Assert.Null(RunLock.LastStart(root));

        DateTime before = DateTime.UtcNow.AddSeconds(-2);
        Assert.True(RunLock.TryAcquire(root, markStart: true, out RunLock? engine, out _));
        engine!.Dispose();
        Assert.True(RunLock.LastStart(root) > before, "the engine's start is the last played time");

        DateTime started = RunLock.LastStart(root)!.Value;
        Assert.True(RunLock.TryAcquire(root, markStart: false, out RunLock? again, out _));
        again!.Dispose();
        Assert.Equal(started, RunLock.LastStart(root));
    }

    [Fact]
    public void AStartOnANewRootCreatesItAndItsLock()
    {
        string root = Path.Combine(_root, "games", "himegari");
        Assert.True(RunLock.TryAcquire(root, markStart: true, out RunLock? taken, out _));
        using (taken)
            Assert.Equal(Path.Combine(root, RunLock.FileName), taken.Path);
        Assert.Equal([RunLock.FileName], Directory.EnumerateFileSystemEntries(root).Select(Path.GetFileName));
    }

    [Fact]
    public void ARootThatCannotHoldTheFileIsNotRunning()
    {
        Directory.CreateDirectory(_root);
        string notAFolder = Path.Combine(_root, "file");
        File.WriteAllText(notAFolder, "x");

        Assert.False(RunLock.TryAcquire(notAFolder, markStart: true, out _, out RunLockRefusal? refusal));
        Assert.False(refusal.HeldElsewhere);
        Assert.Contains("the run lock cannot be opened", refusal.Message);
        Assert.Equal(2, RunToEnd(notAFolder).Exit);
    }
}
