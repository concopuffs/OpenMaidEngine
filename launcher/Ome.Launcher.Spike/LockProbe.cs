using System.Globalization;

namespace Ome.Launcher.Spike;

/// <summary>
/// Stands for the run lock (spec §5.5): <c>--lock PATH [--hold-until-killed] [--ready FILE]</c> opens PATH for
/// exclusive use, sets its last-write time through the held handle, and either holds it until killed (writing the
/// readiness file once it holds it) or reports and exits. Exit 0: acquired; 3: refused.
/// </summary>
internal static class LockProbe
{
    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: --lock PATH [--hold-until-killed] [--ready FILE]");
            return 2;
        }
        string path = args[0];
        bool hold = args.Contains("--hold-until-killed");
        string? ready = Program.OptionValue(args, "--ready");
        string before = File.Exists(path) ? Stamp(File.GetLastWriteTimeUtc(path)) : "(no file)";

        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException e)
        {
            Console.WriteLine($"refused: {e.GetType().Name} HResult=0x{e.HResult:x8} {e.Message}");
            Console.WriteLine($"write time unchanged: {before} -> {Stamp(File.GetLastWriteTimeUtc(path))}");
            return 3;
        }

        using (stream)
        {
            DateTime now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(stream.SafeFileHandle, now);
            Console.WriteLine($"acquired: pid {Environment.ProcessId}; write time {before} -> {Stamp(File.GetLastWriteTimeUtc(path))} (set {Stamp(now)})");
            Console.Out.Flush();
            if (ready is not null)
            {
                File.WriteAllText(ready, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            }
            if (hold)
            {
                Thread.Sleep(Timeout.Infinite);
            }
        }
        return 0;
    }

    private static string Stamp(DateTime utc) => utc.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
}
