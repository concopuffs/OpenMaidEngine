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
        _text.History.AppendVoice(voiceId, 0, _text.Style);
        _audio.PlayVoice(voiceId);
        return StepResult.Next;
    }

    // 0x1bd: native voice start/history argument is one
    private StepResult OpPlayHistoryVoice(Instruction ins, int pc)
    {
        long voiceId = Read(ins.Args[0]);
        _skip.NoteVoiceStarted();
        _text.History.AppendVoice(voiceId, 1, _text.Style);
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

    // 0xb4 / semantics: sfx-load. sfx_channel_load@0x482500 decodes into the sound manager's channel, which refuses
    // 13 and up (sound_decode_channel's dsReadSound trace), and then throws 「WAVファイル %s の読み込みに失敗しました」
    // with flags 0x10007. A load it decodes is recorded in its ten-entry resource table unchecked: channels 10 to 12
    // write into the voice facade after it, and a negative channel passes the decoder's signed test.
    private StepResult OpSfxLoad(Instruction ins, int pc)
    {
        var a = ins.Args;
        long resourceId = Read(a[0]);
        long channel = Read(a[1]);
        if (channel >= Audio.NativeChannelCount)
        {
            _waits.ReportWarning($"{At(ins)}: sound channel {channel} is outside 0..{Audio.NativeChannelCount - 1}; "
                                 + "AGE traces 「関数：dsReadSound エラー：不正なSound番号です」 and fails the load");
            return StepResult.Jump(RaiseInstructionScriptError(
                AgeBoxMessage.Keyed(AgeBoxText.WaveLoadFailed, _audio.SoundEffectName(resourceId)), pc));
        }
        if (IndexRefused(ins, "sound channel", channel, Audio.SoundEffectChannelCount, IndexTest.Unchecked)
            is { } refused)
            return refused;
        _audio.LoadSoundEffect(resourceId, (int)channel);
        return StepResult.Next;
    }

    // 0xb5 / semantics: sfx-start-once
    private StepResult OpSfxStart(Instruction ins, int pc)
    {
        long channel = Read(ins.Args[0]);
        if (SoundStartRefused(ins, channel) is { } refused) return refused;
        _audio.StartSoundEffect((int)channel, 0);
        return StepResult.Next;
    }

    // 0xba / same worker, logical decoder rewind at EOF
    private StepResult OpSfxStartLoop(Instruction ins, int pc)
    {
        long channel = Read(ins.Args[0]);
        if (SoundStartRefused(ins, channel) is { } refused) return refused;
        _audio.StartSoundEffect((int)channel, 1);
        return StepResult.Next;
    }

    /// <summary>sound_manager_start_channel@0x482f00 starts any of its 13 channels and traces
    /// 「関数：dsPlay エラー：不正なSound番号[n]です」 for the rest, unsigned. A channel with no sound is traced too
    /// (channel 10 always; 11 and 12 before the first track or voice), while the sound effect route is on
    /// (sfx_channel_start@0x4825d0 returns before the manager when it is off).</summary>
    private StepResult? SoundStartRefused(Instruction ins, long channel)
    {
        if (IndexRefused(ins, "sound channel", channel, Audio.NativeChannelCount,
                         IndexTest.Traced($"関数：dsPlay エラー：不正なSound番号[{unchecked((int)channel)}]です",
                                          Audio.NativeChannelCount)) is { } refused)
            return refused;
        if (!_audio.ChannelHoldsSound((int)channel)
            && _audio.TryGetRouteEnabled((int)AudioMixerCategory.SoundEffect, out bool routed) && routed)
            _waits.ReportWarning($"{At(ins)}: sound channel {channel} holds no sound; skipped, as AGE skips it with "
                                 + $"the trace 「関数：dsPlay エラー：データが読み込まれていません [{channel}]」");
        return null;
    }

    // 0xb6 / semantics: sfx-release. sfx_channel_release@0x482600 clears the channel's entry in its ten-entry resource
    // table whatever the channel (sound_buffer_destroy's test, below 13 and signed, comes first), so every channel
    // outside 0..9 writes outside the table.
    private StepResult OpSfxRelease(Instruction ins, int pc)
    {
        long channel = Read(ins.Args[0]);
        if (IndexRefused(ins, "sound channel", channel, Audio.SoundEffectChannelCount, IndexTest.Unchecked)
            is { } refused)
            return refused;
        _audio.ReleaseSoundEffect((int)channel);
        return StepResult.Next;
    }

    // 0x2bf / native SetDelay(channel, start mode, delay ms). sfx_set_delay@0x482720 traces channels of 10 and up,
    // and its test is signed: a negative channel writes before its arrays.
    private StepResult OpScheduleSfxStart(Instruction ins, int pc)
    {
        var a = ins.Args;
        long channel = Read(a[0]);
        if (IndexRefused(ins, "sound channel", channel, Audio.SoundEffectChannelCount,
                         IndexTest.Traced("関数：SetDelay エラー：不正なSound番号です", Audio.SoundEffectChannelCount,
                                          signed: true)) is { } refused)
            return refused;
        _audio.ScheduleSoundEffectStart((int)channel, (int)Read(a[1]), Read(a[2]));
        return StepResult.Next;
    }

    // 0x1c4 (out boolean): whether the voice channel, native channel 12, is playing; 0 without a sound manager
    private StepResult OpVoiceIsPlaying(Instruction ins, int pc)
    {
        Write(ins.Args[0], _audio.IsChannelPlaying(Audio.NativeChannelCount - 1) ? 1 : 0); return StepResult.Next;
    }

    // 0x1be (out boolean)(native channel 0..12). sound_channel_is_playing@0x483000 traces channels of 13 and up and
    // writes 0; its test is signed, so a negative channel reads before its table.
    private StepResult OpGetSoundChannelPlaying(Instruction ins, int pc)
    {
        var a = ins.Args;
        long channel = Read(a[1]);
        if (IndexRefused(ins, "sound channel", channel, Audio.NativeChannelCount,
                         IndexTest.Traced("関数：dsIsPlaying エラー：不正なSound番号です", Audio.NativeChannelCount,
                                          signed: true)) is { } refused)
        {
            if (refused.Continues) Write(a[0], 0);
            return refused;
        }
        Write(a[0], _audio.IsChannelPlaying((int)channel) ? 1 : 0);
        return StepResult.Next;
    }

    // 0xc1: flip the BGM's pause
    private StepResult OpToggleBgmPause(Instruction ins, int pc)
    {
        _audio.ToggleBgmPause(); return StepResult.Next;
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
            ReportSkipped(ins, "volume category", category, "0..4", BadArgumentTrace("GetVolume"));
        return StepResult.Next;
    }

    // 0xc6 (category)(basis points)
    private StepResult OpSetAudioVolume(Instruction ins, int pc)
    {
        var a = ins.Args;
        int category = unchecked((int)Read(a[0]));
        if (!_audio.TrySetVolume(category, Read(a[1])))
            ReportSkipped(ins, "volume category", category, "0..4", BadArgumentTrace("SetVolume"));
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
            ReportSkipped(ins, "route category", category, "1..4", BadArgumentTrace("GetSoundMode"));
        return StepResult.Next;
    }

    // 0x1ba (category)(enabled)
    private StepResult OpSetAudioRouteEnabled(Instruction ins, int pc)
    {
        var a = ins.Args;
        int category = unchecked((int)Read(a[0]));
        if (!_audio.TrySetRouteEnabled(category, Read(a[1]) != 0))
            ReportSkipped(ins, "route category", category, "1..4", BadArgumentTrace("SetSoundMode"));
        return StepResult.Next;
    }
}
