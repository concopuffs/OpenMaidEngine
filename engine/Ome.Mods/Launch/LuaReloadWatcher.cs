using Ome.Engine.Modding;
using Ome.Mods.Linking;

namespace Ome.Mods.Launch;

/// <summary>
/// <c>--lua-reload</c> (docs/superpowers/specs/2026-11-10-lua-scripting-design.md §13): watches the <c>lua/</c> folder of
/// each unpacked mod of the stack that ships Lua. Once a mod's <c>.lua</c> files have stopped changing for
/// <see cref="DefaultSettle"/>, it reads and checks them on a pool thread (<see cref="ModLuaReload"/>) and hands the
/// result to <c>deliver</c>, which gives it to the scripting native through the waits native's requests. One delivery
/// runs at a time, in the order the changes settled. A change to the manifest or to <c>events/</c> is not watched: it
/// needs a restart.
/// </summary>
public sealed class LuaReloadWatcher : IDisposable
{
    public static readonly TimeSpan DefaultSettle = TimeSpan.FromMilliseconds(300);

    private readonly ModRuntimeStack _stack;
    private readonly IModRuntime _runtime;
    private readonly Action<string, ModLuaReloadCheck> _deliver;
    private readonly TimeSpan _settle;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly List<Timer> _timers = new();
    private readonly object _gate = new(), _delivering = new();
    private bool _disposed;

    /// <summary>The watched mods by id, each with its <c>lua/</c> folder.</summary>
    public IReadOnlyDictionary<string, string> Watched { get; }

    /// <param name="folders">Each unpacked mod by id, with its folder (<see cref="ModLaunchResult.UnpackedLuaFolders"/>);
    /// a mod not in <paramref name="stack"/>, or with no <c>lua/</c> folder, is not watched.</param>
    /// <param name="deliver">Called on a pool thread with the mod's id and what its files gave.</param>
    public LuaReloadWatcher(IReadOnlyDictionary<string, string> folders, ModRuntimeStack stack, IModRuntime runtime,
                            Action<string, ModLuaReloadCheck> deliver, TimeSpan? settle = null)
    {
        _stack = stack;
        _runtime = runtime;
        _deliver = deliver;
        _settle = settle ?? DefaultSettle;
        var watched = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (ModRuntimeMod mod in stack.Mods)
        {
            if (!folders.TryGetValue(mod.Id, out string? folder)) continue;
            string lua = Path.Combine(folder, "lua");
            if (!Directory.Exists(lua)) continue;   // a base mod in the stack for its layout alone
            var timer = new Timer(_ => Fire(mod, folder), null, Timeout.Infinite, Timeout.Infinite);
            _timers.Add(timer);
            var watcher = new FileSystemWatcher(lua)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            watcher.Changed += (_, change) => Touched(timer, change.FullPath);
            watcher.Created += (_, change) => Touched(timer, change.FullPath);
            watcher.Deleted += (_, change) => Touched(timer, change.FullPath);
            watcher.Renamed += (_, change) => Touched(timer, change.FullPath, change.OldFullPath);
            watcher.Error += (_, _) => Schedule(timer);   // the buffer overflowed: read the files again anyway
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
            watched[mod.Id] = lua;
        }
        Watched = watched;
    }

    private void Touched(Timer timer, params string?[] paths)
    {
        // A folder's rename or removal can move .lua files; an editor's swap or backup file changes nothing.
        if (paths.Any(path => path != null && (path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
                                               || !Path.HasExtension(path))))
            Schedule(timer);
    }

    private void Schedule(Timer timer)
    {
        lock (_gate)
            if (!_disposed) timer.Change(_settle, Timeout.InfiniteTimeSpan);
    }

    private void Fire(ModRuntimeMod mod, string folder)
    {
        lock (_delivering)
        {
            lock (_gate)
                if (_disposed) return;
            ModLuaReloadCheck check;
            try
            {
                check = ModLuaReload.Read(folder, mod, _stack.Handlers, _runtime);
            }
            catch (Exception error)
            {
                check = new ModLuaReloadCheck([], [$"the files could not be read: {error.Message}"]);
            }
            _deliver(mod.Id, check);
        }
    }

    /// <summary>Stops watching. A delivery already running finishes; none starts after.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        foreach (FileSystemWatcher watcher in _watchers) watcher.Dispose();
        foreach (Timer timer in _timers) timer.Dispose();
    }
}
