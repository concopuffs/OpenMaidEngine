using Ome.Engine.Diagnostics;
using Ome.Engine.Model;
using Ome.Engine.Persistence;

namespace Ome.Engine.Vm;

public sealed partial class VirtualMachine
{
    // 0x19d
    private StepResult OpIsCatalogResourceUnlocked(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], _sharedProfile.IsCatalogResourceUnlocked(Read(a[1])) ? 1 : 0);
        return StepResult.Next;
    }

    // 0x19e
    private StepResult OpSaveNumberedSlot(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (_nativeDatStore == null)
        {
            Write(a[0], 1);
            return StepResult.Next;
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
        return StepResult.Next;
    }

    // 0x19f
    private StepResult OpLoadNumberedSlotDataOnly(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (!TryLoadNumberedState(unchecked((int)Read(a[1])), restoreHistory: false))
            Write(a[0], 1);
        else
            Write(a[0], 0);
        return StepResult.Next;
    }

    // 0x1a1
    private StepResult OpLoadNumberedSlotAndResume(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (!TryLoadNumberedState(unchecked((int)Read(a[1])), restoreHistory: true))
        {
            Write(a[0], 1);
            return StepResult.Next;
        }
        throw new NumberedRestoreRequestedException();
    }

    // 0xad: op_0xad_commit_pending_playtime (no shipped script uses it)
    private StepResult OpCommitPendingPlaytime(Instruction ins, int pc)
    {
        CommitLoadedPlayTime();
        return StepResult.Next;
    }

    // 0xae
    private StepResult OpContinueSaveLoadStackRestore(Instruction ins, int pc)
    {
        if (_loadedNumberedState == null || _restoreFrameIndex < 0)
            return StepResult.Next;
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
            return StepResult.Jump(ResolveTableOffset(_cur.Script, _cur.Script.ReadMessageOffsets, saved.ResumeIndex, pc + 1));
        }

        int parentIndex = _restoreFrameIndex;
        NativeSavedScriptFrame childSaved = _loadedNumberedState.Frames[parentIndex + 1];
        Script child = ResolveSavedScript(childSaved);
        CommitLoadedPlayTime();   // script_frame_restore_saved_layout for the child frame
        _restoreFrameIndex = parentIndex + 1;
        FrameOutcome childOutcome = RunFrame(
            CreateRestoredFrame(child, childSaved), FrameCause.SaveRestore,
            childSaved.ScriptId);
        _restoreFrameIndex = parentIndex;
        if (childOutcome == FrameOutcome.Halted) return StepResult.Halt;
        if (childOutcome == FrameOutcome.RootReload) return StepResult.RootReload;
        if (childOutcome == FrameOutcome.ExitRequested)
            throw new ProcessExitRequestedException();
        _cur.RestoredSaveFrame = null;
        return StepResult.Jump(ResolveTableOffset(
            _cur.Script, _cur.Script.ScriptCallOffsets, saved.CallTargetIndex, pc) + 1);
    }

    // 0x1a0
    private StepResult OpQueryNumberedSaveMetadata(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (_nativeDatStore == null)
        {
            Write(a[0], 1);
            return StepResult.Next;
        }
        try
        {
            NativeSaveMetadata? metadata =
                _nativeDatStore.QueryNumberedMetadata(unchecked((int)Read(a[1])));
            if (metadata == null)
            {
                Write(a[0], 1);
                return StepResult.Next;
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
        return StepResult.Next;
    }

    // 0x1ab
    private StepResult OpDeleteNumberedSave(Instruction ins, int pc)
    {
        var a = ins.Args;
        try
        {
            Write(a[0], _nativeDatStore?.DeleteNumberedPair(unchecked((int)Read(a[1]))) ?? 2);
        }
        catch (ArgumentOutOfRangeException) { Write(a[0], 2); }
        return StepResult.Next;
    }

    // 0x1ac
    private StepResult OpCopyNumberedSave(Instruction ins, int pc)
    {
        var a = ins.Args;
        try
        {
            Write(a[0], _nativeDatStore?.CopyNumberedPair(
                unchecked((int)Read(a[1])), unchecked((int)Read(a[2]))) ?? 2);
        }
        catch (ArgumentOutOfRangeException) { Write(a[0], 2); }
        catch (IOException) { Write(a[0], 2); }
        catch (UnauthorizedAccessException) { Write(a[0], 2); }
        return StepResult.Next;
    }

    // 0x1ad
    private StepResult OpMarkSaveResumeFrame(Instruction ins, int pc)
    {
        lock (_debugControlLock) _saveResumeFrame = _cur;
        return StepResult.Next;
    }

    // 0x1ae
    private StepResult OpWriteNumberedSaveThumbnail(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (_nativeDatStore == null)
        {
            Write(a[0], 1);
            return StepResult.Next;
        }
        try
        {
            var image = _host.CaptureSurfacePixels(unchecked((int)Read(a[2])));
            if (image == null)
            {
                Write(a[0], 2);
                return StepResult.Next;
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
        return StepResult.Next;
    }

    // 0x1af
    private StepResult OpLoadNumberedSaveThumbnail(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (_nativeDatStore == null)
        {
            Write(a[0], 1);
            return StepResult.Next;
        }
        try
        {
            byte[]? encoded =
                _nativeDatStore.LoadNumberedThumbnail(unchecked((int)Read(a[1])));
            if (encoded == null)
            {
                Write(a[0], 1);
                return StepResult.Next;
            }
            var image = NumberedThumbnailCodec.Decode(encoded);
            Write(a[0], _host.ReplaceSurfacePixels(unchecked((int)Read(a[2])), image) ? 0 : 2);
        }
        catch (ArgumentOutOfRangeException) { Write(a[0], 2); }
        catch (InvalidDataException) { Write(a[0], 2); }
        catch (OverflowException) { Write(a[0], 2); }
        catch (IOException) { Write(a[0], 1); }
        catch (UnauthorizedAccessException) { Write(a[0], 1); }
        return StepResult.Next;
    }

    // 0x1a2
    private StepResult OpStoreSharedProfileInt(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (!TryResolveSharedProfileCell(a[0], isString: false, out int address))
        {
            HaltReason ??= $"shared-profile-int-lvalue-type:{a[0].Type}";
            return StepResult.Halt;
        }
        _sharedProfile.StoreInteger(address, Read(a[0]));
        return StepResult.Next;
    }

    // 0x1a3
    private StepResult OpLoadSharedProfileInt(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (!TryResolveSharedProfileCell(a[0], isString: false, out int address))
        {
            HaltReason ??= $"shared-profile-int-lvalue-type:{a[0].Type}";
            return StepResult.Halt;
        }
        Write(a[0], _sharedProfile.LoadInteger(address));
        return StepResult.Next;
    }

    // 0x1a9
    private StepResult OpStoreSharedProfileString(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (!TryResolveSharedProfileCell(a[0], isString: true, out int address))
        {
            HaltReason ??= $"shared-profile-string-lvalue-type:{a[0].Type}";
            return StepResult.Halt;
        }
        _sharedProfile.StoreString(address, ReadStr(a[0]));
        return StepResult.Next;
    }

    // 0x1aa
    private StepResult OpLoadSharedProfileString(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (!TryResolveSharedProfileCell(a[0], isString: true, out int address))
        {
            HaltReason ??= $"shared-profile-string-lvalue-type:{a[0].Type}";
            return StepResult.Halt;
        }
        WriteStr(a[0], _sharedProfile.LoadString(address));
        return StepResult.Next;
    }
}
