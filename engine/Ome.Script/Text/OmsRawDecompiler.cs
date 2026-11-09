using System.Globalization;
using System.Text;
using Ome.Script.Compile;
using Ome.Script.Ir;
using Ome.Script.Layout;
using Ome.Script.Syntax;

namespace Ome.Script.Text;

/// <summary>Source text plus the layout sidecar needed to recompile it to the original bytes. At the core level,
/// <see cref="FoldedInstructions"/> counts instructions folded into expressions and <see cref="FoldRejections"/> the
/// folds that lowering did not reproduce.</summary>
/// <summary>Decompiled source and its sidecar. At the idiomatic level <see cref="LibrarySubs"/> counts recognized
/// library subs, <see cref="TemplateLifts"/> lifted template uses and <see cref="LiftedInstructions"/> the instructions
/// inside either, by module (spec §12 gate 5); <see cref="LiftFailure"/> says why lifting was abandoned for core
/// source, when it was.</summary>
public sealed record OmsDecompilation(string Source, OmsLayout Layout, int FoldedInstructions = 0, int FoldRejections = 0,
                                      int LibrarySubs = 0, int TemplateLifts = 0, string? LiftFailure = null,
                                      IReadOnlyDictionary<string, int>? LiftedInstructions = null, uint? PoolBase = null);

/// <summary>
/// Raw-level decompiler (spec §11, level <c>raw</c>): every instruction becomes an <c>@op</c> statement with
/// labels, storage cells and inline string/array literals. The sidecar records only non-canonical layout.
/// </summary>
public static class OmsRawDecompiler
{
    public static OmsDecompilation Decompile(IrScript script, string abiId)
    {
        ArgumentNullException.ThrowIfNull(script);
        int[] offsets = new int[script.Code.Count + 1];
        for (int k = 0; k < script.Code.Count; k++) offsets[k + 1] = offsets[k] + script.Code[k].SizeInDwords;
        var labelTargets = new SortedSet<int>(script.Code.SelectMany(i => i.Operands)
            .Where(o => o.Kind == IrOperandKind.CodeLabel).Select(o => (int)o.Value));
        string LabelName(int index) => index >= script.Code.Count
            ? "label_end" : $"label_{script.Code[index].OriginalOffset ?? offsets[index]:x}";

        var text = new StringBuilder();
        text.Append("oms 1\n");
        text.Append("script ").Append(Quote(script.Name)).Append('\n');
        if (script.Code.Count > 0) text.Append('\n');
        for (int k = 0; k < script.Code.Count; k++)
        {
            if (labelTargets.Contains(k)) text.Append(LabelName(k)).Append(":\n");
            IrInstruction instruction = script.Code[k];
            text.Append("    @op(0x").Append(instruction.Opcode.ToString("x", CultureInfo.InvariantCulture));
            foreach (IrOperand operand in instruction.Operands)
                text.Append(", ").Append(Operand(operand, script, LabelName));
            text.Append(")\n");
        }
        if (labelTargets.Contains(script.Code.Count)) text.Append(LabelName(script.Code.Count)).Append(":\n");

        return new OmsDecompilation(text.ToString(), BuildLayout(script, abiId));
    }

    /// <summary>Formats one operand in the explicit raw forms of spec §6.2 (shared by every decompiler level).</summary>
    public static string Operand(IrOperand operand, IrScript script, Func<int, string> labelName)
    {
        string body;
        int defaultType;
        switch (operand.Kind)
        {
            case IrOperandKind.CodeLabel:
                body = labelName((int)operand.Value);
                defaultType = 0;
                break;
            case IrOperandKind.DataRef:
                IrDataItem item = script.Data[(int)operand.Value];
                (body, defaultType) = item.Kind switch
                {
                    IrDataKind.String => (Quote(item.Text ?? ""), 2),
                    IrDataKind.IntArray => ("[" + string.Join(", ", item.Dwords.Skip(1).Select(d => SignedText(d))) + "]", 0),
                    _ => ("data(" + string.Join(", ", item.Dwords.Select(d => $"0x{d:x}")) + ")", 0),
                };
                break;
            default:
                if (StorageBanks.BankByType.TryGetValue(operand.Type, out string? bank))
                    return $"{bank}[0x{operand.Value:x}]";
                if (operand.Type == 1)
                {
                    float value = BitConverter.UInt32BitsToSingle(operand.Value);
                    string printed = value.ToString("R", CultureInfo.InvariantCulture);
                    if (float.IsFinite(value) && !printed.Contains('E')
                        && BitConverter.SingleToUInt32Bits(float.Parse(printed, CultureInfo.InvariantCulture)) == operand.Value)
                        return "float " + (printed.Contains('.') ? printed : printed + ".0");
                }
                body = operand.Value == 0xFFFFFFFF ? "-1" : $"0x{operand.Value:x}";
                defaultType = 0;
                break;
        }
        return operand.Type == defaultType ? body : $"type(0x{operand.Type:x}, {body})";
    }

