using System;
using System.IO;
using System.Reflection;
using Ome.Engine.Model;
using Ome.Engine.Sys4;

/// <summary>
/// OME profile metadata that must travel with every Godot build. Repository discovery belongs to
/// development tooling; exported runtimes read the generator output embedded into this assembly.
/// </summary>
internal static class OmeRuntimeMetadata
{
    internal const string OpcodeTableResourceName = "OME.Runtime.opcodes.json";

    internal static OpcodeTable LoadOpcodeTable(string abiId = "AGE-catalog")
    {
        Assembly assembly = typeof(OmeRuntimeMetadata).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(OpcodeTableResourceName)
            ?? throw new InvalidDataException(
                $"Embedded runtime metadata '{OpcodeTableResourceName}' is missing from " +
                $"{assembly.GetName().Name}.");
        return OpcodeTableJson.Load(stream, abiId);
    }

    /// <summary>Operand access contracts from the same embedded opcode metadata (the mod linker needs them).</summary>
    internal static Ome.Script.Abi.OpcodeContracts LoadOpcodeContracts()
    {
        Assembly assembly = typeof(OmeRuntimeMetadata).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(OpcodeTableResourceName)
            ?? throw new InvalidDataException($"Embedded runtime metadata '{OpcodeTableResourceName}' is missing.");
        return Ome.Script.Abi.OpcodeContracts.Load(stream);
    }
}
