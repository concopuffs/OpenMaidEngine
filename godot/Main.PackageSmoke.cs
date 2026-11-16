using System;
using Godot;

public partial class Main
{
    private void RunPackageSmoke()
    {
        try
        {
            if (!OS.HasFeature("linux") && !OS.HasFeature("windows"))
                throw new PlatformNotSupportedException(
                    "the packaged-runtime smoke gate supports Linux and Windows x64");
            var table = OmeRuntimeMetadata.LoadOpcodeTable();
            if (table.Count != 548)
                throw new InvalidOperationException(
                    $"embedded opcode table has {table.Count} entries; expected 548");
            if (!table.TryGet(0x55, out string label, out int argc)
                || label != "mov" || argc != 2)
                throw new InvalidOperationException("embedded opcode 0x55 metadata is invalid");
            uint ffmpegAbi = FfmpegMovieNative.AbiVersion();
            if (ffmpegAbi != 3)
                throw new InvalidOperationException(
                    $"native FFmpeg shim ABI is {ffmpegAbi}; expected 3");
            GD.Print($"PACKAGE SMOKE OK: opcodes={table.Count} ffmpeg-abi={ffmpegAbi}");
            // --hold keeps the engine running until its standard input closes, so the release smoke can check that the
            // line reached its pipe while the engine ran (launcher plan §4.12: project.godot's flush_stdout_on_print).
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--hold") >= 0) Console.In.ReadToEnd();
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"[package-smoke] {error}");
            GetTree().Quit(1);
        }
    }
}
