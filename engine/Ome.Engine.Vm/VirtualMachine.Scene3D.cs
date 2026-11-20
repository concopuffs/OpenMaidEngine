using Ome.Engine.Model;
using Ome.Engine.Natives;

namespace Ome.Engine.Vm;

// AGE 4.38D's 3D scene, ops 0x324 to 0x32f (docs/superpowers/specs/2026-11-19-polygon-objects-design.md §4): mesh
// slots, a camera, lights, and the display's three effects, the rain, the snow and the leaves. Each handler reads its
// operands as native's does and calls the graphics native, which keeps the state and draws the effects in the frame
// lowering (docs/superpowers/specs/2026-11-20-title-leaves-design.md; docs/engine-re.md, "The 3D scene").
public sealed partial class VirtualMachine
{
    // 0x324: release the display's three effects
    private StepResult OpRelease3DEffects(Instruction ins, int pc)
    {
        _graphics.ReleaseEffects(); return StepResult.Next;
    }

    // 0x325 (first handle)(second handle): where the frame walk draws the effects
    private StepResult OpSet3DEffectHandles(Instruction ins, int pc)
    {
        var a = ins.Args;
        _graphics.SetEffectHandles(unchecked((int)Read(a[0])), unchecked((int)Read(a[1])));
        return StepResult.Next;
    }

    // 0x326 (flakes)(surface slot)(speed, a float)(fade): Set3DEffectSnow. Native indexes the surface table with the
    // slot unchecked, and traces a slot that holds no texture; the worker builds the effect with that many flakes.
    private StepResult OpSet3DEffectSnow(Instruction ins, int pc)
    {
        var a = ins.Args;
        int flakes = unchecked((int)Read(a[0]));
        long slot = Read(a[1]);
        if (SurfaceSlotRefused(ins, slot) is { } refused) return refused;
        if (flakes > MaxScriptRunLength)
        {
            HaltReason ??= $"set-3d-effect-snow-flakes-too-many:{flakes}";
            return StepResult.Halt;
        }
        if (!_graphics.SetSnowEffect(flakes, (int)slot, ReadFloat(a[2]), Read(a[3])))
            _waits.ReportWarning($"{At(ins)}: surface slot {slot} holds no texture; skipped, as AGE skips it with the "
                                 + $"trace 「関数：Set3DEffectSnow エラー：テクスチャが作成されていません． TEXTURE={slot}」");
        return StepResult.Next;
    }

    // 0x327 (drops): build the display's first effect, the rain, with that many drops
    private StepResult OpSet3DEffectRain(Instruction ins, int pc)
    {
        int drops = unchecked((int)Read(ins.Args[0]));
        if (drops > MaxScriptRunLength)
        {
            HaltReason ??= $"set-3d-effect-rain-drops-too-many:{drops}";
            return StepResult.Halt;
        }
        _graphics.SetRainEffect(drops);
        return StepResult.Next;
    }

    // 0x328 (leaves)(mesh slots)(slot count): Set3DEffectLeaf. The handler copies slot-count integer cells from the
    // array operand's address; the worker builds the effect with that many leaves (native allocates them), indexes the
    // mesh table with each slot, unchecked, and traces a slot that holds no mesh.
    private StepResult OpSet3DEffectLeaf(Instruction ins, int pc)
    {
        var a = ins.Args;
        int leaves = unchecked((int)Read(a[0]));
        int count = unchecked((int)Read(a[2]));
        if (count > MaxScriptRunLength)
        {
            HaltReason ??= $"set-3d-effect-leaf-count-too-large:{count}";
            return StepResult.Halt;
        }
        if (leaves > MaxScriptRunLength)
        {
            HaltReason ??= $"set-3d-effect-leaf-leaves-too-many:{leaves}";
            return StepResult.Halt;
        }
        VmAddress array = BaseAddr(a[1]);
        var slots = new int[System.Math.Max(0, count)];
        for (int index = 0; index < slots.Length; index++)
        {
            long slot = unchecked((int)ReadIntCell(array.Offset(index)));
            if (IndexRefused(ins, "mesh slot", slot, Scene3DState.MeshSlotCount, IndexTest.Unchecked) is { } refused)
                return refused;
            slots[index] = (int)slot;
        }
        foreach (int empty in _graphics.SetLeafEffect(leaves, slots))
            _waits.ReportWarning($"{At(ins)}: mesh slot {empty} holds no mesh; skipped, as AGE skips it with the trace "
                                 + $"「関数：Set3DEffectLeaf エラー：メッシュが作成されていません． MESH={empty}」");
        return StepResult.Next;
    }

