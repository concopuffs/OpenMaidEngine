using Ome.Engine.Model;

namespace Ome.Engine.Localization;

/// <summary>The opcodes the message model reads, resolved from an opcode table by label.</summary>
public sealed record TextOpcodes(int ShowText, int DisplayFurigana, int EndTextLine, IReadOnlySet<int> Markers)
{
    /// <summary>Operand type of an inline string literal (the script's string pool).</summary>
    public const int LiteralOperandType = 2;

    public static TextOpcodes FromTable(OpcodeTable table)
    {
        int Require(string label) => table.ByLabel(label)
            ?? throw new InvalidDataException($"opcode table {table.AbiId} has no '{label}' opcode");
        var markers = new HashSet<int>();
        if (table.ByLabel("comment") is { } comment) markers.Add(comment);
        return new TextOpcodes(Require("show-text"), Require("display-furigana"), Require("end-text-line"), markers);
    }

    /// <summary>Whether the opcode draws a text run: <c>show-text</c> or <c>display-furigana</c>.</summary>
    public bool IsRun(int opcode) => opcode == ShowText || opcode == DisplayFurigana;
}

public enum TextRunKind { Text, Furigana }

/// <summary>One text instruction of a message. <see cref="Text"/> is the drawn operand (for furigana, the base
/// word); <see cref="Ruby"/> is the furigana reading. <see cref="BreaksBefore"/> counts the script's
/// <c>end-text-line</c>s between the previous run and this one.</summary>
public sealed record TextRun(int InstructionIndex, int Offset, TextRunKind Kind, Operand Text, Operand? Ruby,
                             int BreaksBefore);

/// <summary>A maximal run of text instructions on one layout (localization spec §6.2). <see cref="Offset"/> is the
/// first run's dword offset, the coordinate a site entry's <c>at</c> names.</summary>
public sealed record TextMessage(int Index, int FirstInstruction, int LastInstruction, int Offset, Operand Layout,
                                 IReadOnlyList<TextRun> Runs)
{
    /// <summary>The script's own line breaks inside the message.</summary>
    public int AuthoredBreaks => Runs.Sum(run => run.BreaksBefore);
}

/// <summary>The messages of one script, with a lookup from instruction index to its message and run.</summary>
public sealed class ScriptMessages
{
    private readonly int[] _messageOf;
    private readonly int[] _runOf;

    internal ScriptMessages(IReadOnlyList<TextMessage> messages, int instructionCount)
    {
        Messages = messages;
        _messageOf = Enumerable.Repeat(-1, instructionCount).ToArray();
        _runOf = Enumerable.Repeat(-1, instructionCount).ToArray();
        foreach (TextMessage message in messages)
            for (int run = 0; run < message.Runs.Count; run++)
            {
                _messageOf[message.Runs[run].InstructionIndex] = message.Index;
                _runOf[message.Runs[run].InstructionIndex] = run;
            }
    }

    public IReadOnlyList<TextMessage> Messages { get; }

    /// <summary>The message and run that instruction <paramref name="instructionIndex"/> belongs to, if it is a
    /// text run.</summary>
    public bool TryGetRun(int instructionIndex, out TextMessage message, out int runIndex)
    {
        if ((uint)instructionIndex < (uint)_messageOf.Length && _messageOf[instructionIndex] >= 0)
        {
            message = Messages[_messageOf[instructionIndex]];
            runIndex = _runOf[instructionIndex];
            return true;
        }
        message = null!;
        runIndex = -1;
        return false;
    }
}

/// <summary>
/// Splits a script into messages (localization spec §6.2). A message is a maximal run of <c>show-text</c> and
/// <c>display-furigana</c> instructions on one layout, with only <c>end-text-line</c> or marker instructions
/// between them, and no branch target inside. Nothing executes between the runs of a message, so its variable
/// operands can all be read when its first run executes.
/// </summary>
public static class MessageSegmenter
{
    /// <param name="branchTargets">Dword offsets that control flow can reach other than by falling through: jump,
    /// call, callback and switch targets. A message never continues across one.</param>
    public static ScriptMessages Segment(Script script, TextOpcodes opcodes, IReadOnlySet<int> branchTargets)
    {
        IReadOnlyList<Instruction> code = script.Instructions;
        var messages = new List<TextMessage>();
        int i = 0;
        while (i < code.Count)
        {
            Instruction first = code[i];
            if (!IsRun(first, opcodes))
            {
                i++;
                continue;
            }
            Operand layout = first.Args[0];
            var runs = new List<TextRun> { Run(i, first, opcodes, breaksBefore: 0) };
            int last = i, breaks = 0;
            for (int j = i + 1; j < code.Count; j++)
            {
                Instruction next = code[j];
                if (branchTargets.Contains(next.Offset)) break;
                if (IsRun(next, opcodes) && next.Args[0] == layout)
                {
                    runs.Add(Run(j, next, opcodes, breaks));
                    breaks = 0;
                    last = j;
                    continue;
                }
                if (next.Opcode == opcodes.EndTextLine && next.Args.Count >= 1 && next.Args[0] == layout)
                {
                    breaks++;
                    continue;
                }
                if (opcodes.Markers.Contains(next.Opcode)) continue;
                break;
            }
            messages.Add(new TextMessage(messages.Count, i, last, first.Offset, layout, runs));
            i = last + 1;
        }
        return new ScriptMessages(messages, code.Count);
    }

    /// <summary>The literal text of an inline string operand. False for any other operand, and for a literal the
    /// script's string pool could not decode.</summary>
    public static bool TryGetLiteral(Script script, Operand operand, out string text)
    {
        if (operand.Type == TextOpcodes.LiteralOperandType && script.Strings.TryGetValue((int)operand.Value, out text!))
            return true;
        text = "";
        return false;
    }

    /// <summary>Whether an inline string operand points at a string the pool could not decode.</summary>
    public static bool IsDamagedLiteral(Script script, Operand operand)
        => operand.Type == TextOpcodes.LiteralOperandType && !script.Strings.ContainsKey((int)operand.Value);

    /// <summary>The message's key when every run is a literal: the runs' drawn texts joined with nothing between
    /// them, without the ruby (spec §6.2).</summary>
    public static bool TryGetLiteralKey(Script script, TextMessage message, out string key)
    {
        var text = new System.Text.StringBuilder();
        foreach (TextRun run in message.Runs)
        {
            if (!TryGetLiteral(script, run.Text, out string part))
            {
                key = "";
                return false;
            }
            text.Append(part);
        }
        key = text.ToString();
        return true;
    }

    private static bool IsRun(Instruction instruction, TextOpcodes opcodes)
        => opcodes.IsRun(instruction.Opcode) && instruction.Args.Count >= 2;

    private static TextRun Run(int index, Instruction instruction, TextOpcodes opcodes, int breaksBefore)
    {
        bool furigana = instruction.Opcode == opcodes.DisplayFurigana;
        return new TextRun(index, instruction.Offset, furigana ? TextRunKind.Furigana : TextRunKind.Text,
            instruction.Args[1], furigana && instruction.Args.Count >= 3 ? instruction.Args[2] : null, breaksBefore);
    }
}
