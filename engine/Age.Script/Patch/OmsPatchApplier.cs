using Age.Engine.Model;
using Age.Script.Abi;
using Age.Script.Compile;
using Age.Script.Ir;

namespace Age.Script.Patch;

/// <summary>A plan that does not fit its base, or plans that conflict with each other (spec §10.4).</summary>
public sealed class OmsPatchException(string message) : Exception(message);

public enum OmsPatchOrigin
{
    /// <summary>A base instruction.</summary>
    Base,
    /// <summary>A <c>before</c> body at its site.</summary>
    Before,
    /// <summary>An <c>after</c> body at its site.</summary>
    After,
    /// <summary>A region's replacement, at the region's first site.</summary>
    Region,
    /// <summary>A plan's appended code.</summary>
    Append,
}

/// <summary>Where a patched instruction came from: <see cref="BaseIndex"/> is the base instruction itself, or the
/// site its body is anchored at (−1 for appended code); <see cref="Plan"/> is the plan's load-order index (−1 for
/// the base).</summary>
public readonly record struct OmsPatchProvenance(OmsPatchOrigin Origin, int BaseIndex, int Plan);

/// <summary>A patched script with each instruction's provenance, the ordered interactions between plans and notes
/// about base table sites a region removed.</summary>
public sealed record OmsPatchResult(IrScript Script, byte[] Bytes, IReadOnlyList<OmsPatchProvenance> Provenance,
                                    IReadOnlyList<string> Interactions, IReadOnlyList<string> Notes);

/// <summary>
/// Applies transform plans to a pristine base script (spec §10.4): identity and validity checks, hard conflicts and
/// ordered interactions between plans, layout in load order, target resolution, and stable T1/T2/T3 tables.
/// </summary>
public static class OmsPatchApplier
{
    private static readonly (int Opcode, string Table, string Site)[] TableSites =
    [
        (0x71, "T1", "read-message"), (OmsCompiler.CallScript, "T2", "script-call"), (OmsCompiler.Call, "T3", "local-call"),
    ];

    /// <summary>Decodes <paramref name="baseBytes"/> and applies <paramref name="plans"/> in load order.</summary>
    public static OmsPatchResult Apply(byte[] baseBytes, string name, IReadOnlyList<OmsPatchPlan> plans,
                                       OpcodeTable table, OpcodeContracts contracts)
    {
        IrScript script = IrDecoder.Decode(baseBytes, table, contracts, name, out int literalCodeValues);
        OmsPatchResult result = Apply(script, OmsPatchPlan.FingerprintOf(baseBytes), plans, table);
        if (literalCodeValues > 0 && result.Provenance.Any(origin => origin.Origin != OmsPatchOrigin.Base))
            return result with
            {
                Notes = [.. result.Notes, $"{name} holds {literalCodeValues} proven code positions that are not instruction "
                                          + "starts; they stay numbers and do not move with the code"],
            };
        return result;
    }

    /// <summary>Whether control can continue to the next instruction: not a terminator, and not a conditional jump
    /// with both targets given.</summary>
    public static bool CanFallThrough(IrInstruction instruction)
        => !OmsCompiler.IsTerminator(instruction.Opcode)
           && !(instruction.Opcode == OmsCompiler.Jcc && instruction.Operands.Count == 3
                && instruction.Operands[1].Kind == IrOperandKind.CodeLabel
                && instruction.Operands[2].Kind == IrOperandKind.CodeLabel);