    private static string SignedText(uint value) => unchecked((int)value) < 0
        ? unchecked((int)value).ToString(CultureInfo.InvariantCulture)
        : $"0x{value:x}";

    public static string Quote(string text)
    {
        var quoted = new StringBuilder("\"");
        foreach (char c in text)
            quoted.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\t' => "\\t",
                < ' ' => $"\\u{(int)c:x4}",
                _ => c.ToString(),
            });
        return quoted.Append('"').ToString();
    }

    /// <summary>The sidecar for an IR script: only the facts a canonical compile would not reproduce.</summary>
    public static OmsLayout BuildLayout(IrScript script, string abiId)
    {
        var layout = new OmsLayout();
        if (!script.Magic.AsSpan().SequenceEqual(OmsCanonical.MagicFor(abiId)))
            layout.Magic = Encoding.ASCII.GetString(script.Magic);
        if (script.Field6 != OmsCanonical.Field6) layout.Field6 = script.Field6;
        if (!script.LocalCounts.SequenceEqual(OmsCanonical.LocalCounts(script.Code)))
            layout.LocalCounts = script.LocalCounts.ToArray();

        // Data-operand occurrences in code order, and the item each one references.
        var occurrenceItems = script.Code.SelectMany(i => i.Operands)
            .Where(o => o.Kind == IrOperandKind.DataRef).Select(o => (int)o.Value).ToList();
        bool canonical = occurrenceItems.Count == script.Data.Count
                         && occurrenceItems.Select((item, k) => item == k).All(same => same);
        var firstOccurrence = new Dictionary<int, int>();
        for (int k = 0; k < occurrenceItems.Count; k++) firstOccurrence.TryAdd(occurrenceItems[k], k);
        if (!canonical)
        {
            layout.Data = script.Data.Select((item, index) =>
            {
                int[] refs = occurrenceItems.Select((target, k) => (target, k))
                    .Where(pair => pair.target == index).Select(pair => pair.k).ToArray();
                return refs.Length > 0
                    ? new OmsDataSlot { Refs = refs }
                    : new OmsDataSlot { Raw = item.Dwords.ToArray() };
            }).ToList();
        }
        foreach (var (item, occurrence) in firstOccurrence)
        {
            IrDataItem data = script.Data[item];
            IReadOnlyList<uint>? canonicalDwords = data.Kind switch
            {
                IrDataKind.String => TryEncode(data.Text ?? ""),
                IrDataKind.IntArray => IrDataItem.FromInts(data.Dwords.Skip(1).Select(d => unchecked((int)d)).ToArray()).Dwords,
                _ => data.Dwords,
            };
            if (canonicalDwords == null || !canonicalDwords.SequenceEqual(data.Dwords))
                (layout.ItemDwords ??= new SortedDictionary<int, uint[]>())[occurrence] = data.Dwords.ToArray();
        }
        if (!script.Tables.IsDerived)
            layout.Tables =
            [
                script.Tables.ReadMessageSites!.ToArray(), script.Tables.ScriptCallSites!.ToArray(),
                script.Tables.LocalCallSites!.ToArray(),
            ];
        return layout;
    }

    private static IReadOnlyList<uint>? TryEncode(string text)
    {
        try { return IrDataItem.FromText(text).Dwords; }
        catch (EncoderFallbackException) { return null; }
    }
}
