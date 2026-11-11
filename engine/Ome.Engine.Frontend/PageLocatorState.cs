using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Ome.Engine.Model;
using Ome.Engine.Sys4;

/// <summary>
/// A show-text run's source layout as the page map needs it (localization spec §14, §18.1): its layout, the tops of its
/// first and last rows, where its last row's text ends without trailing spaces, the width of its first word, the
/// advance of a space, the layout's right edge and its line advance, and where its first and last rows start.
/// <see cref="Measure"/> builds it from the rows and glyph records (one per Unicode scalar) of the run's source text.
/// </summary>
public readonly record struct PageTextGeometry(
    int Layout, int FirstRowTop, int LastRowTop, int LastInkRight, int FirstWordWidth, int SpaceAdvance, int Right,
    int LineAdvance, int FirstRowLeft, int LastRowLeft)
{
    /// <summary>The geometry of <paramref name="text"/> laid out as <paramref name="rows"/> and
    /// <paramref name="records"/>; null when nothing was laid out. The first word is the importer's (spec §18.1): one
    /// wide character, or the narrow characters up to a space or a wide one.</summary>
    public static PageTextGeometry? Measure(
        string text, IReadOnlyList<AdvTextRow> rows, IReadOnlyList<AdvRetainedGlyphRecord> records, int layout,
        int right, int lineAdvance, int spaceAdvance)
    {
        if (rows.Count == 0 || records.Count == 0) return null;
        var scalars = new List<int>(text.Length);
        foreach (Rune rune in text.EnumerateRunes()) scalars.Add(rune.Value);
        int glyphs = System.Math.Min(scalars.Count, records.Count);
        AdvTextRow last = rows[^1];
        int lastInk = last.Left;   // a row of spaces ends where it starts
        for (int i = System.Math.Min(last.FirstGlyph + last.GlyphCount, glyphs) - 1; i >= last.FirstGlyph; i--)
            if (!PageLocatorState.IsSpace(scalars[i]))
            {
                lastInk = records[i].Right;
                break;
            }
        AdvTextRow first = rows[0];
        int start = first.FirstGlyph, end = start;
        int rowEnd = System.Math.Min(first.FirstGlyph + first.GlyphCount, glyphs);
        if (end < rowEnd && PageLocatorState.IsWide(scalars[end])) end++;
        else
            while (end < rowEnd && !PageLocatorState.IsSpace(scalars[end]) && !PageLocatorState.IsWide(scalars[end]))
                end++;
        int word = end > start ? records[end - 1].Right - records[start].Left : 0;
        return new PageTextGeometry(layout, first.Top, last.Top, lastInk, word, spaceAdvance, right, lineAdvance,
                                    first.Left, last.Left);
    }
}

