using Ome.Engine.Model;
using Ome.Engine.Natives;

namespace Ome.Engine.Vm;

public sealed partial class VirtualMachine
{
    // 0x2c5: raw strlen(native encoded bytes)
    private StepResult OpByteStringLength(Instruction ins, int pc)
    {
        var a = ins.Args;
        string text = ReadStr(a[1]);
        Write(a[0], PresentedLength(a[1], text, pc, Localization.TextLengthKind.HalfCells)
                    ?? NativeStringByteLength(text));
        return StepResult.Next;
    }

    private StepResult OpLookupArray(Instruction ins, int pc)
    {
        var a = ins.Args;
        LookupStore(a[0], BaseAddr(a[1]).Offset(Read(a[2])), pc); return StepResult.Next;
    }

    private StepResult OpLookupArray2d(Instruction ins, int pc)
    {
        var a = ins.Args;
        LookupStore(a[0], BaseAddr(a[1]).Offset(Read(a[2]) * Read(a[3]) + Read(a[4])), pc); return StepResult.Next;
    }

    // 0x63: pointer destination <- underlying address of operand 2
    private StepResult OpTakeAddress(Instruction ins, int pc)
    {
        var a = ins.Args;
        if (!TryStoreAddress(a[0], BaseAddr(a[1])))
        {
            HaltReason ??= $"take-address-destination-type:{a[0].Type}";
            return StepResult.Halt;
        }
        return StepResult.Next;
    }

    // 0x64: count dword followed by plain file values
    private StepResult OpCopyInlineIntArray(Instruction ins, int pc)
    {
        var a = ins.Args;
        int offset = checked((int)Read(a[1]));
        if ((uint)offset >= (uint)_cur.Script.BodyDwords.Count)
        {
            HaltReason ??= $"inline-array-offset@0x{offset:x}";
            return StepResult.Halt;
        }
        uint rawCount = _cur.Script.BodyDwords[offset];
        int available = _cur.Script.BodyDwords.Count - offset - 1;
        if (rawCount > (uint)available)
        {
            HaltReason ??= $"inline-array-length@0x{offset:x}:{rawCount}";
            return StepResult.Halt;
        }
        int count = (int)rawCount;
        for (int i = 0; i < count; i++)
            WriteConsecutive(a[0], i, unchecked((int)_cur.Script.BodyDwords[offset + 1 + i]));
        return StepResult.Next;
    }

    // 0x1b0: memcpy(count * 4) across resolved integer-cell spans
    private StepResult OpCopyDwords(Instruction ins, int pc)
    {
        var a = ins.Args;
        int count = checked((int)Read(a[2]));
        if (count < 0)
        {
            HaltReason ??= $"copy-dwords-negative-count:{count}";
            return StepResult.Halt;
        }
        if (count > MaxScriptRunLength)
        {
            HaltReason ??= $"copy-dwords-count-too-large:{count}";
            return StepResult.Halt;
        }
        VmAddress source = BaseAddr(a[0]);
        VmAddress destination = BaseAddr(a[1]);
        var values = new long[count];
        for (int i = 0; i < count; i++)
            values[i] = unchecked((int)ReadIntCell(source.Offset(i)));
        for (int i = 0; i < count; i++) WriteIntCell(destination.Offset(i), values[i]);
        return StepResult.Next;
    }

    // 0x2d8: fill count consecutive resolved integer cells
    private StepResult OpFillIntArray(Instruction ins, int pc)
    {
        var a = ins.Args;
        VmAddress destination = BaseAddr(a[0]);
        int value = unchecked((int)Read(a[1]));
        int count = unchecked((int)Read(a[2]));
        if (count > MaxScriptRunLength)
        {
            HaltReason ??= $"fill-int-array-count-too-large:{count}";
            return StepResult.Halt;
        }
        for (int i = 0; i < count; i++)
            WriteIntCell(destination.Offset(i), value);
        return StepResult.Next;
    }

