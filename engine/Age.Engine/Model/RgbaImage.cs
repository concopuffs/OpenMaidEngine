namespace Age.Engine.Model;

/// <summary>A decoded, tightly packed, top-down RGBA8 image.</summary>
public sealed record RgbaImage(int Width, int Height, byte[] Pixels);
