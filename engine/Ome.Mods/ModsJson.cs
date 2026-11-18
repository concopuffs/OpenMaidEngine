using System.Text.Json.Serialization;
using Ome.Mods.Linking;
using Ome.Mods.Localization.Import;

namespace Ome.Mods;

/// <summary>
/// The JSON this assembly reads and writes, as source-generated metadata: the engine ships compiled with NativeAOT,
/// where reflection-based serialization is off. Each site makes its own instance with the options it writes with.
/// </summary>
[JsonSerializable(typeof(ModLinkManifest))]
[JsonSerializable(typeof(ImportReport))]
internal sealed partial class ModsJsonContext : JsonSerializerContext;
