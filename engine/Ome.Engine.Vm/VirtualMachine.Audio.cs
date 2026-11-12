using Ome.Engine.Model;
using Ome.Engine.Persistence;

namespace Ome.Engine.Vm;

public sealed partial class VirtualMachine
{
    private readonly AudioMixerSettings _audioMixerSettings;
    private long _currentBgmTrackId;
    // Run-state 0x200: the target of the op 0xc2 fade the host is running in the background, or null.
    private int? _bgmFadeTarget;
    private readonly long[] _loadedSoundEffectResourceIds =
        new long[NativeNumberedSaveState.SoundEffectChannelCount];

    /// <summary>Settle the op 0xc2 fade. Native op 0xc2 only arms it: the engine tick advances it on its own timer and
    /// keeps dispatching ops meanwhile, and at 100 % bgm_fade_tick commits the level, a zero target releasing the source
    /// and clearing the retained track. With <paramref name="complete"/> a running fade ends now, as ops
    /// 0xb7/0xb8/0xb9/0xbf/0xc3 end it before they act (bgm_fade_tick(..., 100)); without it only a fade that has run its
    /// course is settled, for readers of the retained track (op 0xc0, a save).</summary>
    private void ServiceBgmFade(bool complete)
    {
        if (_bgmFadeTarget is not int target) return;
        if (_host.IsBgmFadeActive)
        {
            if (!complete) return;
            _host.CompleteBgmFade();
        }
        _bgmFadeTarget = null;
        if (target == 0) _currentBgmTrackId = 0;
    }

    private void RestartBgm(long requestedTrackId, int startMode)
    {
        ServiceBgmFade(complete: true);
        if (requestedTrackId != 0)
            _currentBgmTrackId = requestedTrackId;

        if (_currentBgmTrackId == 0)
            _host.StopBgm();
        else
            _host.RestartBgm(_currentBgmTrackId, startMode);
    }

    private StepResult OpPlayBgm(Instruction ins, int pc)
    {
        var a = ins.Args;
        ServiceBgmFade(complete: true);
        long requestedTrackId = Read(a[0]);
        // Native scripts freely reassert the stage BGM after FIELD action/event cleanup.
        // The music facade retains the current track id, so an identical request keeps the
        // existing stream position instead of reopening the OGG from the beginning.
        if (requestedTrackId != _currentBgmTrackId)
        {
            _currentBgmTrackId = requestedTrackId;
            _host.PlayBgm(_currentBgmTrackId);
        }
        return StepResult.Next;
    }

    // 0xb7: force start, including current-track/zero alias
    private StepResult OpRestartBgmLoop(Instruction ins, int pc)
    {
        var a = ins.Args;
        RestartBgm(Read(a[0]), 1);
        return StepResult.Next;
    }

    // 0xb8: release source and clear retained track
    private StepResult OpStopBgm(Instruction ins, int pc)
    {
        ServiceBgmFade(complete: true);
        _currentBgmTrackId = 0;
        _host.StopBgm();
        return StepResult.Next;
    }

    // 0xb9: force start without decoder rewind at EOF
    private StepResult OpRestartBgmOnce(Instruction ins, int pc)
    {
        var a = ins.Args;
        RestartBgm(Read(a[0]), 0);
        return StepResult.Next;
    }

    private StepResult OpGetCurrentBgmTrack(Instruction ins, int pc)
    {
        var a = ins.Args;
        ServiceBgmFade(complete: false);
        Write(a[0], _currentBgmTrackId);
        return StepResult.Next;
    }

    private StepResult OpPlayVoice(Instruction ins, int pc)
    {
        var a = ins.Args;
        // 0xc4 promotes a 0x1d5 request (bit 0 -> 0x10001) and plays this voice muted; a request
        // that was already promoted belongs to an earlier voice and is cleared instead.
        if ((_voiceMuteNextFlags & VoiceMutePromoted) != 0) _voiceMuteNextFlags = 0;
        else if ((_voiceMuteNextFlags & VoiceMuteRequested) != 0) _voiceMuteNextFlags |= VoiceMutePromoted;
        bool muted = (_voiceMuteNextFlags & VoiceMuteRequested) != 0;
        _autoVoicePending = true;
        TextHistory.AppendVoice(Read(a[0]), 0, _advTextStyle);
        _host.PlayVoice(Read(a[0]), 0, muted); return StepResult.Next;
    }

    // 0x1bd: native voice start/history argument is one
    private StepResult OpPlayHistoryVoice(Instruction ins, int pc)
    {
        var a = ins.Args;
        // History replay always plays audibly and consumes any pending 0x1d5 request.
        if ((_voiceMuteNextFlags & (VoiceMuteRequested | VoiceMutePromoted)) != 0) _voiceMuteNextFlags = 0;
        _autoVoicePending = true;
        TextHistory.AppendVoice(Read(a[0]), 1, _advTextStyle);
        _host.PlayVoice(Read(a[0]), 1, false); return StepResult.Next;
    }

    // 0x1d5: the next 0xc4 voice plays at volume 0
    private StepResult OpMuteNextVoice(Instruction ins, int pc)
    {
        _voiceMuteNextFlags = VoiceMuteRequested; return StepResult.Next;
    }

