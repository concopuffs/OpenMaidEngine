using Ome.Engine.Localization;
using Ome.Engine.Model;
using Ome.Script.Abi;

namespace Ome.Mods.Localization.Import;

/// <summary>Where the alignment of a translated script with its base failed.</summary>
public sealed record AlignmentFailure(int BaseIndex, int BaseOffset, string BaseOpcode, int TranslatedIndex,
                                      int TranslatedOffset, string TranslatedOpcode, string Reason);

/// <summary>A matched jump, call or callback whose target does not land on the matched instruction.</summary>
public sealed record TargetDisagreement(int BaseOffset, string Opcode, int Operand, long BaseTarget,
                                        long TranslatedTarget, long ExpectedTarget);

/// <summary>
/// The instruction-level alignment of a translated script with its base (localization spec §13, §18.1). A
/// translation that only changes text keeps every base instruction and may add <c>show-text</c> and
/// <c>end-text-line</c> instructions (hand-made line breaks). Two instructions match when their opcode and operands
/// are equal, ignoring inline string literals and code offsets, which the added instructions shift.
/// </summary>
public sealed class ScriptAlignment
{
    private ScriptAlignment(int[] baseToTranslated, bool[] inserted, IReadOnlyList<TargetDisagreement> disagreements)
    {
        BaseToTranslated = baseToTranslated;
        Inserted = inserted;
        TargetDisagreements = disagreements;
    }

    /// <summary>For each base instruction index, the translated instruction index it matches.</summary>
    public IReadOnlyList<int> BaseToTranslated { get; }

    /// <summary>For each translated instruction index, whether it was added by the translation.</summary>
    public IReadOnlyList<bool> Inserted { get; }

    public IReadOnlyList<TargetDisagreement> TargetDisagreements { get; }

    public int InsertedCount => Inserted.Count(added => added);

    /// <summary>Align <paramref name="translated"/> with <paramref name="baseScript"/>. Greedy: a translated
    /// instruction that matches the next base instruction is taken as that instruction, and any other is an
    /// insertion if it is one of the insertable text opcodes. Within one message the runs it pairs are
    /// interchangeable, since messages are attributed by span; <see cref="ScriptTranslationImporter"/> reports
    /// insertions between two adjacent messages, where the split is ambiguous.</summary>
    public static bool TryAlign(Ome.Engine.Model.Script baseScript, Ome.Engine.Model.Script translated,
                                OpcodeTable table, OpcodeContracts contracts, TextOpcodes text,
                                out ScriptAlignment alignment, out AlignmentFailure? failure)
    {
        IReadOnlyList<Instruction> b = baseScript.Instructions, t = translated.Instructions;
        var map = Enumerable.Repeat(-1, b.Count).ToArray();
        var inserted = new bool[t.Count];
        int bi = 0, ti = 0;
        while (bi < b.Count && ti < t.Count)
        {
            if (Same(b[bi], t[ti], contracts, out _))
            {
                map[bi++] = ti++;
                continue;
            }
            if (t[ti].Opcode == text.ShowText || t[ti].Opcode == text.EndTextLine)
            {
                inserted[ti++] = true;
                continue;
            }
            Same(b[bi], t[ti], contracts, out string reason);
            alignment = null!;
            failure = Failure(bi, ti, reason);
            return false;
        }
        while (ti < t.Count && (t[ti].Opcode == text.ShowText || t[ti].Opcode == text.EndTextLine))
            inserted[ti++] = true;
        if (bi < b.Count || ti < t.Count)
        {
            alignment = null!;
            failure = Failure(bi, ti, bi < b.Count ? "the translated script ends early" : "the translated script has "
                + "extra instructions that are not text");
            return false;
        }

        var disagreements = new List<TargetDisagreement>();
        for (int k = 0; k < b.Count; k++)
        {
            Instruction source = b[k], target = t[map[k]];
            for (int a = 0; a < source.Args.Count; a++)
            {
                Operand x = source.Args[a], y = target.Args[a];
                if (x.Type != 0 || !IsCodeOffset(source.Opcode, a, contracts) || x.Value == y.Value) continue;
                long expected = baseScript.IndexByOffset.TryGetValue((int)x.Value, out int index) && map[index] >= 0
                    ? t[map[index]].Offset
                    : -1;
                if (expected != y.Value)
                    disagreements.Add(new TargetDisagreement(source.Offset, table.Label(source.Opcode), a + 1, x.Value,
                        y.Value, expected));
            }
        }
        alignment = new ScriptAlignment(map, inserted, disagreements);
        failure = null;
        return true;

        AlignmentFailure Failure(int at, int translatedAt, string why) => new(
            at,
            at < b.Count ? b[at].Offset : -1,
            at < b.Count ? table.Label(b[at].Opcode) : "(end)",
            translatedAt,
            translatedAt < t.Count ? t[translatedAt].Offset : -1,
            translatedAt < t.Count ? table.Label(t[translatedAt].Opcode) : "(end)",
            why);
    }

    /// <summary>The dword offsets control flow can reach other than by falling through: immediate code targets and
    /// code values, and the script's call tables.</summary>
    public static IReadOnlySet<int> BranchTargets(Ome.Engine.Model.Script script, OpcodeContracts contracts)
    {
        var targets = new HashSet<int>();
        foreach (Instruction instruction in script.Instructions)
            for (int a = 0; a < instruction.Args.Count; a++)
            {
                Operand operand = instruction.Args[a];
                if (operand.Type == 0 && (contracts.IsCodeTarget(instruction.Opcode, a + 1)
                                          || contracts.IsCodeValue(instruction.Opcode, a + 1))
                    && operand.Value is >= 0 and <= int.MaxValue)
                    targets.Add((int)operand.Value);
            }
        targets.UnionWith(script.ScriptCallOffsets);
        targets.UnionWith(script.LocalCallOffsets);
        return targets;
    }

    private const int ArrayOpcode = 0x64;

    private static bool IsCodeOffset(int opcode, int argument, OpcodeContracts contracts)
        => contracts.IsCodeTarget(opcode, argument + 1) || contracts.IsCodeValue(opcode, argument + 1);

    private static bool Same(Instruction b, Instruction t, OpcodeContracts contracts, out string reason)
    {
        if (b.Opcode != t.Opcode)
        {
            reason = "different opcodes";
            return false;
        }
        if (b.Args.Count != t.Args.Count)
        {
            reason = "different operand counts";
            return false;
        }
        for (int a = 0; a < b.Args.Count; a++)
        {
            Operand x = b.Args[a], y = t.Args[a];
            if (x.Type != y.Type)
            {
                reason = $"operand {a + 1} has a different type";
                return false;
            }
            if (x.Type == TextOpcodes.LiteralOperandType) continue;
            if (x.Type == 0 && (IsCodeOffset(b.Opcode, a, contracts) || b.Opcode == ArrayOpcode && a == 1)) continue;
            if (x.Value != y.Value)
            {
                reason = $"operand {a + 1} differs";
                return false;
            }
        }
        reason = "";
        return true;
    }
}
