using System.Text.Json.Serialization;
using Ome.Script.Layout;
using Ome.Script.Patch;

namespace Ome.Script;

/// <summary>
/// The JSON this assembly reads and writes, as source-generated metadata: the engine ships compiled with NativeAOT,
/// where reflection-based serialization is off. A site with options of its own makes its own instance with them.
/// </summary>
[JsonSerializable(typeof(OmsLayout))]
[JsonSerializable(typeof(OmsPatchPlan))]
[JsonSerializable(typeof(OmsPatchPoints.Document))]
internal sealed partial class ScriptJsonContext : JsonSerializerContext;
