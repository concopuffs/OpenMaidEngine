using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace Ome.Engine.Text.Windows;

/// <summary>
/// The Windows queries behind SYS4433's installed-font list (text_build_font_family_list@0x458040, docs/engine-re.md
/// "Font family list"), made the way AGE makes them: ANSI GDI calls on a DISPLAY information context and the 32-bit
/// registry view.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsGdiFontFamilySource : IFontFamilySource, IDisposable
{
    private const byte ShiftJisCharset = 0x80;
    private const byte DefaultCharset = 1;
    private const byte OutlinePrecision = 8;
    private const uint RasterFontType = 1;
    private const uint GgoMetrics = 0;
    private const uint GdiError = 0xffffffff;
    private const string SystemLinkKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\FontLink\SystemLink";
    private static readonly Encoding Cp932 = CreateCp932();

    private readonly DisplayInformationContext _ic = DisplayInformationContext.Create();
    private string? _probeFace;
    private IntPtr _probeFont;

    public IReadOnlyList<EnumeratedFontFamily> EnumerateShiftJisFamilies()
    {
        var families = new List<EnumeratedFontFamily>();
        var query = new LogFontA { CharSet = ShiftJisCharset, OutPrecision = OutlinePrecision, FaceName = new byte[32] };
        // The callback at 0x455010: keep a family unless GDI calls it a raster font, its charset is not Shift-JIS or
        // its name is a vertical-writing '@' face. It appends without checking for duplicates.
        NativeMethods.EnumFontFamExProc callback = (logFont, _, fontType, _) =>
        {
            byte charSet = Marshal.ReadByte(logFont, 23);
            byte pitchAndFamily = Marshal.ReadByte(logFont, 27);
            string face = ReadFaceName(logFont + 28);
            if (fontType != RasterFontType && charSet == ShiftJisCharset && !face.StartsWith('@'))
                families.Add(new EnumeratedFontFamily(face, pitchAndFamily));
            return 1;
        };
        NativeMethods.EnumFontFamiliesExA(_ic.Handle, ref query, callback, IntPtr.Zero, 0);
        GC.KeepAlive(callback);
        return families;
    }

    public IReadOnlyList<string> SystemLinkFaceNames()
    {
        using RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using RegistryKey? key = root.OpenSubKey(SystemLinkKey);
        return key?.GetValueNames() ?? [];
    }

    /// <summary>A failed GGO_METRICS query leaves AGE's metrics from the previous probe; the port counts it as a
    /// present glyph.</summary>
    public bool HasGlyph(string faceName, ushort code)
    {
        ArgumentNullException.ThrowIfNull(faceName);
        IntPtr font = ProbeFont(faceName);
        if (font == IntPtr.Zero) return true;
        IntPtr previous = NativeMethods.SelectObject(_ic.Handle, font);
        try
        {
            Mat2 identity = Mat2.Identity;
            uint size = NativeMethods.GetGlyphOutlineA(
                _ic.Handle, code, GgoMetrics, out GlyphMetrics metrics, 0, IntPtr.Zero, ref identity);
            return size == GdiError || metrics.BlackBoxX >= 2 || metrics.BlackBoxY >= 2;
        }
        finally
        {
            NativeMethods.SelectObject(_ic.Handle, previous);
        }
    }

    public void Dispose()
    {
        ReleaseProbeFont();
        _ic.Dispose();
    }

    /// <summary>The probe font AGE creates for each family: lfHeight -64, lfWidth -32, DEFAULT_CHARSET, the face and
    /// zeros elsewhere. It is kept while the same face is probed.</summary>
    private IntPtr ProbeFont(string faceName)
    {
        if (faceName == _probeFace) return _probeFont;
        ReleaseProbeFont();
        byte[] face = new byte[32];
        byte[] encoded = Cp932.GetBytes(faceName);
        Array.Copy(encoded, face, Math.Min(encoded.Length, face.Length - 1));
        var logFont = new LogFontA { Height = -64, Width = -32, CharSet = DefaultCharset, FaceName = face };
        _probeFont = NativeMethods.CreateFontIndirectA(ref logFont);
        _probeFace = faceName;
        return _probeFont;
    }

    private void ReleaseProbeFont()
    {
        if (_probeFont != IntPtr.Zero) NativeMethods.DeleteObject(_probeFont);
        _probeFont = IntPtr.Zero;
        _probeFace = null;
    }

    private static string ReadFaceName(IntPtr name)
    {
        byte[] bytes = new byte[32];
        Marshal.Copy(name, bytes, 0, bytes.Length);
        int length = Array.IndexOf(bytes, (byte)0);
        return Cp932.GetString(bytes, 0, length < 0 ? bytes.Length : length);
    }

    private static Encoding CreateCp932()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }

    private sealed class DisplayInformationContext : IDisposable
    {
        private DisplayInformationContext(IntPtr handle) => Handle = handle;
        public IntPtr Handle { get; }

        public static DisplayInformationContext Create()
        {
            IntPtr handle = NativeMethods.CreateICA("DISPLAY", null, null, IntPtr.Zero);
            if (handle == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(),
                    "CreateICA(\"DISPLAY\") failed");
            return new DisplayInformationContext(handle);
        }

        public void Dispose() => NativeMethods.DeleteDC(Handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GlyphMetrics
    {
        public uint BlackBoxX;
        public uint BlackBoxY;
        public int OriginX;
        public int OriginY;
        public short CellIncrementX;
        public short CellIncrementY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Mat2
    {
        public int M11;
        public int M12;
        public int M21;
        public int M22;

        public static Mat2 Identity => new() { M11 = 0x10000, M22 = 0x10000 };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LogFontA
    {
        public int Height;
        public int Width;
        public int Escapement;
        public int Orientation;
        public int Weight;
        public byte Italic;
        public byte Underline;
        public byte StrikeOut;
        public byte CharSet;
        public byte OutPrecision;
        public byte ClipPrecision;
        public byte Quality;
        public byte PitchAndFamily;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] FaceName;
    }

    private static class NativeMethods
    {
        public delegate int EnumFontFamExProc(IntPtr logFont, IntPtr textMetric, uint fontType, IntPtr parameter);

        [DllImport("gdi32.dll", EntryPoint = "CreateICA", CharSet = CharSet.Ansi, SetLastError = true)]
        public static extern IntPtr CreateICA(string driver, string? device, string? output, IntPtr initializationData);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteDC(IntPtr deviceContext);

        [DllImport("gdi32.dll", EntryPoint = "EnumFontFamiliesExA")]
        public static extern int EnumFontFamiliesExA(
            IntPtr deviceContext, ref LogFontA logFont, EnumFontFamExProc callback, IntPtr parameter, uint flags);

        [DllImport("gdi32.dll", EntryPoint = "CreateFontIndirectA")]
        public static extern IntPtr CreateFontIndirectA(ref LogFontA logFont);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr gdiObject);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr gdiObject);

        [DllImport("gdi32.dll", EntryPoint = "GetGlyphOutlineA")]
        public static extern uint GetGlyphOutlineA(
            IntPtr deviceContext, uint character, uint format, out GlyphMetrics metrics, uint bufferSize,
            IntPtr buffer, ref Mat2 transform);
    }
}
