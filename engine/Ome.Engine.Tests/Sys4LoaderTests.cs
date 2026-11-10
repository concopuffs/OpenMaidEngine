using Ome.Engine.Sys4;
using Xunit;

public class Sys4LoaderTests
{
    [Fact]
    [Trait("Category", "Workspace")]
    public void ParsesMenuBinLikeSys4load()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var s = Sys4Loader.Load(Path.Combine(Paths.Data1, "MENU.BIN"), table);
        // header F0..F5 from `sys4load MENU.BIN --summary`
        Assert.Equal(0x17, s.Header.LocalInt1);
        Assert.Equal(1, s.Header.LocalFloats);
        Assert.Equal(1, s.Header.LocalStrings1);
        Assert.Equal(2, s.Header.LocalInt2);
        Assert.Equal(1, s.Header.Unknown);
        Assert.Equal(1, s.Header.LocalStrings2);
        Assert.Equal(148, s.Instructions.Count);   // sys4load: 148 instructions
        Assert.Equal(3, s.Strings.Count);           // sys4load: 3 inline strings
        Assert.All(s.Instructions, ins => Assert.True(s.IndexByOffset.ContainsKey(ins.Offset)));
    }

    private static byte[] Synthetic(int f8, uint[] body, int f7 = 0)
    {
        var data = new byte[0x3c + body.Length * 4];
        System.Text.Encoding.ASCII.GetBytes("SYS4422 ").CopyTo(data, 0);
        BitConverter.GetBytes(f7).CopyTo(data, 8 + 7 * 4);
        BitConverter.GetBytes(f8).CopyTo(data, 8 + 8 * 4);
        for (int k = 0; k < body.Length; k++) BitConverter.GetBytes(body[k]).CopyTo(data, 0x3c + k * 4);
        return data;
    }

    // F8 ends the code and locates T1: a value past the body is a malformed script, reported as such before the
    // code decoder indexes with it.
    [Theory]
    [InlineData(1000)]
    [InlineData(-1)]
    public void ACodeEndPastTheBodyIsInvalidData(int f8)
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        Assert.Throws<InvalidDataException>(() => Sys4Loader.Parse(Synthetic(f8, [0x2]), table, "BAD.BIN"));
    }

    [Fact]
    public void AnOffsetTableEntryPastInt32IsInvalidData()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        // Code is one `ret` (0x2, no operands); T1 holds one entry at dword 1.
        Assert.Throws<InvalidDataException>(() =>
            Sys4Loader.Parse(Synthetic(1, [0x2, 0x80000000u], f7: 1), table, "BAD.BIN"));
    }
}
