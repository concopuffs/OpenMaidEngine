using System.Diagnostics;
using Ome.Engine.Sys4;

/// <summary><c>Ome.Cli</c> as a child process, for the tests that need the real command line (the project reference
/// builds it without referencing its assembly).</summary>
internal static class OmeCliProcess
{
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    /// <summary>Ome.Cli's build output beside this assembly's: the same configuration and framework.</summary>
    public static string Assembly()
    {
        string relative = Path.GetRelativePath(Path.Combine(Paths.Repo, "engine", "Ome.Engine.Tests"), AppContext.BaseDirectory);
        string path = Path.GetFullPath(Path.Combine(Paths.Repo, "engine", "Ome.Cli", relative, "Ome.Cli.dll"));
        Assert.True(File.Exists(path), $"Ome.Cli is not built at {path}");
        return path;
    }

    public static ProcessStartInfo StartInfo(string? workingDirectory, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (workingDirectory != null) start.WorkingDirectory = workingDirectory;
        start.ArgumentList.Add(Assembly());
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    /// <summary>Runs a command to its end, with no standard input.</summary>
    public static (int Exit, string Output, string Error) Run(string workingDirectory, params string[] arguments)
    {
        using Process process = Process.Start(StartInfo(workingDirectory, arguments))!;
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(Patience))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"Ome.Cli {string.Join(' ', arguments)} did not finish");
        }
        return (process.ExitCode, output.Result, error.Result);
    }
}