/// <summary>
/// Thread-safe bridge between the VM trace, the ADV host's page waits, and the Godot UI.
/// A page number is run-relative; the script + wait offset is its canonical locator.
/// </summary>
/// <remarks>
/// Each page record also holds the page's text (localization spec §14, §18.1): <c>dialogue</c>, the source text of its
/// runs joined with <c>\n</c> where a run starts a new row of its layout (two for a blank row between); <c>breaks</c>,
/// one class per <c>\n</c>: <c>wrap</c> when the row before it, a space (none between two wide characters) and the
/// next run's first word would cross the layout's right edge, else <c>break</c>, which a blank row, a run that starts
/// with a space and a change of layout always are; with a language active, <c>presented</c>, the drawn texts joined at
/// the same new rows; and <c>cut</c> when a drawn run stopped at its layout's bottom.
/// </remarks>
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

    // The page's text (see the remarks).
    private readonly StringBuilder _dialogue = new();
    private readonly List<string> _breaks = new();
    private StringBuilder? _presented;
    private bool _presentedRowPending;
    private bool _cut;
    private bool _hasRuns;
    private PageTextGeometry? _previous;
    private string _rowText = "";   // the source text of the previous run's last row: only its end and blankness count

    /// <summary>The map's file name for <paramref name="scene"/>: <c>page-map-&lt;SCENE&gt;.jsonl</c>.</summary>
    public static string MapFileName(string scene) => $"page-map-{NormalizeScript(scene)}.jsonl";

    /// <summary>Where an editor or development run writes the map, and where <c>tools/locate_page.py</c> reads it:
    /// the profile's build directory, <c>build/games/&lt;profile&gt;</c>, so two games' same-named scenes never share
    /// a file.</summary>
    public static string DevelopmentMapPath(string profileId, string scene)
        => Path.Combine(Paths.GameBuild(profileId), MapFileName(scene));

    /// <summary>A locator that writes its map to <paramref name="mapPath"/>. When the file cannot be opened (another
    /// running port holds it, or its directory is read-only) the locator keeps no map, and <paramref name="warning"/>
    /// says why: the map is a diagnostic and never stops a run.</summary>
    public static PageLocatorState Open(string rootScene, string? mapPath, out string? warning)
    {
        warning = null;
        try
        {
            return new PageLocatorState(rootScene, mapPath);
        }
        catch (System.Exception error) when (error is IOException or System.UnauthorizedAccessException)
        {
            warning = $"page map {mapPath} cannot be written, so this run keeps none: {error.Message}";
            return new PageLocatorState(rootScene, null);
        }
    }

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

    /// <summary>Whether the locator writes a map, so a host measures runs for it only then.</summary>
    public bool WritesMap => _writer != null;

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

    public void Text(int stringOffset, string text) => Text(stringOffset, text, null, null);

    /// <summary>A show-text run: its source <paramref name="text"/>; <paramref name="presented"/>, what it drew while a
    /// language is active (empty for a run a message's first run drew, null with no language); its source layout
    /// <paramref name="geometry"/>, null when it laid nothing out; and whether the drawn run was <paramref name="cut"/>
    /// at its layout's bottom.</summary>
    public void Text(int stringOffset, string text, string? presented, PageTextGeometry? geometry, bool cut = false)
    {
        lock (_lock)
        {
            _textScript = _script;
            _textOffset = _offset;
            _textStringOffset = stringOffset;
            _text = text;
            _cut |= cut;
            if (presented != null) _presented ??= new StringBuilder();
            if (text.Length == 0 && string.IsNullOrEmpty(presented)) return;

            if (_hasRuns && _previous is { } before && geometry is { } after)
            {
                int rows = NewRows(before, after);
                for (int row = 0; row < rows; row++)
                {
                    _dialogue.Append('\n');
                    _breaks.Add(rows == 1 && Wraps(before, _rowText, after, text) ? "wrap" : "break");
                }
                if (rows > 0)
                {
                    _presentedRowPending = true;
                    _rowText = "";
                }
            }
            _dialogue.Append(text);
            if (!string.IsNullOrEmpty(presented))
            {
                if (_presentedRowPending && _presented!.Length > 0) _presented.Append('\n');
                _presented!.Append(presented);
                _presentedRowPending = false;
            }
            _hasRuns = true;
            if (geometry != null)
            {
                _previous = geometry;
                _rowText += text;
            }
        }
    }

    /// <summary>How many rows down from <paramref name="before"/>'s last row <paramref name="after"/> starts: none for a
    /// run that goes on along the row, one for a run that moved up or back along the row (the layout was cleared or
    /// the cursor set) or into another layout.</summary>
    private static int NewRows(PageTextGeometry before, PageTextGeometry after)
    {
        if (before.Layout != after.Layout || after.FirstRowTop < before.LastRowTop) return 1;
        if (after.FirstRowTop == before.LastRowTop) return after.FirstRowLeft > before.LastRowLeft ? 0 : 1;
        int advance = System.Math.Max(1, after.LineAdvance);
        return System.Math.Max(1, (int)System.Math.Round((after.FirstRowTop - before.LastRowTop) / (double)advance));
    }

    /// <summary>The importer's wrap rule (spec §18.1) at the layout's real right edge.</summary>
    private static bool Wraps(PageTextGeometry before, string beforeText, PageTextGeometry after, string afterText)
    {
        if (before.Layout != after.Layout) return false;
        string line = beforeText.TrimEnd(' ', '\t', '　');
        if (line.Length == 0 || afterText.Length == 0 || afterText[0] is ' ' or '\t' or '　') return false;
        bool wideJoin = IsWide(char.ConvertToUtf32(line, line.Length - 1 - (char.IsLowSurrogate(line[^1]) ? 1 : 0)))
                        && IsWide(char.ConvertToUtf32(afterText, 0));
        return before.LastInkRight + (wideJoin ? 0 : after.SpaceAdvance) + after.FirstWordWidth > after.Right;
    }

    internal static bool IsSpace(int scalar) => scalar is ' ' or '\t' or 0x3000;

    /// <summary>A double-byte character of CP932, as the importer counts widths: anything but ASCII and half-width
    /// katakana.</summary>
    internal static bool IsWide(int scalar) => scalar >= 0x80 && scalar is not (>= 0xff61 and <= 0xff9f);

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
                    ["dialogue"] = _hasRuns ? _dialogue.ToString() : null,
                    ["breaks"] = _breaks.ToArray(),
                    ["call_stack"] = _callStack,
                };
                if (_presented != null) row["presented"] = _presented.ToString();
                if (_cut) row["cut"] = true;
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
            _dialogue.Clear();
            _breaks.Clear();
            _presented = null;
            _presentedRowPending = false;
            _cut = false;
            _hasRuns = false;
            _previous = null;
            _rowText = "";
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
