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
    IReadOnlyList<Sys4CorpusScanFailure> Failures);

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
                    occurrences[instruction.Opcode] =
                        occurrences.GetValueOrDefault(instruction.Opcode) + 1;
            }
            catch (Exception error) when (
                error is IOException or InvalidDataException or ArgumentException or OverflowException)
            {
                failures.Add(new Sys4CorpusScanFailure(
                    packed.PackedId, packed.Asset.Name, error.Message));
            }
        }
        return new Sys4CorpusScanResult(
            scripts, baseScripts, appendScripts, instructions, occurrences, failures);
    }
}
