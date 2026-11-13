namespace Ome.Engine.Natives;

/// <summary>What <see cref="SkipAndAuto"/> reads besides its state: two of the profile's SYS4INI settings and the
/// fast-forward pace. The defaults are native's registered settings and no pacing (headless tools and tests).</summary>
/// <param name="CancelMessageSkipOnClick">Native set:CancelMesSkipOnClick (SYS4INI CANCELMESSKIPONCLICK).
/// Nonzero: a completed action-4 click during active fast-forward cancels Skip; 2 also runs CALLBACK_SETTING.BIN.</param>
/// <param name="CoexistMessageSkip">Native set:CoexistMesSkip (SYS4INI COEXISTMESSKIP, registered default 0). With
/// it off, the native menu's Skip toggle clears Auto and its Auto toggle clears Skip; the script ops ignore it.</param>
/// <param name="FastForwardOpMicroseconds">Wall-clock cost of one op while ADV fast-forward is active (native runs one
/// op per main-loop iteration). Zero, with <paramref name="FastForwardPageMicroseconds"/> zero, disables pacing;
/// interactive front ends use <see cref="NativeFastForwardOpMicroseconds"/>.</param>
/// <param name="FastForwardPageMicroseconds">Additional fast-forward cost of each text wait (op 0x72) passed while
/// fast-forward is active; interactive front ends use <see cref="NativeFastForwardPageMicroseconds"/>.</param>
public sealed record SkipAndAutoSettings(
    int CancelMessageSkipOnClick = 0,
    bool CoexistMessageSkip = false,
    int FastForwardOpMicroseconds = 0,
    int FastForwardPageMicroseconds = 0)
{
    /// <summary>Native fast-forward cost per op. Text waits timed with Frida under held Ctrl (2026-11-07) fit 15 µs
    /// per op plus <see cref="NativeFastForwardPageMicroseconds"/> per page: Kamidori's SC0000 (34,851 ops, 140 pages,
    /// 865 ms) and Himegari's opening pages (12 µs/op, 2.75 ms/page) agree (docs/engine-re.md, "Fast-forward
    /// pace").</summary>
    public const int NativeFastForwardOpMicroseconds = 15;

    /// <summary>Native fast-forward cost per text page beyond its ops (see <see cref="NativeFastForwardOpMicroseconds"/>).
    /// </summary>
    public const int NativeFastForwardPageMicroseconds = 2400;
}
