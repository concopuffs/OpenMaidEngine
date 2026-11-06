using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

/// <summary>
/// GDI's advance widths for the Windows look-alike dialogs and menus (<see cref="Win32Font"/>). GDI lays text out by
/// hinted integer advances, with font linking for the characters a face lacks; FreeType's rounded advances differ
/// by a pixel for some glyphs (Segoe UI 12 px 'C': GDI 8, FreeType 7), which shifts every later bar item. On Windows
/// the widths come from GDI itself; elsewhere <see cref="Advance"/> returns null and Godot's advances apply.
/// </summary>
internal static class GdiTextMetrics
{
    private static readonly Dictionary<(string Face, int Height, int Weight), Face?> Faces = new();

    /// <summary>The advance of <paramref name="codepoint"/> in <paramref name="face"/> at a character height of
    /// <paramref name="pixels"/> (LOGFONT lfHeight -pixels) and <paramref name="weight"/> (lfWeight), as
    /// GetTextExtentPoint32W measures it; null off Windows or when GDI cannot answer.</summary>
    public static int? Advance(string face, int pixels, int codepoint, int weight = 400)
    {
        if (!OperatingSystem.IsWindows()) return null;
        lock (Faces)
        {
            if (!Faces.TryGetValue((face, pixels, weight), out Face? gdi))
            {
                gdi = Face.Create(face, pixels, weight);
                Faces[(face, pixels, weight)] = gdi;
            }
            return gdi?.Advance(codepoint);
        }
    }

    private sealed class Face
    {
        private readonly IntPtr _dc;
        private readonly Dictionary<int, int> _advances = new();

        private Face(IntPtr dc) => _dc = dc;

        public static Face? Create(string face, int pixels, int weight)
        {
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return null;
            IntPtr font = CreateFontW(-pixels, 0, 0, 0, weight, 0, 0, 0, 1 /* DEFAULT_CHARSET */, 0, 0, 0, 0, face);
            if (font == IntPtr.Zero)
            {
                DeleteDC(dc);
                return null;
            }
            SelectObject(dc, font);   // the DC and font live for the process, like the cached Godot faces
            return new Face(dc);
        }

        public int? Advance(int codepoint)
        {
            if (_advances.TryGetValue(codepoint, out int known)) return known;
            string text = char.ConvertFromUtf32(codepoint);
            if (!GetTextExtentPoint32W(_dc, text, text.Length, out Size size)) return null;
            _advances[codepoint] = size.Cx;
            return size.Cx;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size
    {
        public int Cx;
        public int Cy;
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, string face);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetTextExtentPoint32W(IntPtr dc, string text, int length, out Size size);
}
