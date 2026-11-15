using System.Globalization;
using Ome.Engine.Modding;

namespace Ome.Engine.Natives;

/// <summary>A console chunk handed to the scripting native (spec §13): the driver polls <see cref="Done"/> once a frame
/// and then reads <see cref="Result"/>.</summary>
public sealed class ConsoleRequest(string? modId, string code)
{
    private volatile ConsoleResult? _result;

    /// <summary>The mod whose environment the chunk runs in, or null for the console's own.</summary>
    public string? ModId { get; } = modId;
    public string Code { get; } = code;
    public bool Done => _result != null;
    public ConsoleResult? Result => _result;

    internal void Complete(ConsoleResult result) => _result = result;
}

// The log, the notices and the console (spec §5.5, §13).
public sealed partial class Scripting
{
    public const string LogFile = "lua.log";
    /// <summary>The log lines a run keeps in memory, for its tests and for the driver.</summary>
    public const int RecentLogLines = 256;

    private readonly Queue<string> _recentLog = new();
    private readonly Queue<ConsoleRequest> _console = new();
    private int _noticeCount;
    private volatile string? _latestNotice;

    /// <summary>How many notices the native has published; the front end compares it each frame with the count it last
    /// showed, as it reads the page notices (spec §5.5).</summary>
    public int NoticeCount => Volatile.Read(ref _noticeCount);

    /// <summary>The latest notice's text.</summary>
    public string? LatestNotice => _latestNotice;

    /// <summary>The run's latest <c>lua.log</c> lines, oldest first.</summary>
    public IReadOnlyList<string> RecentLog => _recentLog.ToArray();

    private void Notify(string text)
    {
        _latestNotice = text;
        Interlocked.Increment(ref _noticeCount);
    }

    /// <summary>A line of <c>diagnostics/lua.log</c>: the time, the level, the mod (or none for the native's own
    /// line), and the message.</summary>
    internal void WriteLog(ModLogLevel level, string? modId, string message)
    {
        string line = string.Create(CultureInfo.InvariantCulture,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level.ToString().ToLowerInvariant()} {modId ?? "(engine)"}: {message}");
        _recentLog.Enqueue(line);
        while (_recentLog.Count > RecentLogLines) _recentLog.Dequeue();
        if (_setup.DiagnosticsDirectory is not { } directory) return;
        try
        {
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, LogFile), line + "\n");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Hand a console chunk over (spec §13). The front end calls it through <c>InputAndWaits.Invoke</c>; it only queues
    /// the chunk and returns, and the chunk runs the next time the native is serviced with no Lua on the stack.
    /// </summary>
    public ConsoleRequest SubmitConsole(string? modId, string code)
    {
        var request = new ConsoleRequest(modId, code);
        _console.Enqueue(request);
        return request;
    }

    /// <summary>Run the queued console chunks, when no Lua is on the stack. The interpreter calls it at each instruction
    /// boundary, and the waits native after it has run the front end's requests, so a chunk also runs while the game
    /// sits in a wait.</summary>
    public void ServiceRequests()
    {
        if (_console.Count == 0 || _depth > 0) return;
        while (_console.TryDequeue(out ConsoleRequest? request)) RunConsole(request);
    }

    private void RunConsole(ConsoleRequest request)
    {
        if (request.ModId != null && _setup.Stack.Mods.All(mod => mod.Id != request.ModId))
        {
            request.Complete(new ConsoleResult(false, $"{request.ModId} is not a mod of the stack that ships Lua"));
            return;
        }
        if (_scene.Session == null)
        {
            if (_setup.Stack.HasLua)
            {
                request.Complete(new ConsoleResult(false, "no session is running yet"));
                return;
            }
            // Spec §13: with no mod that ships Lua, the console's first use creates the runtime and a session for the
            // console alone. That build runs no mod code, so it may happen here, inside a wait too.
            _scene.Session = Runtime().BuildSession(ModRuntimeStack.Empty, _access, ModState.Empty,
                                                    SessionReason.Boot, _setup.Limits);
            _scene.ConsoleOnly = true;
            _scene.Reason = SessionReason.Boot;
        }
        bool outerFrameAccess = _frameAccess;
        _depth++;
        _consoleDepth++;   // spec §7.3: the chunk's writes go with the next instruction's records
        _frameAccess = true;
        try
        {
            request.Complete(_scene.Session.RunChunk(request.ModId, request.Code));
        }
        catch (ModScriptException error)
        {
            request.Complete(new ConsoleResult(false, error.Message));
        }
        finally
        {
            _frameAccess = outerFrameAccess;
            _consoleDepth--;
            _depth--;
        }
    }
}
