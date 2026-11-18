using System;
using Godot;
using Ome.Engine.Frontend.Shell;
using Ome.Engine.Modding;
using Ome.Engine.Profiles;
using Ome.Engine.Vm;
using Ome.Mods.Packages;
using Ome.Scripting.Lua;

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
            CheckCompiledPaths();
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

    /// <summary>
    /// Code a release compiles with NativeAOT differently from the JIT the tests run under, reached without a game: the
    /// JSON and TOML read through source-generated metadata, and the Lua runtime. A package that breaks one fails here,
    /// in the release build, instead of in a player's game (docs/platform-portability.md, "Fewer files in a package").
    /// </summary>
    private static void CheckCompiledPaths()
    {
        if (GameProfileRegistry.BuiltIn.Profiles.Length < 2)
            throw new InvalidOperationException("the embedded game manifests did not read");
        if (ShellStrings.Embedded("en") is not { Count: > 0 })
            throw new InvalidOperationException("the shell's English strings did not read");
        foreach (string id in ShippedPackages.Ids)
        {
            using ModPackage? package = ShippedPackages.Open(id);
            if (package?.Id != id) throw new InvalidOperationException($"the shipped package {id} did not open");
        }
        if (ShippedPackages.Ids.Count == 0) throw new InvalidOperationException("the engine ships no packages");
        string snapshot = GameSession.FromJson(new GameSession().ToJson()).ToJson();
        if (!snapshot.StartsWith('{')) throw new InvalidOperationException("a session snapshot did not round-trip");
        using var lua = new LuaModRuntime();
        ModSourceCheck check = lua.Check(new ModSource("smoke", "smoke.lua",
            "local t = {} for i = 1, 3 do t[i] = i * i end return t[3]"));
        if (check.Errors.Count != 0) throw new InvalidOperationException($"Lua did not compile: {check.Errors[0].Message}");
    }
}
