namespace Ome.Engine.Model;

/// <summary>
/// The graphics rules an engine build gives the graphics natives (docs/superpowers/specs/2026-11-12-engine-layering-design.md
/// §7.3). The builds that differ are declared in <c>Ome.Engine.Vm/EngineBuilds.cs</c>.
/// </summary>
/// <param name="UnboundObjectReadsAbsent">Whether op 0x215's object query reads an object that no draw-texture has
/// bound as absent (-1) instead of its source slot. 4.33G's worker <c>0x486120</c> tests bit 0 of the record first;
/// 4.24J's returns the slot (docs/engine-re.md, "SYS4433 reads a draw-unbound object as absent";
/// <see cref="GfxState.QuerySlot"/>).</param>
/// <param name="SpriteAlphaTest">Whether an object's draw keeps the alpha test D3DX's sprite turns on at
/// <c>Begin(D3DXSPRITE_ALPHABLEND)</c>, so a texel whose alpha is 0 is not written. Only a copy (colour mode 2 into a
/// kind-1 render target, <see cref="BlendKind.Copy"/>) shows it; the other blends leave the target as it is for such a
/// texel (docs/engine-re.md, "Object blend modes").</param>
public sealed record GfxRules(bool UnboundObjectReadsAbsent, bool SpriteAlphaTest = true);
