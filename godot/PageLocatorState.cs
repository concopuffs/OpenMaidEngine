using System.Collections.Generic;
using System.IO;
using System.Text.Json;

/// <summary>
/// Thread-safe bridge between the VM trace, the ADV host's page waits, and the Godot UI.
/// A page number is run-relative; the script + wait offset is its canonical locator.
/// </summary>
public sealed class PageLocatorState : System.IDisposable
{
    private readonly object _lock = new();
    private readonly string _rootScene;
    private readonly StreamWriter? _writer;
    private string _script = "<startup>";
    private int _offset = -1;
    private string[] _callStack = System.Array.Empty<string>();
    private string? _pageStartScript;
    private int? _pageStartOffset;
    private string? _textScript;
    private int? _textOffset;
    private int? _textStringOffset;
    private string? _text;
    private string _currentDisplay;
    private bool _disposed;

    public PageLocatorState(string rootScene, string? mapPath)
    {
        _rootScene = rootScene.ToUpperInvariant();
        _currentDisplay = _rootScene;
        if (mapPath == null) return;
        var dir = Path.GetDirectoryName(mapPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _writer = new StreamWriter(mapPath, append: false) { AutoFlush = true };
    }

    public string CurrentDisplay { get { lock (_lock) return _currentDisplay; } }

    public void Step(string script, int offset)
    {
        lock (_lock)
        {
            _script = NormalizeScript(script);
            _offset = offset;
            if (_pageStartOffset == null)
            {
                _pageStartScript = _script;
                _pageStartOffset = offset;
            }
        }
    }

    public void CallStack(string[] callStack)
    {
        lock (_lock)
        {
            _callStack = new string[callStack.Length];
            for (int i = 0; i < callStack.Length; i++) _callStack[i] = NormalizeScript(callStack[i]);
        }
    }

    public void Text(int stringOffset, string text)
    {
        lock (_lock)
        {
            _textScript = _script;
            _textOffset = _offset;
            _textStringOffset = stringOffset;
            _text = text;
        }
    }

    public void Wait(int page)
    {
        lock (_lock)
        {
            string preview = Preview(_text);
            string textLocation = _textOffset is int textOffset
                ? $"{_textScript}@0x{textOffset:x}"
                : "none";
            _currentDisplay = $"{_rootScene} P{page:000} · wait {_script}@0x{_offset:x} · text {textLocation}";
            if (preview.Length > 0) _currentDisplay += $" · {preview}";

            if (_writer != null && !_disposed)
            {
                var row = new Dictionary<string, object?>
                {
                    ["root_scene"] = _rootScene,
                    ["page"] = page,
                    ["page_start_script"] = _pageStartScript,
                    ["page_start_offset"] = Hex(_pageStartOffset),
                    ["wait_script"] = _script,
                    ["wait_offset"] = Hex(_offset),
                    ["text_script"] = _textScript,
                    ["text_offset"] = Hex(_textOffset),
                    ["text_string_offset"] = Hex(_textStringOffset),
                    ["text"] = _text,
                    ["call_stack"] = _callStack,
                };
                _writer.WriteLine(JsonSerializer.Serialize(row));
            }

            // The display remains parked on this page, but text/start observations for the next page must
            // not inherit stale values if a page contains no show-text operation of its own.
            _pageStartScript = null;
            _pageStartOffset = null;
            _textScript = null;
            _textOffset = null;
            _textStringOffset = null;
            _text = null;
        }
    }

    private static string NormalizeScript(string script)
        => Path.GetFileNameWithoutExtension(script).ToUpperInvariant();

    private static string? Hex(int? value) => value is int v ? $"0x{v:x}" : null;

    private static string Preview(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string oneLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (oneLine.Length > 36) oneLine = oneLine[..35] + "…";
        return $"「{oneLine}」";
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _writer?.Dispose();
        }
    }
}
