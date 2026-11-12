using Ome.Engine.Model;

namespace Ome.Engine.Vm;

/// <summary>One active script frame, as dword offsets. <see cref="CurrentOffset"/> is the instruction executing (or
/// blocked) now; in a caller frame that is its call-script instruction. <see cref="ResumeOffset"/> is the next
/// instruction. Native AGE's frame pc is the resume offset in the innermost frame (at a text wait it is already past
/// the wait) but stays on the call-script instruction in a caller frame. A caller rebuilt by a save load reports the
/// call-script site it was saved at, not the synthetic op-0xae rendezvous it is parked on.</summary>
public sealed record VmFrameLocation(string Script, int ResumeOffset, int CurrentOffset);

public sealed partial class VirtualMachine
{
    /// <summary>Every active frame, outermost first. Callable from any thread; the result is stable while the VM
    /// thread is blocked in a host call (an input wait, sleep, transition or modal movie).</summary>
    public IReadOnlyList<VmFrameLocation> CaptureFrameLocations()
    {
        ExecFrame[] active;
        lock (_debugControlLock) active = _activeExecutionFrames.ToArray();
        var locations = new VmFrameLocation[active.Length];
        for (int i = 0; i < active.Length; i++)
        {
            ExecFrame frame = active[i];
            IReadOnlyList<Instruction> code = frame.Script.Instructions;
            int pc = Volatile.Read(ref frame.Pc);
            // A restored ancestor waits on op 0xae while its restored child runs; native AGE keeps that frame on
            // the saved call-script site (T2), and the frame resumes after it once the child returns.
            if (Volatile.Read(ref frame.RestoredSaveFrame) is { CallTargetIndex: >= 0 } restored
                && restored.CallTargetIndex < frame.Script.ScriptCallOffsets.Count
                && frame.Script.IndexByOffset.TryGetValue(
                    frame.Script.ScriptCallOffsets[restored.CallTargetIndex], out int callSite))
                pc = callSite;
            int current = (uint)pc < (uint)code.Count ? code[pc].Offset : -1;
            int resume = ((uint)(pc + 1) < (uint)code.Count ? code[pc + 1].Offset : current)
                         + Volatile.Read(ref frame.IgnoredDwords);
            locations[i] = new VmFrameLocation(frame.Script.Name, resume, current);
        }
        return locations;
    }
}
