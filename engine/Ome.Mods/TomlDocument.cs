using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Serialization;

namespace Ome.Mods;

/// <summary>
/// Parses TOML text into Tomlyn's document model, the one form every reader in this assembly takes. The engine ships
/// compiled with NativeAOT, where Tomlyn's reflection serializer is off, so the parse goes through a source-generated
/// context (<see cref="TomlDocumentContext"/>).
/// </summary>
internal static class TomlDocument
{
    /// <summary>The document's root table, or null where Tomlyn's serializer gives none. Throws
    /// <see cref="TomlException"/>.</summary>
    public static TomlTable? Parse(string text) => TomlSerializer.Deserialize(text, TomlDocumentContext.Default.TomlTable);
}

[TomlSerializable(typeof(TomlTable))]
internal sealed partial class TomlDocumentContext : TomlSerializerContext;