    // 0x1bf: arm the tick's ducked-BGM restore for the current voice
    private StepResult OpArmVoiceDuckRelease(Instruction ins, int pc)
    {
        _host.ArmVoiceBgmDuckRelease(); return StepResult.Next;
    }

    // 0x1cf: bit 0 suppresses automatic voice ducking
    private StepResult OpSetVoiceBgmDuckControl(Instruction ins, int pc)
    {
        var a = ins.Args;
        _host.SetVoiceBgmDuckControl(Read(a[0])); return StepResult.Next;
    }

    // 0x2c0: replace the pending delayed combat voice request
    private StepResult OpScheduleVoicePlayback(Instruction ins, int pc)
    {
        var a = ins.Args;
        _host.ScheduleVoicePlayback(Read(a[0]), (int)Read(a[1]), Read(a[2])); return StepResult.Next;
    }

    // 0xb4 / semantics: sfx-load
    private StepResult OpSfxLoad(Instruction ins, int pc)
    {
        var a = ins.Args;
        long resourceId = Read(a[0]);
        int channel = (int)Read(a[1]);
        if ((uint)channel < (uint)_loadedSoundEffectResourceIds.Length)
            _loadedSoundEffectResourceIds[channel] = resourceId;
        _host.LoadSoundEffect(resourceId, channel);
        return StepResult.Next;
    }

    // 0xb5 / semantics: sfx-start-once
    private StepResult OpSfxStart(Instruction ins, int pc)
    {
        var a = ins.Args;
        _host.StartSoundEffect((int)Read(a[0]), 0); return StepResult.Next;
    }

    // 0xba / same worker, logical decoder rewind at EOF
    private StepResult OpSfxStartLoop(Instruction ins, int pc)
    {
        var a = ins.Args;
        _host.StartSoundEffect((int)Read(a[0]), 1); return StepResult.Next;
    }

    // 0xb6 / semantics: sfx-release
    private StepResult OpSfxRelease(Instruction ins, int pc)
    {
        var a = ins.Args;
        int channel = (int)Read(a[0]);
        if ((uint)channel < (uint)_loadedSoundEffectResourceIds.Length)
            _loadedSoundEffectResourceIds[channel] = 0;
        _host.ReleaseSoundEffect(channel);
        return StepResult.Next;
    }

    // 0x2bf / native SetDelay(channel, start mode, delay ms)
    private StepResult OpScheduleSfxStart(Instruction ins, int pc)
    {
        var a = ins.Args;
        _host.ScheduleSoundEffectStart((int)Read(a[0]), (int)Read(a[1]), Read(a[2])); return StepResult.Next;
    }

    // 0x1be (out boolean)(native channel 0..12)
    private StepResult OpGetSoundChannelPlaying(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], _host.IsSoundChannelPlaying(unchecked((int)Read(a[1]))) ? 1 : 0);
        return StepResult.Next;
    }

    // 0xc2 / semantics: fade-bgm
    private StepResult OpFadeBgm(Instruction ins, int pc)
    {
        var a = ins.Args;
        int targetBasisPoints = (int)Read(a[0]);   // basis points: CONFIG passes setting × 100
        long durationMs = Read(a[1]);
        // Native op 0xc2 arms a background fade (run-state 0x200) and the script continues. An
        // already-active ADV fast-forward applies the fade endpoint immediately; a normal advance
        // click does not set that skip state and therefore retains the authored duration.
        bool forceEndpoint = _messageSkipServiceActive || _host.IsMessageSkipActive;
        _host.FadeBgm(targetBasisPoints, forceEndpoint ? 0 : durationMs);
        _bgmFadeTarget = targetBasisPoints;
        ServiceBgmFade(complete: false);   // a zero-length fade has already ended
        return StepResult.Next;
    }

    // 0xc5 (category)(out basis points)
    private StepResult OpGetAudioVolume(Instruction ins, int pc)
    {
        var a = ins.Args;
        int category = unchecked((int)Read(a[0]));
        if (_audioMixerSettings.TryGetVolume(category, out int basisPoints))
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
        long basisPoints = Read(a[1]);
        if (_audioMixerSettings.TrySetVolume(category, basisPoints))
            _host.ApplyAudioVolume(category, checked((int)basisPoints));
        else
            _host.ReportWarning($"audio volume category out of range: {category}");
        return StepResult.Next;
    }

    // 0xc7 (category)(out boolean)
    private StepResult OpGetAudioRouteEnabled(Instruction ins, int pc)
    {
        var a = ins.Args;
        int category = unchecked((int)Read(a[0]));
        if (_audioMixerSettings.TryGetRouteEnabled(category, out bool enabled))
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
        bool enabled = Read(a[1]) != 0;
        if (_audioMixerSettings.TrySetRouteEnabled(category, enabled, out bool changed))
        {
            if (changed) _host.ApplyAudioRouteEnabled(category, enabled);
        }
        else
            _host.ReportWarning($"audio route category out of range: {category}");
        return StepResult.Next;
    }
}
