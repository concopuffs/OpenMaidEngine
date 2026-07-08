namespace Age.Engine.Model;

/// <summary>How an object's surface composites onto the canvas. Opaque = straight copy; Alpha = source-alpha
/// blend (fades). Additive (glow/flash, native blit mode 2/3) is a documented seam — NOT implemented in the
/// blend/transparency slice; see docs/superpowers/specs/2026-07-08-blend-transparency-design.md.</summary>
public enum BlendKind { Opaque, Alpha, Additive }
