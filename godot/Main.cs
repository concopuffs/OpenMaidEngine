using Godot;
using Age.Engine.Sys4;

public partial class Main : Godot.Control
{
    public override void _Ready()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = Sys4Loader.Load(Paths.Scripts()["SC0000.BIN"], table);
        GD.Print($"SMOKE repo={Paths.Repo}");
        GD.Print($"SMOKE SC0000 instrs={script.Instructions.Count} strings={script.Strings.Count}");
        GetTree().Quit(0);
    }
}
