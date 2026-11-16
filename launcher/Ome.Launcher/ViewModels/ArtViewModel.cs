using Avalonia.Media;
using Avalonia.Media.Imaging;
using Ome.Launcher.Core;

namespace Ome.Launcher.ViewModels;

/// <summary>
/// A banner or a cover as the window draws it (launcher spec §8.7): the image when it decodes, else the generated card,
/// the game's title on a gradient from the card's colour.
/// </summary>
public sealed class ArtViewModel
{
    public ArtViewModel(ArtImage image)
    {
        Kind = image.Kind;
        Origin = image.Origin;
        Image = image.Data is { } data ? Decode(data) : null;
        if (Image == null) Origin = ArtOrigin.Card;
        Title = image.Card.OriginalTitle ?? image.Card.Title;
        Color top = Color.FromUInt32(0xff000000 | image.Card.Rgb);
        Color bottom = Color.FromRgb((byte)(top.R * 0.62), (byte)(top.G * 0.62), (byte)(top.B * 0.62));
        Fill = new LinearGradientBrush
        {
            StartPoint = new Avalonia.RelativePoint(0, 0.3, Avalonia.RelativeUnit.Relative),
            EndPoint = new Avalonia.RelativePoint(1, 0.7, Avalonia.RelativeUnit.Relative),
            GradientStops = { new GradientStop(top, 0), new GradientStop(bottom, 1) },
        };
    }

    public ArtKind Kind { get; }
    public ArtOrigin Origin { get; }
    public Bitmap? Image { get; }
    public bool IsCard => Image == null;
    public bool IsBanner => Kind == ArtKind.Banner;
    public string Title { get; }
    public IBrush Fill { get; }

    /// <summary>The window's decoder (<see cref="ArtOptions.Decoder"/>, launcher plan §6.2): Avalonia's own, so only a file
    /// the window can draw is kept or shown.</summary>
    public static ImageSize? Probe(ReadOnlySpan<byte> data)
    {
        using Bitmap? bitmap = Decode(data.ToArray());
        return bitmap == null ? null : new ImageSize(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
    }

    private static Bitmap? Decode(byte[] data)
    {
        try
        {
            return new Bitmap(new MemoryStream(data, writable: false));
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return null;
        }
    }
}
