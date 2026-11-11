using System.Text;
using Ome.Engine.Localization;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Mods.Localization.Import;
using Ome.Script.Abi;
using Ome.Script.Patch;

namespace Ome.Mods.Localization;

public enum ScriptStringKind
{
    /// <summary>A message whose runs are all literals (spec §6.2).</summary>
    Message,
    /// <summary>One literal run of a message of several runs: the presenter falls back to it when the message as a
    /// whole has no entry (gate 4). Extract writes no entry for it.</summary>
    Run,
    /// <summary>A message with variable runs: a pattern with a placeholder per variable run.</summary>
    Pattern,
    /// <summary>A literal <c>draw-string</c> draws.</summary>
    DrawLiteral,
    /// <summary>A literal <c>set-string</c> stores into a global string cell (recorded, spec §5.2).</summary>
    StoredLiteral,
    /// <summary>A literal <c>set-string</c> stores into a frame local; only a global entry reaches it.</summary>
    FrameLiteral,
    /// <summary>A string the script composes with <c>concat</c>: a pattern (spec §6.3).</summary>
    Composed,
}

/// <summary>
/// A string an installed script draws or stores (localization spec §13): where it is (the script and the instruction's
/// dword offset; a message's is its first run's), its kind, its source as a catalog entry writes it (braces doubled;
/// a pattern's variable parts as <c>{vN}</c>), the drawn text of an exact one, and a message's text with its
/// authored line breaks when it has any.
/// </summary>
public sealed record ScriptString(string Script, int Offset, ScriptStringKind Kind, string Source, string? Text,
                                  string? Authored = null)
{
    public bool IsPattern => Kind is ScriptStringKind.Pattern or ScriptStringKind.Composed;
}

/// <summary>
/// Reads the strings of an installed script, as <c>omp text extract</c>, <c>check</c> and <c>stats</c> see them: its
/// messages (segmented as the presenter segments them, spec §6.2), the literals it draws or stores, and the strings it
/// composes (the importer's composed-string walk). A script the opcode table cannot decode gives none.
/// </summary>
public sealed class ScriptTextScanner
{
    private const int LocalStringType = 0xb;

    private readonly OpcodeTable _table;
    private readonly OpcodeContracts _contracts;
    private readonly TextOpcodes _text;
    private readonly int? _drawString, _setString;
    private readonly ComposedStringFinder? _composed;

    /// <exception cref="InvalidDataException">The opcode table lacks a text opcode.</exception>
    public ScriptTextScanner(OpcodeTable table, OpcodeContracts contracts)
    {
        _table = table;
        _contracts = contracts;
        _text = TextOpcodes.FromTable(table);
        _drawString = table.ByLabel("draw-string");
        _setString = table.ByLabel("set-string");
        try
        {
            _composed = new ComposedStringFinder(table, contracts);
        }
        catch (InvalidDataException)
        {
            _composed = null;
        }
    }

    /// <summary>The strings of one script, in offset order; null when it cannot be decoded.</summary>
    public IReadOnlyList<ScriptString>? Scan(string name, byte[] bytes)
    {
        Ome.Engine.Model.Script code;
        try
        {
            code = Sys4Loader.Parse(bytes, _table, name);
        }
        catch (Exception error) when (error is InvalidDataException or Sys4OpcodeDecodeException
                                          or IndexOutOfRangeException or ArgumentException)
        {
            return null;
        }
        string script = TextCatalog.NormalizeScriptName(name);
        var found = new List<ScriptString>();
        ScriptMessages messages = MessageSegmenter.Segment(code, _text, ScriptAlignment.BranchTargets(code, _contracts));
        foreach (TextMessage message in messages.Messages) AddMessage(code, script, message, found);

        for (int k = 0; k < code.Instructions.Count; k++)
        {
            if (messages.TryGetRun(k, out _, out _)) continue;
            Instruction instruction = code.Instructions[k];
            ScriptStringKind? kind = LiteralKind(instruction);
            if (kind == null) continue;
            foreach (Operand operand in instruction.Args)
                if (MessageSegmenter.TryGetLiteral(code, operand, out string literal) && literal.Length > 0)
                    found.Add(new ScriptString(script, instruction.Offset, kind.Value, TextTemplate.Escape(literal), literal));
        }

        if (_composed != null)
            foreach (ComposedString composed in _composed.Find(code, code, ScriptAlignment.Identity(code), script,
                         new ComposedCounts()))
            {
                TextTemplate template = TextTemplate.Parse(composed.Source, allowKinds: true);
                found.Add(template.HasPlaceholders
                    ? new ScriptString(script, composed.ConsumerOffset, ScriptStringKind.Composed, composed.Source, null)
                    : new ScriptString(script, composed.ConsumerOffset, ScriptStringKind.Composed, composed.Source,
                        template.Literal));
            }
        found.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        return found;
    }

