namespace Age.Engine.Hosting;

/// <summary>Host-owned virtual clock + per-frame op budget. Pure (no threading): the Godot host
/// advances it once per rendered frame and consults it to pace the VM. The one <see cref="Speed"/>
/// factor is the future (unwired) Ctrl fast-forward multiplier — scaling it scales the throttle
/// budget, sleeps, and the anim tween together. See docs/superpowers/specs/2026-07-08-frame-stepped-vm-design.md.</summary>
public sealed class FrameClock
{
    /// <summary>Monotonic virtual time in milliseconds (scaled by Speed).</summary>
    public long NowMs { get; private set; }

    /// <summary>Speed multiplier. 1.0 = normal. The future Ctrl hook (ADV-scoped); leave at 1.0 for now.</summary>
    public double Speed = 1.0;

    /// <summary>Base per-frame interpreter op budget (tunable by eye; ~30 ≈ 1,800 ops/sec at 60fps).</summary>
    public int OpsPerFrame = 30;

    /// <summary>Advance the clock by one rendered frame's real delta (seconds), scaled by Speed.</summary>
    public void Advance(double realDeltaSeconds) => NowMs += (long)(realDeltaSeconds * 1000.0 * Speed);

    /// <summary>Ops the VM may run before yielding a frame, scaled by Speed (min 1).</summary>
    public int EffectiveBudget => System.Math.Max(1, (int)System.Math.Round(OpsPerFrame * Speed));
}
