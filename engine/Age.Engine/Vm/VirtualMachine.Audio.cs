using Age.Engine.Model;
using Age.Engine.Persistence;

namespace Age.Engine.Vm;

public sealed partial class VirtualMachine
{
    private readonly AudioMixerSettings _audioMixerSettings;
    private long _currentBgmTrackId;
    private readonly long[] _loadedSoundEffectResourceIds =
        new long[NativeNumberedSaveState.SoundEffectChannelCount];

    private void RestartBgm(long requestedTrackId, int startMode)
    {
        if (requestedTrackId != 0)
            _currentBgmTrackId = requestedTrackId;

        if (_currentBgmTrackId == 0)
            _host.StopBgm();
        else
            _host.RestartBgm(_currentBgmTrackId, startMode);
    }

    private int StepAudio(string label, IReadOnlyList<Operand> a, int pc)
    {
        switch (label)
        {
            case "play-bgm":
            {
                long requestedTrackId = Read(a[0]);
                // Native scripts freely reassert the stage BGM after FIELD action/event cleanup.
                // The music facade retains the current track id, so an identical request keeps the
                // existing stream position instead of reopening the OGG from the beginning.
                if (requestedTrackId != _currentBgmTrackId)
                {
                    _currentBgmTrackId = requestedTrackId;
                    _host.PlayBgm(_currentBgmTrackId);
                }
                return pc + 1;
            }
            case "restart-bgm-loop": // 0xb7: force start, including current-track/zero alias
                RestartBgm(Read(a[0]), 1);
                return pc + 1;
            case "stop-bgm": // 0xb8: release source and clear retained track
                _currentBgmTrackId = 0;
                _host.StopBgm();
                return pc + 1;
            case "restart-bgm-once": // 0xb9: force start without decoder rewind at EOF
                RestartBgm(Read(a[0]), 0);
                return pc + 1;
            case "get-current-bgm-track":
                Write(a[0], _currentBgmTrackId);
                return pc + 1;
            case "play-voice":
                _autoVoicePending = true;
                TextHistory.AppendVoice(Read(a[0]), 0, _advTextStyle);
                _host.PlayVoice(Read(a[0]), 0); return pc + 1;
            case "play-history-voice": // 0x1bd: native voice start/history argument is one
            case "u0041D910":
                _autoVoicePending = true;
                TextHistory.AppendVoice(Read(a[0]), 1, _advTextStyle);
                _host.PlayVoice(Read(a[0]), 1); return pc + 1;
            case "set-voice-bgm-duck-control": // 0x1cf: bit 0 suppresses automatic voice ducking
                _host.SetVoiceBgmDuckControl(Read(a[0])); return pc + 1;
            case "schedule-voice-playback": // 0x2c0: replace the pending delayed combat voice request
                _host.ScheduleVoicePlayback(Read(a[0]), (int)Read(a[1]), Read(a[2])); return pc + 1;
            case "play-sound-effect":   // 0xb4 / semantics: sfx-load
            {
                long resourceId = Read(a[0]);
                int channel = (int)Read(a[1]);
                if ((uint)channel < (uint)_loadedSoundEffectResourceIds.Length)
                    _loadedSoundEffectResourceIds[channel] = resourceId;
                _host.LoadSoundEffect(resourceId, channel);
                return pc + 1;
            }
            case "u0041D050":           // 0xb5 / semantics: sfx-start-once
                _host.StartSoundEffect((int)Read(a[0]), 0); return pc + 1;
            case "sfx-start-loop":       // 0xba / same worker, logical decoder rewind at EOF
                _host.StartSoundEffect((int)Read(a[0]), 1); return pc + 1;
            case "u0041D080":           // 0xb6 / semantics: sfx-release
            {
                int channel = (int)Read(a[0]);
                if ((uint)channel < (uint)_loadedSoundEffectResourceIds.Length)
                    _loadedSoundEffectResourceIds[channel] = 0;
                _host.ReleaseSoundEffect(channel);
                return pc + 1;
            }
            case "schedule-sfx-start":  // 0x2bf / native SetDelay(channel, start mode, delay ms)
                _host.ScheduleSoundEffectStart((int)Read(a[0]), (int)Read(a[1]), Read(a[2])); return pc + 1;
            case "u0041D2B0":           // 0xc2 / semantics: fade-bgm
            {
                int targetPercent = (int)Read(a[0]);
                long durationMs = Read(a[1]);
                // Native op 0xc2 enters its timed run-state only during ordinary playback. An
                // already-active ADV fast-forward applies the fade endpoint immediately; a normal
                // advance click does not set that skip state and therefore retains the authored delay.
                bool forceEndpoint = _messageSkipServiceActive || _host.IsMessageSkipActive;
                _host.FadeBgm(targetPercent, forceEndpoint ? 0 : durationMs);
                if (targetPercent == 0)
                {
                    _currentBgmTrackId = 0;
                    _host.StopBgm();
                }
                return pc + 1;
            }
            case "get-audio-volume": // 0xc5 (category)(out basis points)
            {
                int category = unchecked((int)Read(a[0]));
                if (_audioMixerSettings.TryGetVolume(category, out int basisPoints))
                    Write(a[1], basisPoints);
                else
                    _host.ReportWarning($"audio volume category out of range: {category}");
                return pc + 1;
            }
            case "set-audio-volume": // 0xc6 (category)(basis points)
            {
                int category = unchecked((int)Read(a[0]));
                long basisPoints = Read(a[1]);
                if (_audioMixerSettings.TrySetVolume(category, basisPoints))
                    _host.ApplyAudioVolume(category, checked((int)basisPoints));
                else
                    _host.ReportWarning($"audio volume category out of range: {category}");
                return pc + 1;
            }
            case "get-audio-route-enabled": // 0xc7 (category)(out boolean)
            {
                int category = unchecked((int)Read(a[0]));
                if (_audioMixerSettings.TryGetRouteEnabled(category, out bool enabled))
                    Write(a[1], enabled ? 1 : 0);
                else
                    _host.ReportWarning($"audio route category out of range: {category}");
                return pc + 1;
            }
            case "set-audio-route-enabled": // 0x1ba (category)(enabled)
            {
                int category = unchecked((int)Read(a[0]));
                bool enabled = Read(a[1]) != 0;
                if (_audioMixerSettings.TrySetRouteEnabled(category, enabled, out bool changed))
                {
                    if (changed) _host.ApplyAudioRouteEnabled(category, enabled);
                }
                else
                    _host.ReportWarning($"audio route category out of range: {category}");
                return pc + 1;
            }
            default:
                throw new InvalidOperationException($"Non-audio opcode routed to audio handler: {label}");
        }
    }
}
