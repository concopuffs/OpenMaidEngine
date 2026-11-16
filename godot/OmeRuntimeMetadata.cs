using Ome.Engine.Model;
using Ome.Engine.Sys4;

/// <summary>
/// OME profile metadata that must travel with every Godot build. Repository discovery belongs to development tooling;
/// exported runtimes read the generator output embedded into <c>Ome.Engine</c>, the copy <c>Ome.Cli</c>'s
/// <c>omp</c> commands and the launcher link with too (launcher spec §5.9).
/// </summary>
internal static class OmeRuntimeMetadata
{
    internal static OpcodeTable LoadOpcodeTable(string abiId = "AGE-catalog") => OpcodeTableJson.LoadEmbedded(abiId);

    /// <summary>Operand access contracts from the same embedded opcode metadata (the mod linker needs them).</summary>
    internal static Ome.Script.Abi.OpcodeContracts LoadOpcodeContracts() => Ome.Script.Abi.OpcodeContracts.LoadEmbedded();
}
