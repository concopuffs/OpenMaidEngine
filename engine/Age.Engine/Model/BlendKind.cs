namespace Age.Engine.Model;

/// <summary>How an object's surface composites onto the canvas. Opaque = straight copy; Alpha = source-alpha
/// blend (fades); Additive = native SRCALPHA/ONE glow composition.</summary>
public enum BlendKind { Opaque, Alpha, Additive }
