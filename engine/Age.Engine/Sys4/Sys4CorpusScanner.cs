using Age.Engine.Model;

namespace Age.Engine.Sys4;

public sealed record Sys4CorpusScanFailure(long PackedId, string Name, string Error);

/// <summary>Decode coverage over the runtime catalog/store path, including append mounts and loose overrides.</summary>
public sealed record Sys4CorpusScanResult(
    int ScriptCount,
    int BaseScriptCount,
    int AppendScriptCount,
    long InstructionCount,
    IReadOnlyDictionary<int, long> OpcodeOccurrences,
    IReadOnlyList<Sys4CorpusScanFailure> Failures)
{
    /// <summary>Per opcode, one operand-type histogram per operand position (type tag -> occurrences).
    /// Each position's counts sum to that opcode's occurrence count.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<IReadOnlyDictionary<int, long>>> OperandTypeOccurrences
    { get; init; } = new Dictionary<int, IReadOnlyList<IReadOnlyDictionary<int, long>>>();
}

public static class Sys4CorpusScanner
{
    public static Sys4CorpusScanResult Scan(
        Sys4AssetCatalog catalog,
        Sys4ScriptProvider provider)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(provider);

        int scripts = 0, baseScripts = 0, appendScripts = 0;
        long instructions = 0;
        var occurrences = new SortedDictionary<int, long>();
        var operandTypes = new SortedDictionary<int, SortedDictionary<int, long>[]>();
        var failures = new List<Sys4CorpusScanFailure>();
        foreach (PackedAssetEntry packed in catalog.EnumerateScripts())
        {
            scripts++;
            if ((packed.PackedId >> 24) == 0) baseScripts++;
            else appendScripts++;
            try
            {
                Script script = provider.GetById(packed.PackedId)
                    ?? throw new InvalidDataException("catalog script did not resolve through provider");
                instructions += script.Instructions.Count;
                foreach (Instruction instruction in script.Instructions)
                {
                    occurrences[instruction.Opcode] =
                        occurrences.GetValueOrDefault(instruction.Opcode) + 1;
                    if (!operandTypes.TryGetValue(instruction.Opcode, out var positions))
                    {
                        positions = Enumerable.Range(0, instruction.Args.Count)
                            .Select(_ => new SortedDictionary<int, long>()).ToArray();
                        operandTypes[instruction.Opcode] = positions;
                    }
                    for (int a = 0; a < instruction.Args.Count; a++)
                        positions[a][instruction.Args[a].Type] =
                            positions[a].GetValueOrDefault(instruction.Args[a].Type) + 1;
                }
            }
            catch (Exception error) when (
                error is IOException or InvalidDataException or ArgumentException or OverflowException)
            {
                failures.Add(new Sys4CorpusScanFailure(
                    packed.PackedId, packed.Asset.Name, error.Message));
            }
        }
        return new Sys4CorpusScanResult(
            scripts, baseScripts, appendScripts, instructions, occurrences, failures)
        {
            OperandTypeOccurrences = operandTypes.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<IReadOnlyDictionary<int, long>>)pair.Value
                    .Select(position => (IReadOnlyDictionary<int, long>)position).ToArray()),
        };
    }
}
