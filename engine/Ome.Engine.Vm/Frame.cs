using Ome.Engine.Natives;

namespace Ome.Engine.Vm;

/// <summary>The bank an address names a cell of. <see cref="Global"/> is the integer bank for an integer pointer and
/// the string bank for a string pointer; a float cell has its own two spaces, so that a pointer keeps what it was
/// bound to.</summary>
public enum VmAddressSpace
{
    Global,
    GlobalFloat,
    LocalInteger,
    LocalFloat,
    LocalString,
}

public readonly record struct VmAddress(VmAddressSpace Space, int Address)
{
    public static VmAddress Global(int address) => new(VmAddressSpace.Global, address);
    public static VmAddress GlobalFloat(int address) => new(VmAddressSpace.GlobalFloat, address);
    public static VmAddress LocalInteger(int address) => new(VmAddressSpace.LocalInteger, address);
    public static VmAddress LocalFloat(int address) => new(VmAddressSpace.LocalFloat, address);
    public static VmAddress LocalString(int address) => new(VmAddressSpace.LocalString, address);
    public VmAddress Offset(long offset) => new(Space, checked(Address + (int)offset));
}

public sealed class Frame
{
    private readonly Dictionary<int, long> _integers = new();
    /// <summary>local-int, each cell in its one form (<see cref="ScriptMemory.IntegerCell"/>); written only through
    /// <see cref="SetInteger"/>.</summary>
    public IReadOnlyDictionary<int, long> I => _integers;
    public Dictionary<int, long> F = new();   // local-float (raw)
    public Dictionary<int, string> S = new(); // local-string
    public Dictionary<int, VmAddress> P = new();  // local-ptr (retains local/global address domain)
    public Dictionary<int, VmAddress> SP = new(); // local-string-ptr (same, for the string banks)
    public Dictionary<int, VmAddress> FP = new(); // local-float-ptr (AGE 4.38D; a float cell of a frame or of the globals)

    public void SetInteger(int index, long value) => _integers[index] = ScriptMemory.IntegerCell(value);
}
