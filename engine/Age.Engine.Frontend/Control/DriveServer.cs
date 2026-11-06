using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Age.Engine.Frontend.Control;

/// <summary>The driving protocol's server side: JSON lines over loopback TCP, one client at a time, a token on the
/// first request. Socket I/O runs on a background thread; requests run on the caller's thread in
/// <see cref="Pump"/>, which the frontend calls once per frame.</summary>
public sealed class DriveServer : IDisposable
{
    private sealed record Call(DriveRequest Request, TaskCompletionSource<string> Reply);

    private readonly IDriveBackend _backend;
    private readonly DriveDispatcher _dispatcher;
    private readonly byte[] _token;
    private readonly TcpListener _listener;
    private readonly ConcurrentQueue<Call> _calls = new();
    private readonly CancellationTokenSource _stopping = new();
    private Thread? _thread;
    private volatile TcpClient? _client;
    private volatile bool _connected;

    public DriveServer(IDriveBackend backend, string token, int port = 0)
    {
        if (string.IsNullOrEmpty(token)) throw new ArgumentException("a token is required", nameof(token));
        _backend = backend;
        _dispatcher = new DriveDispatcher(backend);
        _token = Encoding.UTF8.GetBytes(token);
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public int Port { get; }
    public bool ClientConnected => _connected;
    public bool CloseRequested => _dispatcher.CloseRequested;

    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public void Start()
    {
        if (_thread != null) return;
        _thread = new Thread(Serve) { IsBackground = true, Name = "drive-server" };
        _thread.Start();
    }

    /// <summary>Start the next queued request and advance the running one. Call from the frontend's main
    /// thread.</summary>
    public void Pump(long nowMs)
    {
        if (!_dispatcher.Busy && _calls.TryDequeue(out Call? call))
            _dispatcher.Start(call.Request, nowMs, line => call.Reply.TrySetResult(line));
        _dispatcher.Poll(nowMs);
    }

    /// <summary>Whether the VM's assembly was compiled with the JIT optimizer on. Godot's editor binary loads only the
    /// Debug configuration, where the optimizer is off unless the build passed Optimize=true
    /// (<c>drive.py port start --optimize</c>). CPU-bound VM work runs two to three times slower without it, so
    /// timings compared with native need to know.</summary>
    public static bool JitOptimized { get; } =
        typeof(Age.Engine.Vm.VirtualMachine).Assembly.GetCustomAttribute<DebuggableAttribute>()
            is not { IsJITOptimizerDisabled: true };

    public JsonObject Hello() => new()
    {
        ["target"] = _backend.Target,
        ["profile"] = _backend.Profile,
        ["version"] = DriveProtocol.Version,
        ["capabilities"] = new JsonArray(_backend.Capabilities.Select(c => (JsonNode?)c).ToArray()),
        ["jit_optimized"] = JitOptimized,
    };

    private void Serve()
    {
        while (!_stopping.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = _listener.AcceptTcpClient();
            }
            catch (Exception) when (_stopping.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }
            _client = client;
            try
            {
                Converse(client);
            }
            catch (Exception) when (_stopping.IsCancellationRequested)
            {
                return;
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            finally
            {
                _connected = false;
                _client = null;
                client.Dispose();
            }
        }
    }

    private void Converse(TcpClient client)
    {
        client.NoDelay = true;
        using NetworkStream stream = client.GetStream();
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };

        string? first = reader.ReadLine();
        if (first == null) return;
        DriveRequest hello;
        try
        {
            hello = DriveRequest.Parse(first);
        }
        catch (DriveProtocolException error)
        {
            writer.WriteLine(DriveReply.Error(0, error.Message));
            return;
        }
        if (hello.Op != "hello" || !TokenMatches(hello))
        {
            writer.WriteLine(DriveReply.Error(hello.Id, "the first request must be hello with the session token"));
            return;
        }
        _connected = true;
        writer.WriteLine(DriveReply.Ok(hello.Id, Hello()));

        while (!_stopping.IsCancellationRequested)
        {
            string? line = reader.ReadLine();
            if (line == null) return;
            if (line.Trim().Length == 0) continue;
            DriveRequest request;
            try
            {
                request = DriveRequest.Parse(line);
            }
            catch (DriveProtocolException error)
            {
                writer.WriteLine(DriveReply.Error(0, error.Message));
                continue;
            }
            if (request.Op == "hello")
            {
                writer.WriteLine(DriveReply.Ok(request.Id, Hello()));
                continue;
            }
            var call = new Call(request, new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously));
            _calls.Enqueue(call);
            string reply;
            try
            {
                reply = call.Reply.Task.WaitAsync(_stopping.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            writer.WriteLine(reply);
        }
    }

    private bool TokenMatches(DriveRequest hello)
    {
        string supplied;
        try
        {
            supplied = hello.Text("token");
        }
        catch (DriveProtocolException)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), _token);
    }

    public void Dispose()
    {
        if (_stopping.IsCancellationRequested) return;
        _stopping.Cancel();
        try { _listener.Stop(); } catch (SocketException) { }
        try { _client?.Dispose(); } catch (ObjectDisposedException) { }
        _thread?.Join(TimeSpan.FromSeconds(2));
    }

    /// <summary>Write the session file a launcher waits for (§4 "Session file"): written whole, then renamed into
    /// place, so a reader never sees half a file.</summary>
    public void WriteSessionFile(string path, int pid, string token, string? log = null)
    {
        JsonObject session = Hello();
        session["pid"] = pid;
        session["port"] = Port;
        session["token"] = token;
        session["started"] = DateTimeOffset.Now.ToString("O");
        if (log != null) session["log"] = log;
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temporary = full + ".tmp";
        File.WriteAllText(temporary, session.ToJsonString(), new UTF8Encoding(false));
        File.Move(temporary, full, overwrite: true);
    }
}
