using Age.Engine.Diagnostics;
using Age.Engine.Model;
using Age.Engine.Persistence;

namespace Age.Engine.Vm;

public sealed partial class VirtualMachine
{
    private int StepPersistence(string label, IReadOnlyList<Operand> a, int pc)
    {
        switch (label)
        {
            case "is-catalog-resource-unlocked": // 0x19d
                Write(a[0], _sharedProfile.IsCatalogResourceUnlocked(Read(a[1])) ? 1 : 0);
                return pc + 1;
            case "save-numbered-slot": // 0x19e
            {
                if (_nativeDatStore == null)
                {
                    Write(a[0], 1);
                    return pc + 1;
                }
                try
                {
                    int slot = unchecked((int)Read(a[1]));
                    NativeNumberedSaveState state = CaptureNumberedState();
                    byte[] payload = NativeNumberedSaveCodec.Encode(state);
                    byte[] history = NativeTextHistoryCodec.Encode(TextHistory);
                    NativeSystemTime timestamp = NativeSystemTime.FromLocalDateTime(DateTime.Now);
                    uint playSeconds = AccumulatedPlaySeconds();
                    _nativeDatStore.SaveNumberedFile(slot, payload, history, timestamp, playSeconds);
                    _sharedProfile.Save(_nativeDatStore, timestamp, playSeconds);
                    Write(a[0], 0);
                }
                catch (Exception error) when (
                    error is IOException or UnauthorizedAccessException or InvalidDataException
                    or ArgumentOutOfRangeException or OverflowException)
                {
                    Write(a[0], 1);
                }
                return pc + 1;
            }
            case "load-numbered-slot-data-only": // 0x19f
            {
                if (!TryLoadNumberedState(unchecked((int)Read(a[1])), restoreHistory: false))
                    Write(a[0], 1);
                else
                    Write(a[0], 0);
                return pc + 1;
            }
            case "load-numbered-slot-and-resume": // 0x1a1
            {
                if (!TryLoadNumberedState(unchecked((int)Read(a[1])), restoreHistory: true))
                {
                    Write(a[0], 1);
                    return pc + 1;
                }
                throw new NumberedRestoreRequestedException();
            }
            case "continue-save-load-stack-restore": // 0xae
            {
                if (_loadedNumberedState == null || _restoreFrameIndex < 0)
                    return pc + 1;
                NativeSavedScriptFrame saved = _loadedNumberedState.Frames[_restoreFrameIndex];
                bool terminal = _restoreFrameIndex == _loadedNumberedState.Frames.Count - 1;
                if (terminal)
                {
                    // Native restores the saved top frame as the numbered-save boundary selected by
                    // opcode 0x1ad. Re-establish that identity so a subsequent save excludes transient
                    // SAVE/menu helper frames instead of serializing the currently open modal stack.
                    lock (_debugControlLock) _saveResumeFrame = _cur;
                    _cur.RestoredSaveFrame = null;
                    _loadedNumberedState = null;
                    _restoreFrameIndex = -1;
                    return ResolveTableOffset(_cur.Script, _cur.Script.ReadMessageOffsets, saved.ResumeIndex, pc + 1);
                }

                int parentIndex = _restoreFrameIndex;
                NativeSavedScriptFrame childSaved = _loadedNumberedState.Frames[parentIndex + 1];
                Script child = ResolveSavedScript(childSaved);
                _restoreFrameIndex = parentIndex + 1;
                FrameOutcome childOutcome = RunFrame(
                    CreateRestoredFrame(child, childSaved), FrameCause.SaveRestore,
                    childSaved.ScriptId);
                _restoreFrameIndex = parentIndex;
                if (childOutcome == FrameOutcome.Halted) return HALT;
                if (childOutcome == FrameOutcome.RootReload) return ROOT_RELOAD;
                if (childOutcome == FrameOutcome.ExitRequested)
                    throw new ProcessExitRequestedException();
                _cur.RestoredSaveFrame = null;
                return ResolveTableOffset(
                    _cur.Script, _cur.Script.ScriptCallOffsets, saved.CallTargetIndex, pc) + 1;
            }
            case "query-numbered-save-metadata": // 0x1a0
            {
                if (_nativeDatStore == null)
                {
                    Write(a[0], 1);
                    return pc + 1;
                }
                try
                {
                    NativeSaveMetadata? metadata =
                        _nativeDatStore.QueryNumberedMetadata(unchecked((int)Read(a[1])));
                    if (metadata == null)
                    {
                        Write(a[0], 1);
                        return pc + 1;
                    }
                    Write(a[2], metadata.Timestamp.Year);
                    Write(a[3], metadata.Timestamp.Month);
                    Write(a[4], metadata.Timestamp.Day);
                    Write(a[5], metadata.Timestamp.Hour);
                    Write(a[6], metadata.Timestamp.Minute);
                    Write(a[7], metadata.Timestamp.Second);
                    Write(a[8], unchecked((int)metadata.AccumulatedPlaySeconds));
                    Write(a[0], 0);
                }
                catch (EndOfStreamException) { Write(a[0], 2); }
                catch (InvalidDataException) { Write(a[0], 2); }
                catch (ArgumentOutOfRangeException) { Write(a[0], 2); }
                catch (IOException) { Write(a[0], 1); }
                catch (UnauthorizedAccessException) { Write(a[0], 1); }
                return pc + 1;
            }
            case "delete-numbered-save": // 0x1ab
                try
                {
                    Write(a[0], _nativeDatStore?.DeleteNumberedPair(unchecked((int)Read(a[1]))) ?? 2);
                }
                catch (ArgumentOutOfRangeException) { Write(a[0], 2); }
                return pc + 1;
            case "copy-numbered-save": // 0x1ac
                try
                {
                    Write(a[0], _nativeDatStore?.CopyNumberedPair(
                        unchecked((int)Read(a[1])), unchecked((int)Read(a[2]))) ?? 2);
                }
                catch (ArgumentOutOfRangeException) { Write(a[0], 2); }
                catch (IOException) { Write(a[0], 2); }
                catch (UnauthorizedAccessException) { Write(a[0], 2); }
                return pc + 1;
            case "mark-save-resume-frame": // 0x1ad
                lock (_debugControlLock) _saveResumeFrame = _cur;
                return pc + 1;
            case "write-numbered-save-thumbnail": // 0x1ae
            {
                if (_nativeDatStore == null)
                {
                    Write(a[0], 1);
                    return pc + 1;
                }
                try
                {
                    var image = _host.CaptureSurfacePixels(unchecked((int)Read(a[2])));
                    if (image == null)
                    {
                        Write(a[0], 2);
                        return pc + 1;
                    }
                    byte[] encoded = NumberedThumbnailCodec.Encode(image);
                    _nativeDatStore.SaveNumberedThumbnail(unchecked((int)Read(a[1])), encoded);
                    Write(a[0], 0);
                }
                catch (ArgumentOutOfRangeException) { Write(a[0], 2); }
                catch (InvalidDataException) { Write(a[0], 2); }
                catch (OverflowException) { Write(a[0], 2); }
                catch (IOException) { Write(a[0], 1); }
                catch (UnauthorizedAccessException) { Write(a[0], 1); }
                return pc + 1;
            }
            case "load-numbered-save-thumbnail": // 0x1af
            {
                if (_nativeDatStore == null)
                {
                    Write(a[0], 1);
                    return pc + 1;
                }
                try
                {
                    byte[]? encoded =
                        _nativeDatStore.LoadNumberedThumbnail(unchecked((int)Read(a[1])));
                    if (encoded == null)
                    {
                        Write(a[0], 1);
                        return pc + 1;
                    }
                    var image = NumberedThumbnailCodec.Decode(encoded);
                    Write(a[0], _host.ReplaceSurfacePixels(unchecked((int)Read(a[2])), image) ? 0 : 2);
                }
                catch (ArgumentOutOfRangeException) { Write(a[0], 2); }
                catch (InvalidDataException) { Write(a[0], 2); }
                catch (OverflowException) { Write(a[0], 2); }
                catch (IOException) { Write(a[0], 1); }
                catch (UnauthorizedAccessException) { Write(a[0], 1); }
                return pc + 1;
            }
            case "store-shared-profile-int": // 0x1a2
            {
                if (!TryResolveSharedProfileCell(a[0], isString: false, out int address))
                {
                    HaltReason ??= $"shared-profile-int-lvalue-type:{a[0].Type}";
                    return HALT;
                }
                _sharedProfile.StoreInteger(address, Read(a[0]));
                return pc + 1;
            }
            case "load-shared-profile-int": // 0x1a3
            {
                if (!TryResolveSharedProfileCell(a[0], isString: false, out int address))
                {
                    HaltReason ??= $"shared-profile-int-lvalue-type:{a[0].Type}";
                    return HALT;
                }
                Write(a[0], _sharedProfile.LoadInteger(address));
                return pc + 1;
            }
            case "store-shared-profile-string": // 0x1a9
            {
                if (!TryResolveSharedProfileCell(a[0], isString: true, out int address))
                {
                    HaltReason ??= $"shared-profile-string-lvalue-type:{a[0].Type}";
                    return HALT;
                }
                _sharedProfile.StoreString(address, ReadStr(a[0]));
                return pc + 1;
            }
            case "load-shared-profile-string": // 0x1aa
            {
                if (!TryResolveSharedProfileCell(a[0], isString: true, out int address))
                {
                    HaltReason ??= $"shared-profile-string-lvalue-type:{a[0].Type}";
                    return HALT;
                }
                WriteStr(a[0], _sharedProfile.LoadString(address));
                return pc + 1;
            }
            default:
                throw new InvalidOperationException($"Non-persistence opcode routed to persistence handler: {label}");
        }
    }
}
