using System.Text;
using Ome.Engine.Localization;
using Ome.Engine.Model;
using Ome.Script.Abi;
using Ome.Script.Compile;

namespace Ome.Mods.Localization.Import;

/// <summary>A string a script composes (R4 of <c>docs/localization-import-test.md</c>'s review): the template the base
/// builds on one path to the instruction that stores or draws it, and the template the translation builds on the
/// same path. Placeholders stand for the parts that are not literals, <c>{vN:num}</c> for numbers.</summary>
public sealed record ComposedString(int ConsumerOffset, string Source, string Text);

/// <summary>
/// Finds the strings a script composes with <c>concat</c> and <c>toString</c>. It starts from each instruction that
/// stores a string variable into another (<c>set-string</c>) or draws it (<c>draw-string</c>, <c>show-text</c>), and
/// walks back along each path inside the function to the write that starts the variable's value. The writes on the
/// path give a template. Paths join before a store (<c>SHOWCC@0x8c</c> joins the <c>－</c> and <c>＋</c> forms of one
/// notice), so a store can have several. A walk gives up at <see cref="MaxPaths"/> paths, at a loop, at the
/// function's entry and after <see cref="MaxSteps"/> instructions; the store is then reported as skipped. A call on
/// the path is stepped over: a callee that writes the variable is not seen, which costs at most a pattern that never
/// matches, since the drawn string then has another form.
/// </summary>
internal sealed class ComposedStringFinder
{
    public const int MaxPaths = 8;
    public const int MaxSteps = 2048;

    private const uint NoTarget = 0xFFFFFFFF;
    private const int LiteralType = TextOpcodes.LiteralOperandType;
    private static readonly HashSet<int> StringTypes = [5, 8, 11, 14];

    private readonly OpcodeContracts _contracts;
    private readonly int _setString, _concat, _toString, _drawString, _showText, _jcc;

    public ComposedStringFinder(OpcodeTable table, OpcodeContracts contracts)
    {
        _contracts = contracts;
        int Require(string label) => table.ByLabel(label)
            ?? throw new InvalidDataException($"opcode table {table.AbiId} has no '{label}' opcode");
        _setString = Require("set-string");
        _concat = Require("concat");
        _toString = Require("toString");
        _drawString = Require("draw-string");
        _showText = Require("show-text");
        _jcc = Require("jcc");
    }

    /// <summary>The composed strings of a changed script, each with its translated template.</summary>
    public List<ComposedString> Find(Ome.Engine.Model.Script code, Ome.Engine.Model.Script translated,
                                     ScriptAlignment alignment, string script, ComposedCounts counts)
    {
        IReadOnlyList<Instruction> b = code.Instructions;
        var concatDestinations = b.Where(i => i.Opcode == _concat && i.Args.Count == 3).Select(i => i.Args[0])
            .ToHashSet();
        var found = new List<ComposedString>();
        if (concatDestinations.Count == 0) return found;
        Flow flow = Flow.Of(code, _contracts, _jcc);

        for (int c = 0; c < b.Count; c++)
        {
            if (Consumed(b[c]) is not { } variable || !concatDestinations.Contains(variable)) continue;
            List<List<int>> paths = Walk(b, flow, c, variable, out string? skipped);
            var templates = new List<(string Source, string Text)>();
            foreach (List<int> path in paths)
            {
                if (!path.Any(k => b[k].Opcode == _concat)) continue;
                if (Template(code, code, path, path, variable, null) is not { } source
                    || Template(translated, code, path.Select(k => alignment.BaseToTranslated[k]).ToList(), path,
                        variable, code) is not { } text)
                {
                    counts.Unreadable.Add(new ReportedComposed(script, b[c].Offset, "", ""));
                    continue;
                }
                if (!templates.Contains((source, text))) templates.Add((source, text));
            }
            if (templates.Count == 0)
            {
                if (skipped != null) counts.Skipped.Add(new ReportedSkip(script, b[c].Offset, skipped));
                continue;
            }
            counts.Stores++;
            if (skipped != null) counts.Skipped.Add(new ReportedSkip(script, b[c].Offset, skipped));
            foreach (var (source, text) in templates)
            {
                counts.Templates++;
                if (!HasLiteral(source)) continue;
                if (source == text) counts.Untranslated++;
                else counts.Patterns.Add(new ReportedComposed(script, b[c].Offset, source, text));
                found.Add(new ComposedString(b[c].Offset, source, text));
            }
        }
        return found;
    }

