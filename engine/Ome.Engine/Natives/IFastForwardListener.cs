namespace Ome.Engine.Natives;

/// <summary>The two inputs of run-state 0x08000000, ADV fast-forward. Either one sets it.</summary>
public enum FastForwardChannel
{
    /// <summary>Persistent Skip, read skip and the ops and services that set or clear the bit: ops 0x88, 0x19b,
    /// 0x19c, 0x101 and 0xfa, the text wait's entry, the menu's Skip toggle and the click cancel.</summary>
    Script,

    /// <summary>Logical action 6 held (Ctrl, C and Backspace in both games; <see cref="SkipAndAuto"/>'s physical
    /// input).</summary>
    Physical,
}

/// <summary>
/// Told each time a fast-forward channel of <see cref="SkipAndAuto"/> is set, changed or not. The lifted host keeps
/// what a change does elsewhere until those natives exist (implementation plan §3.2): when fast-forward starts it
/// completes the reveal and wakes the waits, and when it ends it plays the voice it held back.
/// </summary>
/// <remarks>Called on the thread that set the channel, after the state lock is released. A physical set calls it
/// inside the input lock, so the effects of two action-6 decisions do not interleave.</remarks>
public interface IFastForwardListener
{
    /// <param name="channel">The channel that was set.</param>
    /// <param name="value">The channel's new value.</param>
    /// <param name="active">Run-state 0x08000000 after the set: either channel.</param>
    /// <param name="changed">Whether <paramref name="active"/> changed.</param>
    void FastForwardChannelSet(FastForwardChannel channel, bool value, bool active, bool changed);
}
