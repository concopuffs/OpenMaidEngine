// The engine's stand-in for the launcher's tests (launcher plan §5.11): it prints its working folder, its PWD and each
// argument, on standard output and on standard error, in UTF-8 as the engine writes. --delete-file PATH deletes a file
// before it exits, so a test can change what the checks find after a start; --exit-code N is the code it exits with.

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.Out.WriteLine($"stub cwd: {Directory.GetCurrentDirectory()}");
Console.Out.WriteLine($"stub PWD: {Environment.GetEnvironmentVariable("PWD") ?? "<unset>"}");
foreach (string argument in args)
{
    Console.Out.WriteLine($"stub out: {argument}");
    Console.Error.WriteLine($"stub err: {argument}");
}

string? Value(string option)
{
    int index = Array.IndexOf(args, option);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

if (Value("--delete-file") is { } doomed) File.Delete(doomed);
return int.TryParse(Value("--exit-code"), out int code) ? code : 0;
