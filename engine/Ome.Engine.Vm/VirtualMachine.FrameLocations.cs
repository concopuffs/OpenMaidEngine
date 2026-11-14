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
    /// <summary>Every active frame, outermost first. Engine thread: the driver reads it through
    /// InputAndWaits.Invoke.</summary>
    public IReadOnlyList<VmFrameLocation> CaptureFrameLocations()
    {
        ExecFrame[] active;
        active = _activeExecutionFrames.ToArray();
        var locations = new VmFrameLocation[active.Length];
        for (int i = 0; i < active.Length; i++)
        {
            ExecFrame frame = active[i];
            IReadOnlyList<Instruction> code = frame.Script.Instructions;
            int pc = frame.Pc;
            // A restored ancestor waits on op 0xae while its restored child runs; native AGE keeps that frame on
            // the saved call-script site (T2), and the frame resumes after it once the child returns.
            if (frame.RestoredSaveFrame is { CallTargetIndex: >= 0 } restored
                && restored.CallTargetIndex < frame.Script.ScriptCallOffsets.Count
                && frame.Script.IndexByOffset.TryGetValue(
                    frame.Script.ScriptCallOffsets[restored.CallTargetIndex], out int callSite))
                pc = callSite;
            int current = (uint)pc < (uint)code.Count ? code[pc].Offset : -1;
            int resume = ((uint)(pc + 1) < (uint)code.Count ? code[pc + 1].Offset : current)
                         + frame.IgnoredDwords;
            locations[i] = new VmFrameLocation(frame.Script.Name, resume, current);
        }
        return locations;
    }
}