    private static void AddMessage(Ome.Engine.Model.Script code, string script, TextMessage message,
                                   List<ScriptString> found)
    {
        var source = new StringBuilder();
        var authored = new StringBuilder();
        bool literalOnly = true, anyLiteral = false;
        int variables = 0;
        foreach (TextRun run in message.Runs)
        {
            authored.Append('\n', run.BreaksBefore);
            if (MessageSegmenter.TryGetLiteral(code, run.Text, out string part))
            {
                source.Append(TextTemplate.Escape(part));
                authored.Append(part);
                anyLiteral |= part.Length > 0;
                if (message.Runs.Count > 1 && part.Length > 0)
                    found.Add(new ScriptString(script, run.Offset, ScriptStringKind.Run, TextTemplate.Escape(part), part));
            }
            else
            {
                literalOnly = false;
                string placeholder = "{v" + ++variables + "}";
                source.Append(placeholder);
                authored.Append(placeholder);
            }
        }
        if (!anyLiteral) return;
        string? lines = message.AuthoredBreaks > 0 ? authored.ToString() : null;
        if (literalOnly)
        {
            MessageSegmenter.TryGetLiteralKey(code, message, out string key);
            found.Add(new ScriptString(script, message.Offset, ScriptStringKind.Message, TextTemplate.Escape(key), key, lines));
        }
        else found.Add(new ScriptString(script, message.Offset, ScriptStringKind.Pattern, source.ToString(), null, lines));
    }

    private ScriptStringKind? LiteralKind(Instruction instruction)
        => instruction.Opcode == _drawString ? ScriptStringKind.DrawLiteral
            : instruction.Opcode != _setString || instruction.Args.Count < 2 ? null
            : instruction.Args[0].Type == LocalStringType ? ScriptStringKind.FrameLiteral
            : ScriptStringKind.StoredLiteral;
}

/// <summary>
/// The strings of every installed script, with the lookups <c>omp text check</c> and <c>stats</c> ask (spec §13):
/// whether an exact entry's source is drawn or stored anywhere in its scope, and the fingerprint of each script.
/// </summary>
public sealed class ScriptTextCorpus
{
    private readonly HashSet<string> _anywhere = new(StringComparer.Ordinal);
    private readonly HashSet<(string Script, string Text)> _inScript = new();
    private readonly HashSet<(string Script, int Offset, string Text)> _atSite = new();

    /// <summary>Each script's strings, by normalized name, in the order scanned.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ScriptString>> Scripts { get; }

    /// <summary>Each script's fingerprint (<c>sha256:…</c>), by normalized name.</summary>
    public IReadOnlyDictionary<string, string> Fingerprints { get; }

    /// <summary>Scripts that could not be decoded.</summary>
    public IReadOnlyList<string> Unreadable { get; }

    private ScriptTextCorpus(Dictionary<string, IReadOnlyList<ScriptString>> scripts,
                             Dictionary<string, string> fingerprints, List<string> unreadable)
    {
        Scripts = scripts;
        Fingerprints = fingerprints;
        Unreadable = unreadable;
        foreach (ScriptString found in scripts.Values.SelectMany(list => list))
        {
            if (found.Text == null) continue;
            _anywhere.Add(found.Text);
            _inScript.Add((found.Script, found.Text));
            _atSite.Add((found.Script, found.Offset, found.Text));
        }
    }

    /// <summary>Scans <paramref name="scripts"/> (name and bytes); the first script of a name wins, as the importer
    /// pairs them.</summary>
    public static ScriptTextCorpus Scan(ScriptTextScanner scanner, IEnumerable<(string Name, Func<byte[]> Bytes)> scripts)
    {
        var found = new Dictionary<string, IReadOnlyList<ScriptString>>(StringComparer.Ordinal);
        var fingerprints = new Dictionary<string, string>(StringComparer.Ordinal);
        var unreadable = new List<string>();
        foreach (var (name, read) in scripts)
        {
            string script = TextCatalog.NormalizeScriptName(name);
            if (found.ContainsKey(script) || unreadable.Contains(script)) continue;
            byte[] bytes = read();
            fingerprints[script] = OmsPatchPlan.FingerprintOf(bytes);
            if (scanner.Scan(name, bytes) is { } strings) found[script] = strings;
            else unreadable.Add(script);
        }
        return new ScriptTextCorpus(found, fingerprints, unreadable);
    }

    /// <summary>Whether an exact entry's source is drawn or stored where its scope reaches: anywhere for a global
    /// entry, in its script for a script entry, at its instruction for a site entry. Patterns are not tested.</summary>
    public bool Reaches(TextEntry entry)
        => entry.IsPattern
           || (entry.Script is not { } script ? _anywhere.Contains(entry.Key)
               : entry.At is { } at ? _atSite.Contains((script, at, entry.Key))
               : _inScript.Contains((script, entry.Key)));
}
