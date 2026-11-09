namespace Ome.Engine.Model;

/// <summary>
/// AGE's volume curve. Every native audio path converts a basis-point level (0..10000) to a DirectSound or
/// DirectShow attenuation with the same expression: <c>(ln(v) × 14.427 − 132.88) × 100</c> millibels, truncated,
/// with level 0 mapped to −10000 (silence). That is <c>1000 × log2(v / 10000)</c> mB, so each halving of the level
/// costs 10 dB, not the 6 dB of a linear amplitude scale. Sites: the sound manager's per-channel apply at
/// 0x4830d0 (SFX 0..9, voice 12; level = master × channel / 10000), the BGM DirectShow apply at 0x4610d0
/// (IBasicAudio::put_Volume) and movie_audio_apply_volume@0x462610.
/// </summary>
public static class NativeAudioVolume
{
    /// <summary>DSBVOLUME_MIN / IBasicAudio's floor: −10000 mB.</summary>
    public const int SilentMillibels = -10_000;

    /// <summary>Native attenuation in millibels for a level in basis points. Levels below 10 would compute
    /// past −10000 mB, which DirectSound and DirectShow reject (the previous volume stays); the port clamps them
    /// to silence instead.</summary>
    public static int ToMillibels(int basisPoints)
    {
        if (basisPoints <= 0) return SilentMillibels;
        int level = Math.Min(basisPoints, AudioMixerSettings.MaximumVolume);
        double mb = (Math.Log(level) * 14.427 - 132.88) * 100.0;
        return Math.Max(SilentMillibels, (int)mb);   // crt_ftol2 truncates toward zero
    }

    /// <summary>Native attenuation in decibels.</summary>
    public static float ToDecibels(int basisPoints) => ToMillibels(basisPoints) / 100.0f;

    /// <summary>Native interpolation of a BGM fade (bgm_fade_tick@0x464960): progress 0..100 points between the
    /// committed level and the target, in integer basis points.</summary>
    public static int InterpolateFade(int from, int to, int progressPoints)
    {
        int p = Math.Clamp(progressPoints, 0, 100);
        return ((100 - p) * from + to * p) / 100;
    }

    /// <summary>Steps of an op-0xc2 fade: 100 for durations of at least 1000 ms, otherwise 10.</summary>
    public static int FadeSteps(long durationMs) => durationMs >= 1000 ? 100 : 10;
}
