using Ome.Engine.Diagnostics;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Persistence;

namespace Ome.Engine.Vm;

public sealed partial class VirtualMachine
{
    // 0x19d
    private StepResult OpIsCatalogResourceUnlocked(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], _saves.Profile.IsCatalogResourceUnlocked(Read(a[1])) ? 1 : 0);
        return StepResult.Next;
    }

    // 0x19e
    private StepResult OpSaveNumberedSlot(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], _saves.SaveNumbered(unchecked((int)Read(a[1])), _saveRecords));
        // Lua scripting spec §11: a mod's state that did not serialize under --lua-errors halt ends the run once the
        // save is written.
        if (_scripting?.TakeHalt() is { } halt)
        {
            HaltReason ??= halt;
            return StepResult.Halt;
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
        {
            // Lua scripting spec §5.7: the banks are the save's, so the session ends now; the next boundary builds the
            // next one. The status write below is part of the old instruction, which no hook of the old session sees.
            _scripting?.LoadedDataOnly();
            Write(a[0], 0);
        }
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
        _saves.CommitLoadedPlayTime();
        return StepResult.Next;
    }

    // 0xae
    private StepResult OpContinueSaveLoadStackRestore(Instruction ins, int pc)
    {
        if (_saves.RestoringFrames is not { } savedFrames || _restoreFrameIndex < 0)
            return StepResult.Next;
        NativeSavedScriptFrame saved = savedFrames[_restoreFrameIndex];
        bool terminal = _restoreFrameIndex == savedFrames.Count - 1;
        if (terminal)
        {
            // Native restores the saved top frame as the numbered-save boundary selected by
            // opcode 0x1ad. Re-establish that identity so a subsequent save excludes transient
            // SAVE/menu helper frames instead of serializing the currently open modal stack.
            _scene.SaveResumeFrame = _cur;
            _cur.RestoredSaveFrame = null;
            _saves.EndRestore();
            _restoreFrameIndex = -1;
            _scripting?.RestoreEnded();   // Lua scripting spec §11 step 5: restoring clears; ome.session.resumed is next
            return StepResult.Jump(ResolveTableOffset(_cur.Script, _cur.Script.ReadMessageOffsets, saved.ResumeIndex, pc + 1));
        }

        int parentIndex = _restoreFrameIndex;
        NativeSavedScriptFrame childSaved = savedFrames[parentIndex + 1];
        Script child = ResolveSavedScript(childSaved);
        _saves.CommitLoadedPlayTime();   // script_frame_restore_saved_layout for the child frame
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
        int result = _saves.QueryNumbered(unchecked((int)Read(a[1])), out NativeSaveMetadata? metadata);
        if (result == 0 && metadata != null)
        {
            Write(a[2], metadata.Timestamp.Year);
            Write(a[3], metadata.Timestamp.Month);
            Write(a[4], metadata.Timestamp.Day);
            Write(a[5], metadata.Timestamp.Hour);
            Write(a[6], metadata.Timestamp.Minute);
            Write(a[7], metadata.Timestamp.Second);
            Write(a[8], unchecked((int)metadata.AccumulatedPlaySeconds));
        }
        Write(a[0], result);
        return StepResult.Next;
    }

    // 0x1ab
    private StepResult OpDeleteNumberedSave(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], _saves.DeleteNumbered(unchecked((int)Read(a[1]))));
        return StepResult.Next;
    }

    // 0x1ac
    private StepResult OpCopyNumberedSave(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], _saves.CopyNumbered(unchecked((int)Read(a[1])), unchecked((int)Read(a[2]))));
        return StepResult.Next;
    }

    // 0x1ad
    private StepResult OpMarkSaveResumeFrame(Instruction ins, int pc)
    {
        _scene.SaveResumeFrame = _cur;
        return StepResult.Next;
    }

    // 0x1ae
    private StepResult OpWriteNumberedSaveThumbnail(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (DrawSurfaceSlotRefused(ins, Read(a[2])) is { } refused) return refused;
        Write(a[0], _saves.WriteThumbnail(_graphics, slot: unchecked((int)Read(a[1])), surface: unchecked((int)Read(a[2]))));
        return StepResult.Next;
    }

    // 0x1af
    private StepResult OpLoadNumberedSaveThumbnail(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (DrawSurfaceSlotRefused(ins, Read(a[2])) is { } refused) return refused;
        Write(a[0], _saves.LoadThumbnail(_graphics, slot: unchecked((int)Read(a[1])), surface: unchecked((int)Read(a[2]))));
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
        _saves.Profile.StoreInteger(address, Read(a[0]));
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
        Write(a[0], _saves.Profile.LoadInteger(address));
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
        _saves.Profile.StoreString(address, ReadStr(a[0]));
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
        WriteStr(a[0], _saves.Profile.LoadString(address));
        return StepResult.Next;
    }
}
