using Ome.Engine.Natives;
using Ome.Engine.Persistence;

/// <summary>
/// The script memory native (Ome.Engine/Natives/ScriptMemory*.cs; engine layering milestone 9, plan addition 3) on its
/// own: the five banks, the engine's string bytes, the queues and stacks of ops 0x132 to 0x139, op 0x9's reset and the
/// banks' sections of a numbered save.
/// </summary>
public class ScriptMemoryTests
{
    [Fact]
    public void UnsetCellsReadZeroAndAPointerCellFallsBackToTheIntegerCell()
    {
        var memory = new ScriptMemory();
        memory.SetInteger(7, 0x1234);

        Assert.Equal(0, memory.Integer(3));
        Assert.Equal("", memory.String(3));
        Assert.Equal(0x1234, memory.IntegerPointer(7));   // never set as a pointer: the banks overlap
        memory.SetPointer(7, 0x40);
        Assert.Equal(0x40, memory.IntegerPointer(7));
        Assert.Equal(0x1234, memory.StringPointer(7));
    }

    [Fact]
    public void AStringsLengthIsItsCp932BytesUpToTheFirstNul()
    {
        var memory = new ScriptMemory();

        Assert.Equal(4, memory.ByteLength("魔王"));
        Assert.Equal(3, memory.ByteLength("abc\0魔王"));
    }

    [Fact]
    public void QueuesAndStacksReportTheirStatus()
    {
        var memory = new ScriptMemory();

        Assert.Equal(ScriptCollectionStatus.Uninitialized, memory.Enqueue(2, 5));
        Assert.Equal(ScriptCollectionStatus.OutOfRange, memory.ResetQueue(11));
        Assert.Equal(ScriptCollectionStatus.OutOfRange, memory.Push(-1, 5));
        Assert.Equal(ScriptCollectionStatus.Done, memory.ResetQueue(2));
        memory.Enqueue(2, 5);
        memory.Enqueue(2, 6);
        memory.Push(2, 7);   // a stack is ready from the start
        memory.Push(2, 8);

        Assert.Equal(ScriptCollectionStatus.Done, memory.TryDequeue(2, out bool taken, out int value));
        Assert.True(taken);
        Assert.Equal(5, value);
        memory.TryPop(2, out taken, out value);
        Assert.Equal(8, value);
        memory.TryDequeue(2, out _, out _);
        memory.TryDequeue(2, out taken, out _);
        Assert.False(taken);
    }

    [Fact]
    public void ASceneResetClearsTheBanksAndStacksButKeepsTheQueues()
    {
        var memory = new ScriptMemory();
        memory.SetInteger(1, 2);
        memory.SetFloat(1, 3);
        memory.SetString(1, "x");
        memory.SetPointer(1, 4);
        memory.SetStringPointer(1, 5);
        memory.ResetQueue(0);
        memory.Enqueue(0, 9);
        memory.Push(0, 9);

        memory.ResetScene();

        Assert.Empty(memory.Integers);
        Assert.Empty(memory.Floats);
        Assert.Empty(memory.Strings);
        Assert.Empty(memory.Pointers);
        Assert.Empty(memory.StringPointers);
        memory.TryPop(0, out bool popped, out _);
        Assert.False(popped);
        memory.TryDequeue(0, out bool dequeued, out int value);
        Assert.True(dequeued);
        Assert.Equal(9, value);
    }

    [Fact]
    public void TheBanksSaveAsDensePrefixesAndALoadReplacesThemWhole()
    {
        var memory = new ScriptMemory();
        memory.SetInteger(0, 1);
        memory.SetInteger(2, 3);
        memory.SetInteger(9, 99);   // past the saved prefix
        memory.SetString(1, "保存");
        memory.SetPointer(0, 0x30);
        var dimensions = new NativeSaveBankDimensions(4, 1, 2, 1, 1, 1);

        NativeNumberedSaveState saved = memory.WriteSaveRecords(NativeNumberedSaveCodec.Empty([]), dimensions);

        Assert.Equal([1, 0, 3, 0], saved.IntegerGlobals);
        Assert.Equal(["", "保存"], saved.StringGlobals);
        Assert.Equal([0x30], saved.PointerGlobals);

        var loaded = new ScriptMemory();
        loaded.SetInteger(9, 99);   // a load zeroes the whole bank first
        loaded.ReadSaveRecords(saved);

        Assert.Equal(new Dictionary<int, long> { [0] = 1, [2] = 3 }, loaded.Integers);
        Assert.Equal("保存", loaded.String(1));
        Assert.Equal(0x30, loaded.IntegerPointer(0));
        Assert.Empty(loaded.Floats);
    }
}
