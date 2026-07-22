using System.Text.Json;
using Age.Engine.Sys4;

static string? Option(string[] arguments, string name)
{
    int index = Array.IndexOf(arguments, name);
    return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
}

if (args.Contains("--help"))
{
    Console.WriteLine("usage: dotnet run --project tools/movie-corpus-gate -- [--output <json>] [--native-dir <dir>] [--expected-count 213] [--max-item-ms 30000]");
    return 0;
}

string output = Path.GetFullPath(Option(args, "--output")
                                 ?? Path.Combine(Paths.Build, "movie-corpus-ffmpeg.json"));
string nativeDirectory = Path.GetFullPath(Option(args, "--native-dir")
                                          ?? Path.Combine(Paths.Build, "native", "win-x64"));
int expectedCount = int.Parse(Option(args, "--expected-count") ?? "213");
long maximumItemMilliseconds = long.Parse(Option(args, "--max-item-ms") ?? "30000");
string nativeLibrary = Path.Combine(nativeDirectory, OperatingSystem.IsWindows()
    ? "age_movie_ffmpeg.dll" : OperatingSystem.IsMacOS()
        ? "libage_movie_ffmpeg.dylib" : "libage_movie_ffmpeg.so");
if (!File.Exists(nativeLibrary))
{
    Console.Error.WriteLine($"native movie shim not found: {nativeLibrary}");
    return 2;
}
Environment.SetEnvironmentVariable("AGE_FFMPEG_NATIVE_DIR", nativeDirectory);

var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
var store = new Sys4AssetStore(catalog, Paths.GameDir, Paths.GameDir);
var resources = new ResourceMap(catalog, store);
Console.WriteLine("discovering MPEG program streams from .AGF catalog entries...");
var discovered = MovieCorpusDiscovery.DiscoverMpegMovies(catalog, store);
var inputs = discovered.Select(packed => new MovieCorpusInput(
    packed.PackedId, packed.Asset.Name, () => resources.ReadMovie(packed.Asset))).ToArray();
Console.WriteLine($"discovered {inputs.Length} movies; decoding every frame without presentation waits");

var gate = new MovieCorpusGate(payload => new FfmpegMovieSession(payload));
MovieCorpusReport report = gate.Run(inputs, expectedCount, maximumItemMilliseconds,
    (index, count, item) => Console.WriteLine(
        $"[{index,3}/{count}] {(item.Passed ? "PASS" : "FAIL")} {item.PackedIdHex,-10} {item.Name,-16} " +
        $"{item.Width}x{item.Height} {item.FrameCount}f {item.StopTimeMs}ms decode={item.DecodeMilliseconds}ms" +
        (item.Error == null ? "" : $" :: {item.Error}")));

string? parent = Path.GetDirectoryName(output);
if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
};
File.WriteAllText(output, JsonSerializer.Serialize(report, jsonOptions));
Console.WriteLine($"summary: {report.PassedCount}/{report.CandidateCount} passed, " +
                  $"{report.FailedCount} failed in {report.ElapsedMilliseconds} ms");
foreach (string error in report.SelectionErrors) Console.Error.WriteLine("selection: " + error);
Console.WriteLine("report: " + output);
return report.Passed ? 0 : 1;
