using System;
using System.IO;
using Godot;

/// <summary>
/// The engine shell's own assets, embedded in the OME assembly from godot/assets/ (which Godot ignores), so development
/// runs and exports load them alike: Noto Sans JP for the options panel (docs/superpowers/specs/2026-11-09-engine-shell-design.md,
/// §7.1, §11) and the OME icon for the window, the fullscreen strip and 情報 (§5.5). THIRD_PARTY_NOTICES.md records
/// the font's source and license.
/// </summary>
public static class ShellAssets
{
    private const string FontResource = "OME.Assets.NotoSansJP-Regular.otf";
    private const string IconResource = "OME.Assets.ome-icon.svg";
    private const int IconDesignSize = 256;

    private static FontFile? _font;

    /// <summary>Noto Sans JP Regular, drawn with grayscale antialiasing and light hinting.</summary>
    public static FontFile Font => _font ??= new FontFile
    {
        Data = Read(FontResource),
        Antialiasing = TextServer.FontAntialiasing.Gray,
        Hinting = TextServer.Hinting.Light,
        GenerateMipmaps = false,
    };

    /// <summary>The OME icon rasterized at <paramref name="size"/> pixels square.</summary>
    public static Image Icon(int size)
    {
        var image = new Image();
        Error error = image.LoadSvgFromBuffer(Read(IconResource), size / (float)IconDesignSize);
        if (error != Error.Ok) throw new InvalidOperationException($"the OME icon does not load: {error}");
        return image;
    }

    public static Texture2D IconTexture(int size) => ImageTexture.CreateFromImage(Icon(size));

    private static byte[] Read(string name)
    {
        using Stream stream = typeof(ShellAssets).Assembly.GetManifestResourceStream(name)
                              ?? throw new InvalidOperationException($"the OME assembly has no resource {name}");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
}