    // 0x329 (file id)(mesh slot): load a Direct3D .X file into a mesh slot. A file that does not load throws
    // 「メッシュファイル %s の読み込みに失敗しました」 with flags 0x10007; the slot is indexed unchecked.
    private StepResult OpLoadMesh(Instruction ins, int pc)
    {
        var a = ins.Args;
        long slot = Read(a[1]);
        if (IndexRefused(ins, "mesh slot", slot, Scene3DState.MeshSlotCount, IndexTest.Unchecked) is { } refused)
            return refused;
        if (!_graphics.LoadMesh(Read(a[0]), (int)slot, out string name))
            return StepResult.Jump(RaiseInstructionScriptError(
                AgeBoxMessage.Keyed(AgeBoxText.MeshLoadFailed, name), pc));
        return StepResult.Next;
    }

    // 0x32a (mesh slot): release a mesh slot
    private StepResult OpReleaseMesh(Instruction ins, int pc)
    {
        long slot = Read(ins.Args[0]);
        if (IndexRefused(ins, "mesh slot", slot, Scene3DState.MeshSlotCount, IndexTest.Unchecked) is { } refused)
            return refused;
        _graphics.ReleaseMesh((int)slot);
        return StepResult.Next;
    }

    // 0x32b: release all 1,000 mesh slots
    private StepResult OpReleaseAllMeshes(Instruction ins, int pc)
    {
        _graphics.ReleaseAllMeshes(); return StepResult.Next;
    }

    // 0x32c (eye x)(eye y)(eye z)(target x)(target y)(target z): the camera, six floats
    private StepResult OpSetCameraLookAt(Instruction ins, int pc)
    {
        var a = ins.Args;
        _graphics.SetCameraLookAt((ReadFloat(a[0]), ReadFloat(a[1]), ReadFloat(a[2])),
                                  (ReadFloat(a[3]), ReadFloat(a[4]), ReadFloat(a[5])));
        return StepResult.Next;
    }

    // 0x32d (alpha)(rgb): the ambient light colour; an alpha above 0xff is 0xff
    private StepResult OpSetAmbientLight(Instruction ins, int pc)
    {
        var a = ins.Args;
        _graphics.SetAmbientLight(LightColor(Read(a[0]), Read(a[1])));
        return StepResult.Next;
    }

    // 0x32e (index)(type)(position x y z)(direction x y z)(alpha)(rgb)(range): set a light and enable it. Native
    // indexes its ten light records with the index unchecked.
    private StepResult OpSetLight(Instruction ins, int pc)
    {
        var a = ins.Args;
        long index = Read(a[0]);
        if (IndexRefused(ins, "light", index, Scene3DState.LightCount, IndexTest.Unchecked) is { } refused)
            return refused;
        _graphics.SetLight((int)index, unchecked((int)Read(a[1])),
                           (ReadFloat(a[2]), ReadFloat(a[3]), ReadFloat(a[4])),
                           (ReadFloat(a[5]), ReadFloat(a[6]), ReadFloat(a[7])),
                           LightColor(Read(a[8]), Read(a[9])), ReadFloat(a[10]));
        return StepResult.Next;
    }

    // 0x32f (index): disable a light
    private StepResult OpDisableLight(Instruction ins, int pc)
    {
        long index = Read(ins.Args[0]);
        if (IndexRefused(ins, "light", index, Scene3DState.LightCount, IndexTest.Unchecked) is { } refused)
            return refused;
        _graphics.DisableLight((int)index);
        return StepResult.Next;
    }

    // The colour ops 0x32d and 0x32e build: an alpha above 0xff is 0xff, then its low byte over the RGB's three.
    private static uint LightColor(long alpha, long rgb)
    {
        int a = unchecked((int)alpha);
        if (a > 0xff) a = 0xff;
        return (unchecked((uint)a) & 0xff) << 24 | unchecked((uint)rgb) & 0xffffff;
    }
}
