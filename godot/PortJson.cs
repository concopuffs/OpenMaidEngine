using System.Collections.Generic;
using System.Text.Json.Serialization;
using Ome.Engine.Diagnostics;
using Ome.Engine.Model;
using Ome.Engine.Natives;

/// <summary>
/// The JSON the port itself reads and writes, as source-generated metadata: the engine ships compiled with NativeAOT,
/// where reflection-based serialization is off. A site makes its own instance with the options it uses.
/// </summary>
[JsonSerializable(typeof(StallDiagnostic))]
[JsonSerializable(typeof(PortableTextRenderingPolicy))]
internal sealed partial class PortJsonContext : JsonSerializerContext;

/// <summary>A diagnostic snapshot (F6, a worker failure, a halt, the step limit), written with snake_case names.
/// <see cref="Waits"/> and <see cref="Gfx"/> are null when the engine thread did not answer in time.</summary>
internal sealed record StallDiagnostic(
    int FormatVersion, string CapturedUtc, int RenderFrame, long ClockMs, StallDiagnosticVm Vm,
    bool EngineThreadAnswered, WaitsDiagnosticSnapshot? Waits, GfxDiagnosticSnapshot? Gfx,
    IReadOnlyList<MoviePlayingDiagnostic>? ActiveMovies, IReadOnlyList<MovieOpenedDiagnostic>? PendingMovies);

/// <summary>The VM's part of a <see cref="StallDiagnostic"/>.</summary>
internal sealed record StallDiagnosticVm(
    long Steps, string? HaltReason, UnsupportedOpcodeDiagnostic? CompatibilityFailure, bool Done,
    GodotTraceSnapshot Trace);
