using Age.Engine.Model;

namespace Age.Engine.Vm;

public sealed partial class VirtualMachine
{
    private int StepMemoryCollection(string label, IReadOnlyList<Operand> a, int pc)
    {
        switch (label)
        {
            case "strlen": // 0x2c5: raw strlen(native encoded bytes)
                Write(a[0], NativeStringByteLength(ReadStr(a[1])));
                return pc + 1;
            case "lookup-array":
                LookupStore(a[0], BaseAddr(a[1]).Offset(Read(a[2]))); return pc + 1;
            case "lookup-array-2d":
                LookupStore(a[0], BaseAddr(a[1]).Offset(Read(a[2]) * Read(a[3]) + Read(a[4]))); return pc + 1;
            case "take-address": // 0x63: pointer destination <- underlying address of operand 2
                if (!TryStoreAddress(a[0], BaseAddr(a[1])))
                {
                    HaltReason ??= $"take-address-destination-type:{a[0].Type}";
                    return HALT;
                }
                return pc + 1;
            case "copy-inline-int-array": // 0x64: count dword followed by plain file values
            {
                int offset = checked((int)Read(a[1]));
                if ((uint)offset >= (uint)_cur.Script.BodyDwords.Count)
                {
                    HaltReason ??= $"inline-array-offset@0x{offset:x}";
                    return HALT;
                }
                uint rawCount = _cur.Script.BodyDwords[offset];
                int available = _cur.Script.BodyDwords.Count - offset - 1;
                if (rawCount > (uint)available)
                {
                    HaltReason ??= $"inline-array-length@0x{offset:x}:{rawCount}";
                    return HALT;
                }
                int count = (int)rawCount;
                for (int i = 0; i < count; i++)
                    WriteConsecutive(a[0], i, unchecked((int)_cur.Script.BodyDwords[offset + 1 + i]));
                return pc + 1;
            }
            case "copy-dwords": // 0x1b0: memcpy(count * 4) across resolved integer-cell spans
            {
                int count = checked((int)Read(a[2]));
                if (count < 0)
                {
                    HaltReason ??= $"copy-dwords-negative-count:{count}";
                    return HALT;
                }
                VmAddress source = BaseAddr(a[0]);
                VmAddress destination = BaseAddr(a[1]);
                var values = new long[count];
                for (int i = 0; i < count; i++)
                    values[i] = unchecked((int)ReadIntCell(source.Offset(i)));
                for (int i = 0; i < count; i++) WriteIntCell(destination.Offset(i), values[i]);
                return pc + 1;
            }
            case "find-hit-rectangle": // 0x12e: inclusive rectangle intersection over addressed arrays
            case "u0041E940":
            {
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
                return pc + 1;
            }
            case "sort-indices-by-key-sum": // 0x12f: stable ascending permutation by signed key sum
            case "u0041ECB0":
            {
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
                return pc + 1;
            }
            case "u0041EF00":
            case "reset-int-queue": // 0x132: 11 safe logical slots; native's admitted id 10 aliases stack 0
            {
                int queueId = unchecked((int)Read(a[0]));
                if ((uint)queueId >= (uint)_intQueues.Length)
                {
                    HaltReason ??= $"int-queue-id-out-of-range:{queueId}";
                    return HALT;
                }
                _intQueues[queueId] = new Queue<int>(0x100);
                return pc + 1;
            }
            case "u0041EFF0":
            case "enqueue-int": // 0x133 (queue_id, value)
            {
                int queueId = unchecked((int)Read(a[0]));
                if ((uint)queueId >= (uint)_intQueues.Length)
                {
                    HaltReason ??= $"int-queue-id-out-of-range:{queueId}";
                    return HALT;
                }
                if (_intQueues[queueId] is not { } queue)
                {
                    HaltReason ??= $"int-queue-uninitialized:{queueId}";
                    return HALT;
                }
                queue.Enqueue(unchecked((int)Read(a[1])));
                return pc + 1;
            }
            case "u0041F050":
            case "try-dequeue-int": // 0x134 (queue_id, out_success, out_value)
            {
                int queueId = unchecked((int)Read(a[0]));
                if ((uint)queueId >= (uint)_intQueues.Length)
                {
                    HaltReason ??= $"int-queue-id-out-of-range:{queueId}";
                    return HALT;
                }
                if (_intQueues[queueId] is not { } queue)
                {
                    HaltReason ??= $"int-queue-uninitialized:{queueId}";
                    return HALT;
                }
                if (queue.TryDequeue(out int value))
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
                return pc + 1;
            }
            case "u0041F1C0":
            case "reset-int-stack": // 0x137 (stack_id)
            {
                int stackId = unchecked((int)Read(a[0]));
                if ((uint)stackId >= (uint)_intStacks.Length)
                {
                    HaltReason ??= $"int-stack-id-out-of-range:{stackId}";
                    return HALT;
                }
                _intStacks[stackId] = new Stack<int>(0x100);
                return pc + 1;
            }
            case "u0041F2B0":
            case "push-int-stack": // 0x138 (stack_id, value)
            {
                int stackId = unchecked((int)Read(a[0]));
                if ((uint)stackId >= (uint)_intStacks.Length)
                {
                    HaltReason ??= $"int-stack-id-out-of-range:{stackId}";
                    return HALT;
                }
                _intStacks[stackId].Push(unchecked((int)Read(a[1])));
                return pc + 1;
            }
            case "u0041F310":
            case "try-pop-int-stack": // 0x139 (stack_id, out_success, out_value)
            {
                int stackId = unchecked((int)Read(a[0]));
                if ((uint)stackId >= (uint)_intStacks.Length)
                {
                    HaltReason ??= $"int-stack-id-out-of-range:{stackId}";
                    return HALT;
                }
                if (_intStacks[stackId].TryPop(out int value))
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
                return pc + 1;
            }
            case "bit-set":
            {
                long bit = Read(a[1]);
                if ((ulong)bit >= 32) { HaltReason ??= $"bit-index-out-of-range:{bit}"; return HALT; }
                Write(a[0], Read(a[0]) | (1L << (int)bit)); return pc + 1;
            }
            case "bit-reset":
            {
                long bit = Read(a[1]);
                if ((ulong)bit >= 32) { HaltReason ??= $"bit-index-out-of-range:{bit}"; return HALT; }
                Write(a[0], Read(a[0]) & ~(1L << (int)bit)); return pc + 1;
            }
            case "check-bit": Write(a[0], (Read(a[1]) >> (int)(Read(a[2]) & 31)) & 1); return pc + 1;
            case "zero-int-range":
            case "copy-to-global": // pre-reference compatibility for opcode 0x6c
            {
                int count = System.Math.Max(0, checked((int)Read(a[1])));
                for (int i = 0; i < count; i++) WriteConsecutive(a[0], i, 0);
                return pc + 1;
            }
            case "random-modulo": // 0x60: native CRT rand() % bound
            case "u0041A270":
            {
                long bound = Read(a[1]);
                if (bound == 0)
                {
                    Write(a[0], 0);
                    HaltReason ??= "random-modulo-zero";
                    return HALT;
                }
                Write(a[0], System.Random.Shared.Next(0x8000) % bound);
                return pc + 1;
            }
            default:
                throw new InvalidOperationException($"Non-memory/collection opcode routed to memory/collection handler: {label}");
        }
    }
}
