using System;
using System.Collections.Generic;
using System.Globalization;
using Ome.Engine.Sys4;

/// <summary>
/// Presentation-only window dimensions resolved from Godot user arguments, else from OME.INI's display:WindowWidth and
/// WindowHeight, else the logical canvas. These never redefine the SYS4 logical canvas, VM coordinates, or AGE surface
/// dimensions.
/// </summary>
public readonly record struct WindowLaunchOptions(
    int Width, int Height, bool WidthOverridden, bool HeightOverridden)
{
    public const int MaximumDimension = Sys4LogicalCanvas.MaximumDimension;
    public bool IsOverridden => WidthOverridden || HeightOverridden;

    /// <param name="saved">The port options file's values; its size applies when it is not the canvas's.</param>
    public static WindowLaunchOptions Resolve(
        IReadOnlyList<string> arguments, Sys4LogicalCanvas logicalCanvas,
        Ome.Engine.Frontend.Shell.PortOptions? saved = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        bool savedSize = saved is { CanvasWindowSize: false };
        int width = savedSize ? saved!.WindowWidth : logicalCanvas.Width;
        int height = savedSize ? saved!.WindowHeight : logicalCanvas.Height;
        bool widthOverridden = false;
        bool heightOverridden = false;

        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            if (argument is not ("--window-width" or "--window-height")) continue;
            if (index + 1 >= arguments.Count)
                throw new ArgumentException($"{argument} requires a pixel value");

            string raw = arguments[++index];
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                || value <= 0 || value > MaximumDimension)
                throw new ArgumentException(
                    $"{argument} must be an integer from 1 through {MaximumDimension}; got '{raw}'");

            if (argument == "--window-width")
            {
                width = value;
                widthOverridden = true;
            }
            else
            {
                height = value;
                heightOverridden = true;
            }
        }

        return new WindowLaunchOptions(width, height, widthOverridden, heightOverridden);
    }
}
