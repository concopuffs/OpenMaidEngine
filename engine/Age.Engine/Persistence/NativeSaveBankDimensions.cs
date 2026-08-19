namespace Age.Engine.Persistence;

/// <summary>
/// Dense VM-bank dimensions serialized by AGE's numbered-save layout. These are game/profile data,
/// not universal SYS4 constants.
/// </summary>
public sealed record NativeSaveBankDimensions
{
    public NativeSaveBankDimensions(
        int integerGlobals,
        int floatGlobals,
        int stringGlobals,
        int pointerGlobals,
        int pointerStrings,
        int localPointerScratch)
    {
        int[] values =
        [
            integerGlobals, floatGlobals, stringGlobals,
            pointerGlobals, pointerStrings, localPointerScratch,
        ];
        if (values.Any(value => value < 0))
            throw new ArgumentOutOfRangeException(
                nameof(integerGlobals), "native save bank dimensions cannot be negative");

        IntegerGlobals = integerGlobals;
        FloatGlobals = floatGlobals;
        StringGlobals = stringGlobals;
        PointerGlobals = pointerGlobals;
        PointerStrings = pointerStrings;
        LocalPointerScratch = localPointerScratch;
    }

    public int IntegerGlobals { get; }
    public int FloatGlobals { get; }
    public int StringGlobals { get; }
    public int PointerGlobals { get; }
    public int PointerStrings { get; }
    public int LocalPointerScratch { get; }

    public int[] ToArray()
        =>
        [
            IntegerGlobals, FloatGlobals, StringGlobals,
            PointerGlobals, PointerStrings, LocalPointerScratch,
        ];
}