    public static OmsPatchResult Apply(IrScript baseScript, string fingerprint, IReadOnlyList<OmsPatchPlan> plans,
                                       OpcodeTable? table = null)
    {
        ArgumentNullException.ThrowIfNull(baseScript);
        ArgumentNullException.ThrowIfNull(plans);
        List<IrInstruction> code = baseScript.Code;
        int n = code.Count;
        var offsets = new int[n + 1];
        for (int k = 0; k < n; k++) offsets[k + 1] = offsets[k] + code[k].SizeInDwords;
        var indexOf = new Dictionary<int, int>(n);
        for (int k = 0; k < n; k++) indexOf[offsets[k]] = k;
        string Where(int index) => $"0x{offsets[index]:x}";

        var errors = new List<string>();
        var interactions = new List<string>();
        var notes = new List<string>();
        var before = new Dictionary<int, List<(int Plan, int Block)>>();
        var after = new Dictionary<int, List<(int Plan, int Block)>>();
        var regions = new SortedDictionary<int, (int Plan, int Through, int Block)>();
        var regionOf = new int[n];
        Array.Fill(regionOf, -1);
        var operands = new Dictionary<(int Index, int Operand), (int Plan, OmsPatchOperand Value)>();
        // Every base site a plan's code or operand values name: (plan, base index, what names it).
        var siteReferences = new List<(int Plan, int Index, string What)>();

        for (int p = 0; p < plans.Count; p++)
        {
            OmsPatchPlan plan = plans[p];
            string who = $"plan {p + 1} ({plan.Script})";
            if (!string.Equals(plan.Script, baseScript.Name, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{who} targets {plan.Script}, not {baseScript.Name}");
                continue;
            }
            if (!string.Equals(plan.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{who} was compiled against {plan.Fingerprint}; {baseScript.Name} is {fingerprint}");
                continue;
            }
            if (table != null && !string.Equals(plan.Abi, table.AbiId, StringComparison.Ordinal))
                errors.Add($"{who} targets ABI {plan.Abi}, not {table.AbiId}");
            if (plan.LocalCounts.Length != IrScript.LocalBankCount)
                errors.Add($"{who} lists {plan.LocalCounts.Length} local counts, not {IrScript.LocalBankCount}");

            int? Site(int offset, string what)
            {
                if (indexOf.TryGetValue(offset, out int index)) return index;
                errors.Add($"{who}: {what} 0x{offset:x} is not an instruction start in {baseScript.Name}");
                return null;
            }
            bool ValidOperand(OmsPatchOperand operand, string what)
            {
                switch (operand.Kind)
                {
                    case OmsPatchOperandKind.Data when operand.Value >= plan.Data.Count:
                        errors.Add($"{who}: {what} names data item {operand.Value}, which the plan lacks");
                        return false;
                    case OmsPatchOperandKind.Label when operand.Block < 0 || operand.Block >= plan.Blocks.Count
                                                        || operand.Value > plan.Blocks[operand.Block].Count
                                                        || operand.Block == plan.Append
                                                           && operand.Value == plan.Blocks[operand.Block].Count:
                        errors.Add($"{who}: {what} names position {operand.Value} of block {operand.Block}, which the plan lacks");
                        return false;
                    case OmsPatchOperandKind.Resource:
                        errors.Add($"{who}: {what} is the unresolved resource reference "
                                   + (operand.Value < plan.Resources.Count ? $"'{plan.Resources[(int)operand.Value]}'" : $"#{operand.Value}")
                                   + "; the linker resolves it before applying");
                        return false;
                    case OmsPatchOperandKind.Site:
                        if (Site((int)operand.Value, what) is not int index) return false;
                        siteReferences.Add((p, index, what));
                        return true;
                    default:
                        return true;
                }
            }

            var owners = new Dictionary<int, string>();
            int? Block(int? block, string what)
            {
                if (block is not int b || b < 0 || b >= plan.Blocks.Count)
                {
                    errors.Add($"{who}: {what} names block {block?.ToString() ?? "none"}, which the plan lacks");
                    return null;
                }
                if (!owners.TryAdd(b, what)) errors.Add($"{who}: block {b} belongs to both {owners[b]} and {what}");
                return b;
            }

            for (int b = 0; b < plan.Blocks.Count; b++)
                foreach (OmsPatchInstruction instruction in plan.Blocks[b])
                {
                    if (table != null && table.Argc(instruction.Opcode) is int argc && argc != instruction.Operands.Count)
                        errors.Add($"{who}: block {b} has opcode 0x{instruction.Opcode:x} with {instruction.Operands.Count} operands; "
                                   + (argc < 0 ? $"{table.AbiId} does not install it" : $"it takes {argc}"));
                    foreach (OmsPatchOperand operand in instruction.Operands) ValidOperand(operand, $"block {b}");
                }

            foreach (OmsPatchOperation operation in plan.Operations)
            {
                string what = $"{operation.Kind.ToString().ToLowerInvariant()} site 0x{operation.Site:x}"
                              + (operation.Source != null ? $" ({operation.Source})" : "");
                if (Site(operation.Site, "site") is not int s) continue;
                switch (operation.Kind)
                {
                    case OmsPatchOperationKind.Before or OmsPatchOperationKind.After:
                    {
                        if (Block(operation.Block, what) is not int b) break;
                        if (operation.Kind == OmsPatchOperationKind.After && !CanFallThrough(code[s]))
                        {
                            errors.Add($"{who}: {what}: the instruction there (opcode 0x{code[s].Opcode:x}) never falls through");
                            break;
                        }
                        var inserts = operation.Kind == OmsPatchOperationKind.Before ? before : after;
                        if (!inserts.TryGetValue(s, out var list)) inserts[s] = list = new();
                        list.Add((p, b));
                        break;
                    }
                    case OmsPatchOperationKind.Replace:
                    {
                        if (Block(operation.Block, what) is not int b) break;
                        if (Site(operation.Through ?? operation.Site, "region end") is not int e) break;
                        if (e < s)
                        {
                            errors.Add($"{who}: {what} ends at 0x{operation.Through:x}, before it starts");
                            break;
                        }
                        int overlap = Enumerable.Range(s, e - s + 1).FirstOrDefault(k => regionOf[k] >= 0, -1);
                        if (overlap >= 0)
                        {
                            var other = regions[regionOf[overlap]];
                            errors.Add($"conflict: {who} {what} overlaps the region plan {other.Plan + 1} replaces at "
                                       + $"{Where(regionOf[overlap])} (both cover {Where(overlap)})");
                            break;
                        }
                        for (int k = s; k <= e; k++) regionOf[k] = s;
                        regions[s] = (p, e, b);
                        break;
                    }
                    case OmsPatchOperationKind.Operand:
                    {
                        if (operation.Operand is not int number || number < 1 || number > code[s].Operands.Count)
                        {
                            errors.Add($"{who}: {what}: the instruction there has {code[s].Operands.Count} operands, "
                                       + $"not an operand {operation.Operand?.ToString() ?? "(none)"}");
                            break;
                        }
                        if (operation.Value is not OmsPatchOperand value)
                        {
                            errors.Add($"{who}: {what} has no value");
                            break;
                        }
                        if (!ValidOperand(value, what)) break;
                        bool wasCode = code[s].Operands[number - 1].Kind == IrOperandKind.CodeLabel;
                        bool isCode = value.Kind is OmsPatchOperandKind.Label or OmsPatchOperandKind.Site;
                        if (wasCode != isCode)
                        {
                            errors.Add($"{who}: {what}: operand {number} is {(wasCode ? "a code position and takes a label" : "not a code position and takes no label")}");
                            break;
                        }
                        if (operands.TryGetValue((s, number), out var earlier))
                        {
                            errors.Add(earlier.Plan == p
                                ? $"{who} replaces operand {number} at {Where(s)} twice"
                                : $"conflict: plans {earlier.Plan + 1} and {p + 1} both replace operand {number} at {Where(s)}");
                            break;
                        }
                        operands[(s, number)] = (p, value);
                        break;
                    }
                }
            }

            if (plan.Append is int append && Block(append, "the appended code") is int a && plan.Blocks[a].Count > 0
                && !OmsCompiler.IsTerminator(plan.Blocks[a][^1].Opcode))
                errors.Add($"{who}: its appended code can fall off its end");
        }

        // Regions against everything else.
        var incoming = new List<(int From, int To)>();
        for (int k = 0; k < n; k++)
            foreach (IrOperand operand in code[k].Operands)
                if (operand.Kind == IrOperandKind.CodeLabel) incoming.Add((k, (int)operand.Value));
        foreach (var (s, (owner, e, _)) in regions)
        {
            foreach (var (from, to) in incoming)
                if (to > s && to <= e && (from < s || from > e))
                    errors.Add($"plan {owner + 1} replaces {Where(s)} through {Where(e)}, but base instruction {Where(from)} "
                               + $"transfers to {Where(to)} inside it");
            for (int k = s; k <= e; k++)
            {
                foreach (var (kind, inserts) in new[] { ("before", before), ("after", after) })
                    if (inserts.TryGetValue(k, out var list))
                        foreach (var (plan, _) in list)
                            errors.Add($"conflict: plan {plan + 1} inserts {kind} {Where(k)}, inside the region plan {owner + 1} "
                                       + $"replaces at {Where(s)}");
                foreach (var ((index, number), (plan, _)) in operands)
                    if (index == k)
                        errors.Add($"conflict: plan {plan + 1} replaces operand {number} at {Where(k)}, inside the region "
                                   + $"plan {owner + 1} replaces at {Where(s)}");
            }
            foreach (var (plan, index, what) in siteReferences)
                if (index >= s && index <= e && !(plan == owner && index == s))
                    errors.Add(plan == owner
                        ? $"plan {plan + 1}: {what} names {Where(index)}, inside the region it replaces at {Where(s)}"
                        : $"conflict: plan {plan + 1}: {what} names {Where(index)}, inside the region plan {owner + 1} replaces "
                          + $"at {Where(s)}");
        }
        bool appends = plans.Any(plan => plan.Append is int a && a >= 0 && a < plan.Blocks.Count && plan.Blocks[a].Count > 0);
        if (appends && n > 0 && (CanFallThrough(code[n - 1]) || incoming.Any(pair => pair.To == n)))
            errors.Add($"{baseScript.Name} can reach the end of its code, where appended code would run");
        if (errors.Count > 0) throw new OmsPatchException(string.Join(Environment.NewLine, errors.Distinct()));

        foreach (var (kind, inserts) in new[] { ("before", before), ("after", after) })
            foreach (var (k, list) in inserts.OrderBy(pair => pair.Key))
                if (list.Select(insert => insert.Plan).Distinct().Count() > 1)
                    interactions.Add($"plans {string.Join(", ", list.Select(insert => insert.Plan + 1))} insert {kind} {Where(k)}, "
                                     + "in that order");

        // Layout (step 4).
        var output = new List<IrInstruction?>(n + 16);
        var provenance = new List<OmsPatchProvenance>(n + 16);
        var position = new int[n + 1];
        var newIndex = new int[n];
        Array.Fill(newIndex, -1);
        var blockStart = plans.Select(plan => Enumerable.Repeat(-1, plan.Blocks.Count).ToArray()).ToArray();
        var segments = new List<(int Plan, int Block)>();
        void Place(int plan, int block, OmsPatchOrigin origin, int anchor)
        {
            blockStart[plan][block] = output.Count;
            segments.Add((plan, block));
            foreach (OmsPatchInstruction _ in plans[plan].Blocks[block])
            {
                output.Add(null);
                provenance.Add(new OmsPatchProvenance(origin, anchor, plan));
            }
        }
        for (int k = 0; k < n; k++)
        {
            position[k] = output.Count;
            foreach (var (plan, block) in before.GetValueOrDefault(k) ?? []) Place(plan, block, OmsPatchOrigin.Before, k);
            if (regions.TryGetValue(k, out var region))
            {
                Place(region.Plan, region.Block, OmsPatchOrigin.Region, k);
                for (int j = k + 1; j <= region.Through; j++) position[j] = position[k];
                k = region.Through;
                continue;
            }
            newIndex[k] = output.Count;
            output.Add(null);
            provenance.Add(new OmsPatchProvenance(OmsPatchOrigin.Base, k, -1));
            foreach (var (plan, block) in after.GetValueOrDefault(k) ?? []) Place(plan, block, OmsPatchOrigin.After, k);
        }
        position[n] = output.Count;
        for (int p = 0; p < plans.Count; p++)
            if (plans[p].Append is int append && plans[p].Blocks[append].Count > 0)
                Place(p, append, OmsPatchOrigin.Append, -1);

        // Targets (step 5) and data (step 6).
        var dataStart = new int[plans.Count];
        for (int p = 0, next = baseScript.Data.Count; p < plans.Count; next += plans[p].Data.Count, p++) dataStart[p] = next;
        IrOperand Resolve(OmsPatchOperand operand, int plan) => operand.Kind switch
        {
            OmsPatchOperandKind.Literal => IrOperand.Literal(operand.Type, operand.Value),
            OmsPatchOperandKind.Data => IrOperand.Data(operand.Type, dataStart[plan] + (int)operand.Value),
            OmsPatchOperandKind.Label => IrOperand.Label(operand.Type, blockStart[plan][operand.Block] + (int)operand.Value),
            OmsPatchOperandKind.Site => IrOperand.Label(operand.Type, position[indexOf[(int)operand.Value]]),
            _ => throw new InvalidOperationException($"unknown operand kind {operand.Kind}"),
        };
        for (int k = 0; k < n; k++)
        {
            if (newIndex[k] < 0) continue;
            IrInstruction instruction = code[k];
            var moved = new IrOperand[instruction.Operands.Count];
            for (int i = 0; i < moved.Length; i++)
            {
                IrOperand operand = instruction.Operands[i];
                moved[i] = operands.TryGetValue((k, i + 1), out var replacement) ? Resolve(replacement.Value, replacement.Plan)
                    : operand.Kind == IrOperandKind.CodeLabel ? IrOperand.Label(operand.Type, position[(int)operand.Value])
                    : operand;
            }
            output[newIndex[k]] = new IrInstruction(instruction.Opcode, moved, instruction.OriginalOffset ?? offsets[k]);
        }
        foreach (var (plan, block) in segments)
        {
            List<OmsPatchInstruction> instructions = plans[plan].Blocks[block];
            for (int i = 0; i < instructions.Count; i++)
                output[blockStart[plan][block] + i] = new IrInstruction(instructions[i].Opcode,
                    instructions[i].Operands.Select(operand => Resolve(operand, plan)).ToArray());
        }
        List<IrInstruction> patched = output.Select(instruction => instruction!).ToList();
        var data = new List<IrDataItem>(baseScript.Data);
        foreach (OmsPatchPlan plan in plans) data.AddRange(plan.Data.Select(item => item.ToIr()));
        // A base item whose every reference a patch removed is dropped: readers that find the end of the code at the
        // first referenced item (the port's loader) would otherwise decode it as code. Items the base itself never
        // references stay.
        var referencedBefore = new HashSet<int>(code.SelectMany(instruction => instruction.Operands)
            .Where(operand => operand.Kind == IrOperandKind.DataRef).Select(operand => (int)operand.Value));
        var referencedAfter = new HashSet<int>(patched.SelectMany(instruction => instruction.Operands)
            .Where(operand => operand.Kind == IrOperandKind.DataRef).Select(operand => (int)operand.Value));
        if (referencedBefore.Any(item => !referencedAfter.Contains(item)))
        {
            var renumbered = new int[data.Count];
            var kept = new List<IrDataItem>(data.Count);
            for (int i = 0; i < data.Count; i++)
            {
                renumbered[i] = kept.Count;
                if (i < baseScript.Data.Count && referencedBefore.Contains(i) && !referencedAfter.Contains(i)) continue;
                kept.Add(data[i]);
            }
            data = kept;
            Renumber(patched, renumbered);
            // Such readers recognize the code's end only by a string operand or an inline array (op 0x64's second
            // operand). Items in front of the first such item, which the base kept behind a dropped one, move to the end.
            var recognized = new HashSet<int>();
            foreach (IrInstruction instruction in patched)
                for (int i = 0; i < instruction.Operands.Count; i++)
                    if (instruction.Operands[i] is { Kind: IrOperandKind.DataRef } operand
                        && (operand.Type == 2 || instruction.Opcode == 0x64 && i == 1))
                        recognized.Add((int)operand.Value);
            int first = Enumerable.Range(0, data.Count).FirstOrDefault(recognized.Contains, 0);
            if (first > 0)
            {
                var order = Enumerable.Range(first, data.Count - first).Concat(Enumerable.Range(0, first)).ToArray();
                var moved = new int[data.Count];
                for (int i = 0; i < order.Length; i++) moved[order[i]] = i;
                data = order.Select(i => data[i]).ToList();
                Renumber(patched, moved);
            }
        }
        int[] localCounts = baseScript.LocalCounts.ToArray();
        foreach (OmsPatchPlan plan in plans)
            for (int bank = 0; bank < localCounts.Length; bank++)
                localCounts[bank] = Math.Max(localCounts[bank], plan.LocalCounts[bank]);

        // Stable tables (step 7).
        var newOffsets = new int[patched.Count + 1];
        for (int j = 0; j < patched.Count; j++) newOffsets[j + 1] = newOffsets[j] + patched[j].SizeInDwords;
        IReadOnlyList<uint>?[] baseTables =
            [baseScript.Tables.ReadMessageSites, baseScript.Tables.ScriptCallSites, baseScript.Tables.LocalCallSites];
        var tables = new uint[3][];
        bool derived = true;
        for (int t = 0; t < 3; t++)
        {
            var (opcode, name, site) = TableSites[t];
            IEnumerable<int> entries = baseTables[t] is { } explicitEntries
                ? explicitEntries.Select(entry => indexOf.TryGetValue((int)entry, out int index) ? index
                    : throw new OmsPatchException($"{baseScript.Name}: {name} entry 0x{entry:x} is not an instruction start"))
                : Enumerable.Range(0, n).Where(k => code[k].Opcode == opcode);
            var stable = new List<uint>();
            foreach (int k in entries)
            {
                if (newIndex[k] < 0)
                    notes.Add($"the region replaced at {Where(regionOf[k])} removes {site} site {Where(k)}; its {name} slot "
                              + $"{stable.Count} now points at the replacement");
                stable.Add((uint)newOffsets[newIndex[k] >= 0 ? newIndex[k] : position[k]]);
            }
            // An inserted site gets a new slot after the base's, unless a removed base site's slot already points at it
            // (a region whose replacement starts with the instruction it replaces).
            var slotted = new HashSet<uint>(stable);
            for (int j = 0; j < patched.Count; j++)
                if (provenance[j].Origin != OmsPatchOrigin.Base && patched[j].Opcode == opcode && slotted.Add((uint)newOffsets[j]))
                    stable.Add((uint)newOffsets[j]);
            tables[t] = stable.ToArray();
            derived &= stable.SequenceEqual(Enumerable.Range(0, patched.Count).Where(j => patched[j].Opcode == opcode)
                .Select(j => (uint)newOffsets[j]));
        }

        var result = new IrScript
        {
            Name = baseScript.Name,
            Magic = baseScript.Magic,
            LocalCounts = localCounts,
            Field6 = baseScript.Field6,
            Code = patched,
            Data = data,
            Tables = derived ? IrTables.Derived : new IrTables(tables[0], tables[1], tables[2]),
        };
        return new OmsPatchResult(result, IrEncoder.Encode(result), provenance, interactions, notes);
    }

    private static void Renumber(List<IrInstruction> code, int[] item)
    {
        for (int j = 0; j < code.Count; j++)
            if (code[j].Operands.Any(operand => operand.Kind == IrOperandKind.DataRef))
                code[j] = code[j] with
                {
                    Operands = code[j].Operands.Select(operand => operand.Kind == IrOperandKind.DataRef
                        ? IrOperand.Data(operand.Type, item[operand.Value]) : operand).ToArray(),
                };
    }
}
