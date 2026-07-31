using System;
using System.IO;
using System.Reflection;
using Age.Engine.Model;
using Age.Engine.Sys4;

/// <summary>
/// Himegari profile metadata that must travel with every Godot build. Repository discovery belongs to
/// development tooling; exported runtimes read the generator output embedded into this assembly.
/// </summary>
internal static class HimegariRuntimeMetadata
{
    internal const string OpcodeTableResourceName = "Himegari.Runtime.opcodes.json";

    internal static OpcodeTable LoadOpcodeTable()
    {
        Assembly assembly = typeof(HimegariRuntimeMetadata).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(OpcodeTableResourceName)
            ?? throw new InvalidDataException(
                $"Embedded runtime metadata '{OpcodeTableResourceName}' is missing from " +
                $"{assembly.GetName().Name}.");
        return OpcodeTableJson.Load(stream);
    }
}