    /// <summary>The string variable an instruction stores or draws, if it is one of the consumers.</summary>
    private Operand? Consumed(Instruction instruction)
    {
        int argument = instruction.Opcode == _setString ? 1
            : instruction.Opcode == _drawString ? 3
            : instruction.Opcode == _showText ? 1
            : -1;
        if (argument < 0 || instruction.Args.Count <= argument) return null;
        Operand operand = instruction.Args[argument];
        if (!StringTypes.Contains(operand.Type)) return null;
        if (instruction.Opcode == _setString && instruction.Args[0] == operand) return null;
        return operand;
    }

    private bool Writes(Instruction instruction, Operand variable)
        => (instruction.Opcode == _setString || instruction.Opcode == _concat || instruction.Opcode == _toString)
           && instruction.Args.Count >= 2 && instruction.Args[0] == variable;

    /// <summary>A write that keeps part of the variable's value: <c>concat</c> with the variable as a source.</summary>
    private bool Extends(Instruction instruction, Operand variable)
        => instruction.Opcode == _concat && instruction.Args.Skip(1).Contains(variable);

    /// <summary>The paths back from <paramref name="consumer"/>, each the writes to the variable in forward order,
    /// from the write that starts its value.</summary>
    private List<List<int>> Walk(IReadOnlyList<Instruction> b, Flow flow, int consumer, Operand variable,
                                 out string? skipped)
    {
        var paths = new List<List<int>>();
        string? reason = null;
        int steps = 0;
        var stack = new Stack<(int At, List<int> Writes, HashSet<int> Seen)>();
        foreach (int p in flow.Predecessors[consumer]) stack.Push((p, [], []));
        if (flow.Predecessors[consumer].Count == 0) reason = "function entry";
        while (stack.Count > 0)
        {
            var (at, writes, seen) = stack.Pop();
            while (true)
            {
                if (paths.Count >= MaxPaths)
                {
                    reason = "paths";
                    break;
                }
                if (++steps > MaxSteps)
                {
                    reason = "steps";
                    break;
                }
                if (!seen.Add(at))
                {
                    reason = "loop";
                    break;
                }
                Instruction instruction = b[at];
                if (Writes(instruction, variable))
                {
                    writes.Add(at);
                    if (!Extends(instruction, variable))
                    {
                        writes.Reverse();
                        paths.Add(writes);
                        break;
                    }
                }
                if (flow.Entries.Contains(at) || flow.Predecessors[at].Count == 0)
                {
                    reason = "function entry";
                    break;
                }
                List<int> predecessors = flow.Predecessors[at];
                for (int k = 1; k < predecessors.Count; k++)
                    stack.Push((predecessors[k], [.. writes], [.. seen]));
                at = predecessors[0];
            }
            if (reason is "paths" or "steps") break;
        }
        skipped = reason;
        return paths;
    }

    /// <summary>
    /// The template the writes of one path build. <paramref name="indices"/> are the path's instructions in
    /// <paramref name="script"/>; <paramref name="baseIndices"/> name the placeholders, so a translated path gets the
    /// base's names. A translated literal that is its source with a character missing is the translated install's
    /// damage, and the base literal is used (<see cref="ScriptTranslationImporter.Shortened"/>).
    /// </summary>
    private string? Template(Ome.Engine.Model.Script script, Ome.Engine.Model.Script code, List<int> indices,
                             List<int> baseIndices, Operand variable, Ome.Engine.Model.Script? baseScript)
    {
        var parts = new List<(string? Literal, int Instruction, int Argument, bool Number)>();
        for (int w = 0; w < indices.Count; w++)
        {
            Instruction instruction = script.Instructions[indices[w]];
            Instruction source = code.Instructions[baseIndices[w]];
            var next = new List<(string?, int, int, bool)>();
            if (instruction.Opcode == _toString) next.Add((null, baseIndices[w], 1, true));
            else
            {
                for (int a = 1; a < instruction.Args.Count; a++)
                {
                    Operand operand = instruction.Args[a];
                    if (operand == variable)
                    {
                        next.AddRange(parts);
                        continue;
                    }
                    if (operand.Type == LiteralType)
                    {
                        if (!TryLiteral(script, operand, out string literal)) return null;
                        if (baseScript != null && MessageSegmenter.TryGetLiteral(baseScript, source.Args[a],
                                out string original)
                            && ScriptTranslationImporter.Shortened(original, literal) == Shortening.Damage)
                            literal = original;
                        next.Add((literal, baseIndices[w], a, false));
                    }
                    else next.Add((null, baseIndices[w], a, !StringTypes.Contains(operand.Type)));
                }
            }
            parts = next;
        }

        // Placeholders are named in the order the base template shows them; the same operand twice is not a pattern.
        var names = new Dictionary<(int, int), string>();
        foreach (var part in OrderOfBase(code, baseIndices, variable))
            names.TryAdd(part, "v" + (names.Count + 1));
        var text = new StringBuilder();
        var used = new HashSet<string>();
        foreach (var (literal, instruction, argument, number) in parts)
        {
            if (literal != null)
            {
                text.Append(TextTemplate.Escape(literal));
                continue;
            }
            string name = names[(instruction, argument)];
            if (!used.Add(name)) return null;
            text.Append('{').Append(name);
            if (number && baseScript == null) text.Append(":num");
            text.Append('}');
        }
        return text.ToString();
    }

