using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using Age.Engine.Text;
using Microsoft.Win32.SafeHandles;

namespace Age.Engine.Text.Windows;

/// <summary>
/// Exact Windows reference backend for AGE's CreateICA/CreateFontIndirectA/GetGlyphOutlineA
/// GGO_GRAY4_BITMAP path. It is opt-in and requires the Japanese ANSI system code page.
/// </summary>
public sealed class WindowsGdiGlyphMaskRasterizer
    : IIdentifiedGlyphMaskRasterizer, IDisposable
{
    private const uint GgoGray4Bitmap = 5;
    private const uint GdiError = 0xffffffff;
    private const byte DefaultCharset = 1;
    private static readonly Encoding Cp932 = CreateCp932();

    private readonly object _gate = new();
    private readonly SafeDeviceContextHandle _displayIc;
    private readonly BoundedLruCache<FontKey, SafeGdiObjectHandle> _fonts;
    private bool _disposed;

    private readonly record struct FontKey(
        string Face, int PixelHeight, int RequestedWidth, int Weight);

    public WindowsGdiGlyphMaskRasterizer(int fontCacheCapacity = 16)
    {
        if (fontCacheCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontCacheCapacity));
        if (!TryGetAvailability(out string reason))
            throw new PlatformNotSupportedException(reason);

        IntPtr dc = NativeMethods.CreateICA("DISPLAY", null, null, IntPtr.Zero);
        if (dc == IntPtr.Zero) ThrowWin32("CreateICA(\"DISPLAY\") failed");
        _displayIc = new SafeDeviceContextHandle(dc);
        _fonts = new BoundedLruCache<FontKey, SafeGdiObjectHandle>(
            fontCacheCapacity, font => font.Dispose());
    }

    public static bool TryGetAvailability(out string reason)
    {
        if (!OperatingSystem.IsWindows())
        {
            reason = "Windows GDI is unavailable on this operating system.";
            return false;
        }

        uint activeCodePage = NativeMethods.GetACP();
        if (activeCodePage != 932)
        {
            reason =
                $"Exact AGE ANSI rasterization requires Windows system code page 932; active ACP is {activeCodePage}.";
            return false;
        }

        reason = "Windows GDI DISPLAY information context with Japanese ANSI code page 932.";
        return true;
    }

    public GlyphRasterizerBackendInfo BackendInfo => new(
        "windows-gdi-gray4",
        "Windows GDI Gray-4 (AGE reference)",
        GlyphRasterPolicy.NativeCp932Gray4,
        NativePixelExact: true,
        Detail: "CreateICA(\"DISPLAY\") + CreateFontIndirectA + GetGlyphOutlineA(GGO_GRAY4_BITMAP), ACP 932");

    public int FontCacheCount => _fonts.Count;
    public int FontCacheCapacity => _fonts.Capacity;

    public GlyphMask Rasterize(GlyphRasterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Policy != GlyphRasterPolicy.NativeCp932Gray4 || request.Cp932Code == null)
            throw new ArgumentException(
                "The Windows GDI reference backend accepts only native CP932 gray-4 requests.",
                nameof(request));

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            SafeGdiObjectHandle font = GetOrCreateFont(request);
            IntPtr previous = NativeMethods.SelectObject(
                _displayIc.DangerousGetHandle(), font.DangerousGetHandle());
            if (previous == IntPtr.Zero || previous == new IntPtr(-1))
                ThrowWin32("SelectObject(font) failed");
            try
            {
                return RasterizeSelectedFont(request);
            }
            finally
            {
                NativeMethods.SelectObject(_displayIc.DangerousGetHandle(), previous);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _fonts.Clear();
            _displayIc.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    private GlyphMask RasterizeSelectedFont(GlyphRasterRequest request)
    {
        ushort code = request.Cp932Code!.Value;
        if (code == 0) throw new ArgumentException("CP932 NUL is not a drawable glyph.", nameof(request));
        Mat2 identity = Mat2.Identity;
        uint size = NativeMethods.GetGlyphOutlineA(
            _displayIc.DangerousGetHandle(), code, GgoGray4Bitmap,
            out GlyphMetrics metrics, 0, null, ref identity);
        if (size == GdiError) ThrowWin32($"GetGlyphOutlineA query failed for CP932 0x{code:x4}");

        int width = checked((int)metrics.BlackBoxX);
        int height = checked((int)metrics.BlackBoxY);
        int stride = checked((width + 3) & ~3);
        int expectedBytes = checked(stride * height);
        // CP932 0x8140 (U+3000 IDEOGRAPHIC SPACE) is a zero-ink spacing glyph. GDI reports
        // its placement as a nominal 1x1 black box but returns a zero-byte required buffer.
        // Preserve those metrics and materialize the implied transparent mask; a nonzero
        // short/oversized payload still means that the bitmap contract is inconsistent.
        if (size != 0 && size != expectedBytes)
            throw new InvalidOperationException(
                $"GDI gray-4 buffer size {size} disagrees with {width}x{height}, stride {stride}.");

        byte[] coverage = new byte[expectedBytes];
        if (size > 0)
        {
            identity = Mat2.Identity;
            uint written = NativeMethods.GetGlyphOutlineA(
                _displayIc.DangerousGetHandle(), code, GgoGray4Bitmap,
                out GlyphMetrics secondMetrics, size, coverage, ref identity);
            if (written == GdiError) ThrowWin32($"GetGlyphOutlineA read failed for CP932 0x{code:x4}");
            if (written != size || !metrics.Equals(secondMetrics))
                throw new InvalidOperationException("GDI glyph metrics changed between query and read.");
        }

        byte[] encoded = EncodeCp932Code(code);
        if (!NativeMethods.GetTextExtentPoint32A(
                _displayIc.DangerousGetHandle(), encoded, encoded.Length, out NativeSize cell))
            ThrowWin32($"GetTextExtentPoint32A failed for CP932 0x{code:x4}");

        return new GlyphMask(
            width, height, stride,
            metrics.GlyphOrigin.X, metrics.GlyphOrigin.Y,
            metrics.CellIncrementX, metrics.CellIncrementY,
            cell.Width, cell.Height,
            coverage);
    }

    private SafeGdiObjectHandle GetOrCreateFont(GlyphRasterRequest request)
    {
        var key = new FontKey(
            request.FontFace, request.PixelHeight, request.RequestedWidth, request.Weight);
        if (_fonts.TryGetValue(key, out SafeGdiObjectHandle? cached)) return cached;

        var logFont = new LogFontA
        {
            Height = -request.PixelHeight,
            Width = request.RequestedWidth,
            Weight = request.Weight,
            CharSet = DefaultCharset,
            FaceName = EncodeFaceName(request.FontFace),
        };
        IntPtr raw = NativeMethods.CreateFontIndirectA(ref logFont);
        if (raw == IntPtr.Zero)
            ThrowWin32($"CreateFontIndirectA failed for '{request.FontFace}'");
        var created = new SafeGdiObjectHandle(raw);
        _fonts.Set(key, created);
        return created;
    }

    private static byte[] EncodeFaceName(string face)
    {
        byte[] encoded;
        try
        {
            encoded = Cp932.GetBytes(face);
        }
        catch (EncoderFallbackException error)
        {
            throw new ArgumentException($"Font face '{face}' is not representable in CP932.", nameof(face), error);
        }
        if (encoded.Length > 31)
            throw new ArgumentException("A LOGFONTA face name cannot exceed 31 encoded bytes.", nameof(face));
        var result = new byte[32];
        encoded.CopyTo(result, 0);
        return result;
    }

    private static byte[] EncodeCp932Code(ushort code)
        => code <= byte.MaxValue
            ? [(byte)code]
            : [(byte)(code >> 8), (byte)code];

    private static Encoding CreateCp932()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(
            932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    [DoesNotReturn]
    private static void ThrowWin32(string message)
        => throw new Win32Exception(Marshal.GetLastPInvokeError(), message);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint : IEquatable<NativePoint>
    {
        public int X;
        public int Y;
        public readonly bool Equals(NativePoint other) => X == other.X && Y == other.Y;
        public override readonly bool Equals(object? obj) => obj is NativePoint other && Equals(other);
        public override readonly int GetHashCode() => HashCode.Combine(X, Y);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GlyphMetrics : IEquatable<GlyphMetrics>
    {
        public uint BlackBoxX;
        public uint BlackBoxY;
        public NativePoint GlyphOrigin;
        public short CellIncrementX;
        public short CellIncrementY;

        public readonly bool Equals(GlyphMetrics other)
            => BlackBoxX == other.BlackBoxX
               && BlackBoxY == other.BlackBoxY
               && GlyphOrigin.Equals(other.GlyphOrigin)
               && CellIncrementX == other.CellIncrementX
               && CellIncrementY == other.CellIncrementY;
        public override readonly bool Equals(object? obj)
            => obj is GlyphMetrics other && Equals(other);
        public override readonly int GetHashCode()
            => HashCode.Combine(
                BlackBoxX, BlackBoxY, GlyphOrigin, CellIncrementX, CellIncrementY);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Fixed
    {
        public ushort Fraction;
        public short Value;

        public static Fixed One => new() { Value = 1 };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Mat2
    {
        public Fixed M11;
        public Fixed M12;
        public Fixed M21;
        public Fixed M22;

        public static Mat2 Identity => new() { M11 = Fixed.One, M22 = Fixed.One };
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

    private sealed class SafeDeviceContextHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeDeviceContextHandle(IntPtr handle) : base(ownsHandle: true) => SetHandle(handle);
        protected override bool ReleaseHandle() => NativeMethods.DeleteDC(handle);
    }

    private sealed class SafeGdiObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeGdiObjectHandle(IntPtr handle) : base(ownsHandle: true) => SetHandle(handle);
        protected override bool ReleaseHandle() => NativeMethods.DeleteObject(handle);
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll")]
        public static extern uint GetACP();

        [DllImport("gdi32.dll", EntryPoint = "CreateICA", CharSet = CharSet.Ansi, SetLastError = true)]
        public static extern IntPtr CreateICA(
            string driver, string? device, string? output, IntPtr initializationData);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteDC(IntPtr deviceContext);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr gdiObject);

        [DllImport("gdi32.dll", EntryPoint = "CreateFontIndirectA", SetLastError = true)]
        public static extern IntPtr CreateFontIndirectA(ref LogFontA logFont);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr gdiObject);

        [DllImport("gdi32.dll", EntryPoint = "GetGlyphOutlineA", SetLastError = true)]
        public static extern uint GetGlyphOutlineA(
            IntPtr deviceContext,
            uint character,
            uint format,
            out GlyphMetrics metrics,
            uint bufferSize,
            [Out] byte[]? buffer,
            ref Mat2 transform);

        [DllImport("gdi32.dll", EntryPoint = "GetTextExtentPoint32A", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetTextExtentPoint32A(
            IntPtr deviceContext,
            byte[] text,
            int length,
            out NativeSize size);
    }
}
