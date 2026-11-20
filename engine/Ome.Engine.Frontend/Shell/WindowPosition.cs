using System.Globalization;

namespace Ome.Engine.Frontend.Shell;

/// <summary>A screen's rectangle on the desktop, in the coordinates window positions use.</summary>
public readonly record struct ScreenRect(int Left, int Top, int Width, int Height);

/// <summary>
/// Where the game's window was left: its client area's top-left corner on the desktop. A game whose catalog sets
/// <c>SAVEWINPOS</c> to 1 opens its window there again (AGE 4.38D; docs/engine-re.md, "The kept window position").
/// Native AGE keeps <c>WinPosLeft</c> and <c>WinPosTop</c> in the registry, under the game's key. The port writes no
/// registry: it keeps the two in <c>OME.INI</c> as <c>display:WindowLeft</c> and <c>display:WindowTop</c>
/// (<see cref="OmeIniStore.LoadWindowPosition"/>), beside the window's size.
/// </summary>
public readonly record struct WindowPosition(int Left, int Top)
{
    /// <summary>
    /// The position a window of <paramref name="width"/> by <paramref name="height"/> opens at. Native's settings
    /// loader (0x47f810, 4.38D) keeps the stored position when the game's canvas lies on the primary screen from it,
    /// and otherwise, or with none stored, centres the canvas on that screen, halving toward zero. The port does the
    /// same with its window's client size, and accepts any screen where native knows only the primary: a window left
    /// on a second screen opens there again.
    /// </summary>
    /// <param name="screens">Every screen; with none, the stored position stands, or the origin.</param>
    /// <param name="primary">The index of the screen a window without a usable position is centred on.</param>
    public static WindowPosition Restore(WindowPosition? saved, int width, int height,
                                         IReadOnlyList<ScreenRect> screens, int primary)
    {
        if (screens.Count == 0) return saved ?? default;
        if (saved is { } position && screens.Any(screen => position.Fits(width, height, screen))) return position;
        ScreenRect on = screens[Math.Clamp(primary, 0, screens.Count - 1)];
        return new WindowPosition(on.Left + (on.Width - width) / 2, on.Top + (on.Height - height) / 2);
    }

    /// <summary>Whether a window of this size at this position lies within <paramref name="screen"/>.</summary>
    public bool Fits(int width, int height, ScreenRect screen) =>
        Left >= screen.Left && Top >= screen.Top
        && (long)Left + width <= (long)screen.Left + screen.Width
        && (long)Top + height <= (long)screen.Top + screen.Height;

    /// <summary>The position an <c>OME.INI</c> text holds, or null when it lacks either key or one is not an
    /// integer.</summary>
    public static WindowPosition? Parse(string text)
    {
        var ini = OmeIniDocument.Parse(text);
        return Coordinate(ini.Get("display", "WindowLeft")) is { } left
               && Coordinate(ini.Get("display", "WindowTop")) is { } top
            ? new WindowPosition(left, top)
            : null;
    }

    private static int? Coordinate(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int pixels) ? pixels : null;

    /// <summary>The file with this position: every other section and key of <paramref name="previous"/> stays.</summary>
    public string Serialize(string? previous = null)
    {
        var ini = OmeIniDocument.Parse(previous ?? "");
        ini.Set("display", "WindowLeft", Left.ToString(CultureInfo.InvariantCulture));
        ini.Set("display", "WindowTop", Top.ToString(CultureInfo.InvariantCulture));
        return ini.Serialize();
    }
}
