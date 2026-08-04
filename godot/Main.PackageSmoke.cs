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
            var table = HimegariRuntimeMetadata.LoadOpcodeTable();
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
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"[package-smoke] {error}");
            GetTree().Quit(1);
        }
    }
}
