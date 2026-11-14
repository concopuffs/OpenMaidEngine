using System;

namespace Ome.Engine.Natives;

/// <summary>Why a frame is drawn again (<see cref="InputAndWaits"/>' frame turn). The perf log records the bits.</summary>
[Flags]
internal enum PresentationReason
{
    None = 0,
    Requested = 1,
    ScreenTransition = 2,
    RetainedMutation = 4,
    ContinuousChannel = 8,
    DiscreteSourceCell = 16,
}