    // 0x12e: inclusive rectangle intersection over addressed arrays
    private StepResult OpFindHitRectangle(Instruction ins, int pc)
    {
        var a = ins.Args;
        int previous = (int)Read(a[0]);
        int count = System.Math.Max(0, (int)Read(a[7]));
        long refLeft = ReadAddressedCell(a[1], 0);
        long refRight = ReadAddressedCell(a[1], 1);
        long refTop = ReadAddressedCell(a[1], 2);
        long refBottom = ReadAddressedCell(a[1], 3);
        int match = -1;
        for (int index = previous + 1; index < count; index++)
        {
            long x = Read(a[2]) - ReadAddressedCell(a[5], index);
            long y = Read(a[3]) - ReadAddressedCell(a[6], index);
            long left = ReadAddressedCell(a[4], index * 4);
            long right = ReadAddressedCell(a[4], index * 4 + 1);
            long top = ReadAddressedCell(a[4], index * 4 + 2);
            long bottom = ReadAddressedCell(a[4], index * 4 + 3);
            bool isReferenceRectangle = AddressedCellIdentity(a[1], 0)
                == AddressedCellIdentity(a[4], index * 4);
            if (!isReferenceRectangle
                && x + refLeft <= right && x + refRight >= left
                && y + refTop <= bottom && y + refBottom >= top)
            {
                match = index;
                break;
            }
        }
        Write(a[0], match);
        return StepResult.Next;
    }

    // 0x12f: stable ascending permutation by signed key sum
    private StepResult OpSortIndicesByKeySum(Instruction ins, int pc)
    {
        var a = ins.Args;
        VmAddress output = BaseAddr(a[0]);

        // Native writes element zero even when count is zero or negative, then builds the
        // permutation in place with insertion sort. Read the count for each outer iteration:
        // the handler fetches operand 4 repeatedly rather than caching it.
        WriteIntCell(output, 0);
        for (int sourceIndex = 1; sourceIndex < unchecked((int)Read(a[3])); sourceIndex++)
        {
            int position = sourceIndex;
            while (position > 0)
            {
                int previousIndex = unchecked((int)ReadIntCell(output.Offset(position - 1)));
                int previousKey = unchecked(
                    unchecked((int)ReadAddressedCell(a[1], previousIndex))
                    + unchecked((int)ReadAddressedCell(a[2], previousIndex)));
                int sourceKey = unchecked(
                    unchecked((int)ReadAddressedCell(a[1], sourceIndex))
                    + unchecked((int)ReadAddressedCell(a[2], sourceIndex)));
                if (sourceKey >= previousKey) break;

                WriteIntCell(output.Offset(position), previousIndex);
                position--;
            }
            WriteIntCell(output.Offset(position), sourceIndex);
        }
        return StepResult.Next;
    }

    // 0x147: Win32 ALTERNATE-fill polygon membership
    private StepResult OpPointInPolygon(Instruction ins, int pc)
    {
        var a = ins.Args;
        int pointX = unchecked((int)Read(a[1]));
        int pointY = unchecked((int)Read(a[2]));
        int count = unchecked((int)Read(a[5]));
        bool inside = false;

        // This is the standard half-open even/odd crossing rule used by a GDI alternate-fill
        // region: top/left edges of an axis-aligned rectangle are included; bottom/right are not.
        if (count >= 3)
        {
            int previous = count - 1;
            for (int current = 0; current < count; current++)
            {
                int currentX = unchecked((int)ReadAddressedCell(a[3], current));
                int currentY = unchecked((int)ReadAddressedCell(a[4], current));
                int previousX = unchecked((int)ReadAddressedCell(a[3], previous));
                int previousY = unchecked((int)ReadAddressedCell(a[4], previous));
                if ((currentY > pointY) != (previousY > pointY)
                    && pointX < ((double)previousX - currentX) * ((double)pointY - currentY)
                                / ((double)previousY - currentY) + currentX)
                    inside = !inside;
                previous = current;
            }
        }

        Write(a[0], inside ? 1 : 0);
        return StepResult.Next;
    }

    // 0x132: 11 safe logical slots; native's admitted id 10 aliases stack 0
    private StepResult OpResetIntQueue(Instruction ins, int pc)
    {
        int queueId = unchecked((int)Read(ins.Args[0]));
        return CollectionStep(_memory.ResetQueue(queueId), "queue", queueId);
    }

    // 0x133 (queue_id, value)
    private StepResult OpEnqueueInt(Instruction ins, int pc)
    {
        var a = ins.Args;
        int queueId = unchecked((int)Read(a[0]));
        return CollectionStep(_memory.Enqueue(queueId, unchecked((int)Read(a[1]))), "queue", queueId);
    }

