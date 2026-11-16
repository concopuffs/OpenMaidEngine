using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Ome.Launcher.Spike;

/// <summary>
/// Stands for the launcher's start of the engine (spec §8.5): starts a program with its standard output and error
/// redirected, and writes each line to a log with the time since the start and the stream it came on.
/// <c>--capture --log FILE [--timeout SECONDS] [--cwd DIR] [--] PROGRAM ARG…</c>. On the timeout the program's
/// process tree is killed. Prints the exit code and the line counts.
/// </summary>
internal static class Capture
{
    public static int Run(string[] args)
    {
        string? log = null;
        string? cwd = null;
        double timeout = 60;
        int at = 0;
        while (at < args.Length)
        {
            switch (args[at])
            {
                case "--log":
                    log = args[at + 1];
                    at += 2;
                    continue;
                case "--timeout":
                    timeout = double.Parse(args[at + 1], CultureInfo.InvariantCulture);
                    at += 2;
                    continue;
                case "--cwd":
                    cwd = args[at + 1];
                    at += 2;
                    continue;
                case "--":
                    at++;
                    break;
            }
            break;
        }
        if (log is null || at >= args.Length)
        {
            Console.Error.WriteLine("usage: --capture --log FILE [--timeout SECONDS] [--cwd DIR] [--] PROGRAM ARG...");
            return 2;
        }

        var start = new ProcessStartInfo(args[at])
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = cwd ?? Path.GetDirectoryName(Path.GetFullPath(args[at])) ?? Environment.CurrentDirectory,
        };
        foreach (string arg in args[(at + 1)..])
        {
            start.ArgumentList.Add(arg);
        }

        using var writer = new StreamWriter(log, append: false, new UTF8Encoding(false)) { AutoFlush = true };
        var gate = new object();
        var clock = Stopwatch.StartNew();
        int outLines = 0;
        int errLines = 0;
        void Write(string stream, string? line)
        {
            if (line is null)
            {
                return;
            }
            lock (gate)
            {
                if (stream == "out") outLines++; else errLines++;
                writer.WriteLine($"[{clock.Elapsed.TotalMilliseconds,9:F0} ms] [{stream}] {line}");
            }
        }

        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => Write("out", e.Data);
        process.ErrorDataReceived += (_, e) => Write("err", e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        bool exited = process.WaitForExit(TimeSpan.FromSeconds(timeout));
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
        else
        {
            process.WaitForExit(); // drains the asynchronous readers
        }
        lock (gate)
        {
            writer.WriteLine($"[{clock.Elapsed.TotalMilliseconds,9:F0} ms] [end] exit {process.ExitCode}{(exited ? "" : " (killed at the timeout)")}");
        }
        Console.WriteLine($"exit={process.ExitCode} timed_out={!exited} stdout_lines={outLines} stderr_lines={errLines} log={log}");
        return 0;
    }
}