    /// <summary>The base path's placeholder operands in the order its template shows them.</summary>
    private IEnumerable<(int, int)> OrderOfBase(Ome.Engine.Model.Script code, List<int> path, Operand variable)
    {
        var parts = new List<(int, int)?>();
        foreach (int k in path)
        {
            Instruction instruction = code.Instructions[k];
            var next = new List<(int, int)?>();
            if (instruction.Opcode == _toString) next.Add((k, 1));
            else
                for (int a = 1; a < instruction.Args.Count; a++)
                {
                    Operand operand = instruction.Args[a];
                    if (operand == variable) next.AddRange(parts);
                    else if (operand.Type != LiteralType) next.Add((k, a));
                }
            parts = next;
        }
        return parts.OfType<(int, int)>();
    }

    private static bool TryLiteral(Ome.Engine.Model.Script script, Operand operand, out string literal)
        => MessageSegmenter.TryGetLiteral(script, operand, out literal)
           || (MessageSegmenter.IsDamagedLiteral(script, operand)
               && ScriptTranslationImporter.TryDecodeLenient(script, operand, out literal));

    private static bool HasLiteral(string template)
        => TextTemplate.Parse(template, allowKinds: true).Parts.Any(part => part is LiteralPart);

    /// <summary>A script's control flow inside its functions: each instruction's predecessors (fall-through and
    /// jumps), and the instructions a call, callback or coroutine enters, where a walk back stops.</summary>
    private sealed record Flow(List<int>[] Predecessors, HashSet<int> Entries)
    {
        public static Flow Of(Ome.Engine.Model.Script code, OpcodeContracts contracts, int jcc)
        {
            IReadOnlyList<Instruction> b = code.Instructions;
            var index = new Dictionary<int, int>();
            for (int k = 0; k < b.Count; k++) index[b[k].Offset] = k;
            var predecessors = new List<int>[b.Count];
            for (int k = 0; k < b.Count; k++) predecessors[k] = [];
            // The script's T2 and T3 tables list call sites, not entries; a walk steps over a call.
            var entries = new HashSet<int> { 0 };
            for (int k = 0; k < b.Count; k++)
            {
                Instruction instruction = b[k];
                bool transfer = contracts.CodeTargetKind(instruction.Opcode) == "transfer";
                int targets = 0;
                for (int a = 0; a < instruction.Args.Count; a++)
                {
                    Operand operand = instruction.Args[a];
                    bool target = contracts.IsCodeTarget(instruction.Opcode, a + 1);
                    if (operand.Type != 0 || !(target || contracts.IsCodeValue(instruction.Opcode, a + 1))) continue;
                    if ((uint)operand.Value == NoTarget || !index.TryGetValue((int)operand.Value, out int to)) continue;
                    if (target && transfer)
                    {
                        predecessors[to].Add(k);
                        targets++;
                    }
                    else entries.Add(to);
                }
                bool fallsThrough = !OmsCompiler.IsTerminator(instruction.Opcode)
                                    && !(instruction.Opcode == jcc && targets == 2);
                if (fallsThrough && k + 1 < b.Count) predecessors[k + 1].Add(k);
            }
            return new Flow(predecessors, entries);
        }
    }
}
