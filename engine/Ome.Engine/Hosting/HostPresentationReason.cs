using System;

namespace Ome.Engine.Hosting;

/// <summary>Why a frame is drawn again (<see cref="Natives.InputAndWaits.ConsumePresentationReasons"/>).</summary>
[Flags]
public enum HostPresentationReason
{
    None = 0,
    HostRequest = 1,
    ScreenTransition = 2,
    RetainedMutation = 4,
    ContinuousChannel = 8,
    DiscreteSourceCell = 16,
}
