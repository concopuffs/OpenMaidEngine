using Ome.Engine.Model;
using Ome.Engine.Natives;

namespace Ome.Engine.Vm;

// Audio handlers: operands in, calls to the audio native (Ome.Engine.Natives.Audio), which owns the channels, the BGM
// track and envelope, the voice state and the mixer. The skip native and the text history take their part of a voice.
public sealed partial class VirtualMachine
{
    private readonly Audio _audio;
    private readonly EngineTick _tick;

    // 0xbf: an identical request keeps the stream
    private StepResult OpPlayBgm(Instruction ins, int pc)
    {
        _audio.PlayBgm(Read(ins.Args[0]));
        return StepResult.Next;
    }

    // 0xb7: force start, including current-track/zero alias
    private StepResult OpRestartBgmLoop(Instruction ins, int pc)
    {
        _audio.RestartBgm(Read(ins.Args[0]), 1);
        return StepResult.Next;
    }

    // 0xb8: release source and clear retained track
    private StepResult OpStopBgm(Instruction ins, int pc)
    {
        _audio.StopBgm();
        return StepResult.Next;
    }

    // 0xb9: force start without decoder rewind at EOF
    private StepResult OpRestartBgmOnce(Instruction ins, int pc)
    {
        _audio.RestartBgm(Read(ins.Args[0]), 0);
        return StepResult.Next;
    }

    private StepResult OpGetCurrentBgmTrack(Instruction ins, int pc)
    {
        Write(ins.Args[0], _audio.CurrentBgmTrack);
        return StepResult.Next;
    }

    private StepResult OpPlayVoice(Instruction ins, int pc)
    {
        long voiceId = Read(ins.Args[0]);
        _skip.NoteVoiceStarted();
        TextHistory.AppendVoice(voiceId, 0, _advTextStyle);
        _audio.PlayVoice(voiceId);
        return StepResult.Next;
    }

    // 0x1bd: native voice start/history argument is one
    private StepResult OpPlayHistoryVoice(Instruction ins, int pc)
    {
        long voiceId = Read(ins.Args[0]);
        _skip.NoteVoiceStarted();
        TextHistory.AppendVoice(voiceId, 1, _advTextStyle);
        _audio.PlayHistoryVoice(voiceId);
        return StepResult.Next;
    }

    // 0x1d5: the next 0xc4 voice plays at volume 0
    private StepResult OpMuteNextVoice(Instruction ins, int pc)
    {
        _audio.MuteNextVoice();
        return StepResult.Next;
    }

    // 0x1bf: arm the tick's ducked-BGM restore for the current voice
    private StepResult OpArmVoiceDuckRelease(Instruction ins, int pc)
    {
        _audio.ArmVoiceBgmDuckRelease();
        return StepResult.Next;
    }

    // 0x1cf: bit 0 suppresses automatic voice ducking
    private StepResult OpSetVoiceBgmDuckControl(Instruction ins, int pc)
    {
        _audio.SetVoiceBgmDuckControl(Read(ins.Args[0]));
        return StepResult.Next;
    }

    // 0x2c0: replace the pending delayed combat voice request
    private StepResult OpScheduleVoicePlayback(Instruction ins, int pc)
    {
        var a = ins.Args;
        _audio.ScheduleVoicePlayback(Read(a[0]), (int)Read(a[1]), Read(a[2]));
        return StepResult.Next;
    }

    // 0xb4 / semantics: sfx-load
    private StepResult OpSfxLoad(Instruction ins, int pc)
    {
        var a = ins.Args;
        _audio.LoadSoundEffect(Read(a[0]), (int)Read(a[1]));
        return StepResult.Next;
    }

    // 0xb5 / semantics: sfx-start-once
    private StepResult OpSfxStart(Instruction ins, int pc)
    {
        _audio.StartSoundEffect((int)Read(ins.Args[0]), 0);
        return StepResult.Next;
    }

    // 0xba / same worker, logical decoder rewind at EOF
    private StepResult OpSfxStartLoop(Instruction ins, int pc)
    {
        _audio.StartSoundEffect((int)Read(ins.Args[0]), 1);
        return StepResult.Next;
    }

    // 0xb6 / semantics: sfx-release
    private StepResult OpSfxRelease(Instruction ins, int pc)
    {
        _audio.ReleaseSoundEffect((int)Read(ins.Args[0]));
        return StepResult.Next;
    }

    // 0x2bf / native SetDelay(channel, start mode, delay ms)
    private StepResult OpScheduleSfxStart(Instruction ins, int pc)
    {
        var a = ins.Args;
        _audio.ScheduleSoundEffectStart((int)Read(a[0]), (int)Read(a[1]), Read(a[2]));
        return StepResult.Next;
    }

    // 0x1be (out boolean)(native channel 0..12)
    private StepResult OpGetSoundChannelPlaying(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], _audio.IsChannelPlaying(unchecked((int)Read(a[1]))) ? 1 : 0);
        return StepResult.Next;
    }

    // 0xc2 / semantics: fade-bgm (target in basis points: CONFIG passes setting × 100)
    private StepResult OpFadeBgm(Instruction ins, int pc)
    {
        var a = ins.Args;
        _audio.FadeBgm((int)Read(a[0]), Read(a[1]));
        return StepResult.Next;
    }

    // 0xc5 (category)(out basis points)
    private StepResult OpGetAudioVolume(Instruction ins, int pc)
    {
        var a = ins.Args;
        int category = unchecked((int)Read(a[0]));
        if (_audio.TryGetVolume(category, out int basisPoints))
            Write(a[1], basisPoints);
        else
            _host.ReportWarning($"audio volume category out of range: {category}");
        return StepResult.Next;
    }

    // 0xc6 (category)(basis points)
    private StepResult OpSetAudioVolume(Instruction ins, int pc)
    {
        var a = ins.Args;
        int category = unchecked((int)Read(a[0]));
        if (!_audio.TrySetVolume(category, Read(a[1])))
            _host.ReportWarning($"audio volume category out of range: {category}");
        return StepResult.Next;
    }

    // 0xc7 (category)(out boolean)
    private StepResult OpGetAudioRouteEnabled(Instruction ins, int pc)
    {
        var a = ins.Args;
        int category = unchecked((int)Read(a[0]));
        if (_audio.TryGetRouteEnabled(category, out bool enabled))
            Write(a[1], enabled ? 1 : 0);
        else
            _host.ReportWarning($"audio route category out of range: {category}");
        return StepResult.Next;
    }

    // 0x1ba (category)(enabled)
    private StepResult OpSetAudioRouteEnabled(Instruction ins, int pc)
    {
        var a = ins.Args;
        int category = unchecked((int)Read(a[0]));
        if (!_audio.TrySetRouteEnabled(category, Read(a[1]) != 0))
            _host.ReportWarning($"audio route category out of range: {category}");
        return StepResult.Next;
    }
}
