namespace Ome.Engine.Model;

/// <summary>How an object's surface composites onto the canvas. Opaque = straight copy; Alpha = source-alpha
/// blend (fades); Additive = native SRCALPHA/ONE glow composition; Subtractive = native mode 3, SRCALPHA/ONE with the
/// reverse-subtract operation. Copy and CopyAll are native mode 2 drawn while a kind-1 surface is the render target
/// (ONE/ZERO: the source replaces the target, alpha included); Copy keeps out the texels of alpha 0, as the alpha test
/// D3DX's sprite turns on does, and CopyAll, for a build that turns the test off, writes them too. Only a render
/// target's drawing makes the two copies (docs/engine-re.md, "Object blend modes").</summary>
public enum BlendKind { Opaque, Alpha, Additive, Subtractive, Copy, CopyAll }