    // 0x134 (queue_id, out_success, out_value)
    private StepResult OpTryDequeueInt(Instruction ins, int pc)
    {
        var a = ins.Args;
        int queueId = unchecked((int)Read(a[0]));
        ScriptCollectionStatus status = _memory.TryDequeue(queueId, out bool taken, out int value);
        if (status != ScriptCollectionStatus.Done) return CollectionStep(status, "queue", queueId);
        if (taken)
        {
            Write(a[1], 1);
            Write(a[2], value);
        }
        else
        {
            // Native writes success=0 and an implementation pointer to operand 3. Shipped
            // callers branch on success before reading it, so retain the prior destination.
            Write(a[1], 0);
        }
        return StepResult.Next;
    }

    // 0x137 (stack_id)
    private StepResult OpResetIntStack(Instruction ins, int pc)
    {
        int stackId = unchecked((int)Read(ins.Args[0]));
        return CollectionStep(_memory.ResetStack(stackId), "stack", stackId);
    }

    // 0x138 (stack_id, value)
    private StepResult OpPushIntStack(Instruction ins, int pc)
    {
        var a = ins.Args;
        int stackId = unchecked((int)Read(a[0]));
        return CollectionStep(_memory.Push(stackId, unchecked((int)Read(a[1]))), "stack", stackId);
    }

    // 0x139 (stack_id, out_success, out_value)
    private StepResult OpTryPopIntStack(Instruction ins, int pc)
    {
        var a = ins.Args;
        int stackId = unchecked((int)Read(a[0]));
        ScriptCollectionStatus status = _memory.TryPop(stackId, out bool taken, out int value);
        if (status != ScriptCollectionStatus.Done) return CollectionStep(status, "stack", stackId);
        if (taken)
        {
            Write(a[1], 1);
            Write(a[2], value);
        }
        else
        {
            // Native writes success=0 and leaks an internal EngineCtx pointer through
            // operand 3. Preserve the destination instead of exposing host garbage.
            Write(a[1], 0);
        }
        return StepResult.Next;
    }

    /// <summary>An integer queue or stack op's step: an id out of range, or a queue never reset, halts the run.
    /// </summary>
    private StepResult CollectionStep(ScriptCollectionStatus status, string kind, int id)
    {
        switch (status)
        {
            case ScriptCollectionStatus.OutOfRange:
                HaltReason ??= $"int-{kind}-id-out-of-range:{id}";
                return StepResult.Halt;
            case ScriptCollectionStatus.Uninitialized:
                HaltReason ??= $"int-{kind}-uninitialized:{id}";
                return StepResult.Halt;
            default:
                return StepResult.Next;
        }
    }

    private StepResult OpBitSet(Instruction ins, int pc)
    {
        var a = ins.Args;
        long bit = Read(a[1]);
        if ((ulong)bit >= 32) { HaltReason ??= $"bit-index-out-of-range:{bit}"; return StepResult.Halt; }
        Write(a[0], I32(Read(a[0]) | (1L << (int)bit))); return StepResult.Next;
    }

    private StepResult OpBitReset(Instruction ins, int pc)
    {
        var a = ins.Args;
        long bit = Read(a[1]);
        if ((ulong)bit >= 32) { HaltReason ??= $"bit-index-out-of-range:{bit}"; return StepResult.Halt; }
        Write(a[0], I32(Read(a[0]) & ~(1L << (int)bit))); return StepResult.Next;
    }

    private StepResult OpCheckBit(Instruction ins, int pc)
    {
        var a = ins.Args;
        Write(a[0], (Read(a[1]) >> (int)(Read(a[2]) & 31)) & 1); return StepResult.Next;
    }

    private StepResult OpZeroIntRange(Instruction ins, int pc)
    {
        var a = ins.Args;
        int count = System.Math.Max(0, checked((int)Read(a[1])));
        for (int i = 0; i < count; i++) WriteConsecutive(a[0], i, 0);
        return StepResult.Next;
    }

    // 0x60: native CRT rand() % bound
    private StepResult OpRandomModulo(Instruction ins, int pc)
    {
        var a = ins.Args;
        // Native draws before it reads the bound, so a zero bound still spends a draw.
        Instruction site = _cur.Script.Instructions[pc];
        int drawn = _memory.DrawRandom(_cur.Script.Name, _cur.Script.PackedId, site.Offset);
        long bound = Read(a[1]);
        if (bound == 0)
        {
            Write(a[0], 0);
            HaltReason ??= "random-modulo-zero";
            return StepResult.Halt;
        }
        Write(a[0], drawn % bound);
        return StepResult.Next;
    }
}
