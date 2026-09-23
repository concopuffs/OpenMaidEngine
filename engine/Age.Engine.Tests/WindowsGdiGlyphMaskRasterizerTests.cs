using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Age.Engine.Model;
using Age.Engine.Text;
using Age.Engine.Text.Windows;

public class WindowsGdiGlyphMaskRasterizerTests
{
    private const uint GgoGray4Bitmap = 5;
    private const uint GdiError = 0xffffffff;

    [Fact]
    public void KamidoriAspectModeKeepsBothTranslatedOpeningLinesWithinLayout()
    {
        if (!WindowsGdiGlyphMaskRasterizer.TryGetAvailability(out _)) return;
        using var rasterizer = new WindowsGdiGlyphMaskRasterizer();
        var engine = new RetainedGlyphLayoutEngine(rasterizer);
        var style = AdvTextStyle.Default with
        {
            PrimaryFontSize = 22, FontFace = "ＭＳ ゴシック", Bold = true,
            LineSpacing = 10, AspectMode = 1,
        };
        var image = new RgbaImage(836, 139, new byte[836 * 139 * 4]);
        string first = "Using power and magic passed down through the ages, a growing";
        string second = "city-state produces new inventions every day, desired by all.";
        var options = new GlyphTextLayoutOptions(63, 28, 63, 773, 113, true, style);
        var oldMetrics = engine.Render(image, options with { Style = style with { AspectMode = 0 } },
            ImmediateSurfaceTextRenderer.CreateRequests(first, style));
        Assert.True(oldMetrics.WrappedLines == 1, $"count={first.Length} cursor={oldMetrics.CursorX},{oldMetrics.CursorY} records={oldMetrics.Records[0]}");
        var rejected = engine.Render(image,
            options with { CursorY = oldMetrics.CursorY + 32, Style = style with { AspectMode = 0 } },
            ImmediateSurfaceTextRenderer.CreateRequests(second, style));
        Assert.Equal(0, rejected.ConsumedGlyphs);
        Assert.True(rejected.StoppedOnVerticalOverflow);

        var a = engine.Render(image, options,
            ImmediateSurfaceTextRenderer.CreateRequests(first, style));
        Assert.Equal(first.Length, a.ConsumedGlyphs);
        Assert.Equal(0, a.WrappedLines);
        var b = engine.Render(image, options with { CursorY = a.CursorY + 32 },
            ImmediateSurfaceTextRenderer.CreateRequests(second, style));
        Assert.Equal(second.Length, b.ConsumedGlyphs);
        Assert.Equal(0, b.WrappedLines);
        Assert.False(b.StoppedOnVerticalOverflow);
        Assert.Equal(60, b.CursorY);
    }

    [Fact]
    public void BackendSelectionIsExplicitAndDiagnostic()
    {
        if (!WindowsGdiGlyphMaskRasterizer.TryGetAvailability(out string availability))
        {
            Assert.NotEmpty(availability);
            return;
        }

        using var rasterizer = new WindowsGdiGlyphMaskRasterizer(fontCacheCapacity: 2);

        Assert.Equal(
            new GlyphRasterizerBackendInfo(
                "windows-gdi-gray4",
                "Windows GDI Gray-4 (AGE reference)",
                GlyphRasterPolicy.NativeCp932Gray4,
                NativePixelExact: true,
                "CreateICA(\"DISPLAY\") + CreateFontIndirectA + GetGlyphOutlineA(GGO_GRAY4_BITMAP), ACP 932"),
            rasterizer.BackendInfo);
        var portable = new GlyphRasterRequest(
            "ＭＳ 明朝", 24, -12, 700, 0x3042, null, GlyphRasterPolicy.PortableUnicode);
        Assert.Throws<ArgumentException>(() => rasterizer.Rasterize(portable));
    }

    [Theory]
    [InlineData("ＭＳ 明朝", 24, -12, 0, 0x3042)]   // あ
    [InlineData("ＭＳ 明朝", 24, -12, 700, 0x59eb)] // 姫
    [InlineData("ＭＳ ゴシック", 16, -8, 700, 0x30a2)] // ア
    public void AnsiCp932BackendMatchesIndependentUnicodeGdiOracle(
        string face, int height, int width, int weight, int scalar)
    {
        if (!WindowsGdiGlyphMaskRasterizer.TryGetAvailability(out _)) return;
        GlyphRasterRequest request = NativeRequest(face, height, width, weight, scalar);

        using var rasterizer = new WindowsGdiGlyphMaskRasterizer();
        GlyphMask actual = rasterizer.Rasterize(request);
        DirectGlyph expected = RasterizeUnicodeDirect(face, height, width, weight, scalar);

        Assert.Equal(
            (expected.Width, expected.Height, expected.Stride,
             expected.OriginX, expected.OriginY,
             expected.CellAdvanceX, expected.CellAdvanceY,
             expected.CellWidth, expected.CellHeight),
            (actual.Width, actual.Height, actual.Stride,
             actual.OriginX, actual.OriginY,
             actual.CellAdvanceX, actual.CellAdvanceY,
             actual.CellWidth, actual.CellHeight));
        Assert.Equal(expected.Coverage, actual.Coverage.ToArray());
        Assert.All(actual.Coverage.ToArray(), value => Assert.InRange(value, (byte)0, (byte)16));
    }

    [Fact]
    public void IdeographicSpacePreservesAdvanceWithTransparentCoverage()
    {
        if (!WindowsGdiGlyphMaskRasterizer.TryGetAvailability(out _)) return;

        using var rasterizer = new WindowsGdiGlyphMaskRasterizer();
        GlyphMask space = rasterizer.Rasterize(
            NativeRequest("ＭＳ 明朝", 24, -12, 0, 0x3000));

        Assert.True(space.CellAdvanceX > 0);
        Assert.All(space.Coverage.ToArray(), value => Assert.Equal(0, value));
    }

    [Fact]
    public void Sc0000OpeningNarrationLineRasterizesEveryGlyph()
    {
        if (!WindowsGdiGlyphMaskRasterizer.TryGetAvailability(out _)) return;

        const string text =
            "だが怨嗟に満ちた言葉は、呪いのように彼らの心に暗い影を落とした。";
        var style = AdvTextStyle.Default with
        {
            PrimaryFontSize = 24,
            Bold = true,
            FontFace = "ＭＳ 明朝",
            RenderMode = 3,
            LineSpacing = 8,
        };

        using var rasterizer = new WindowsGdiGlyphMaskRasterizer();
        foreach (GlyphRasterRequest request in
                 ImmediateSurfaceTextRenderer.CreateRequests(text, style))
        {
            Exception? error = Record.Exception(() => rasterizer.Rasterize(request));
            Assert.True(
                error == null,
                $"U+{request.UnicodeScalar:X4} CP932 0x{request.Cp932Code:X4}: {error}");
        }
    }

    [Fact]
    public void Gray4StorageBeyondMetricRowsIsTrimmed()
    {
        if (!WindowsGdiGlyphMaskRasterizer.TryGetAvailability(out _)) return;

        GlyphRasterRequest request = NativeRequest("ＭＳ 明朝", 24, -12, 700, 'p');
        DirectGlyph direct = RasterizeUnicodeDirect("ＭＳ 明朝", 24, -12, 700, 'p');
        using var rasterizer = new WindowsGdiGlyphMaskRasterizer();
        GlyphMask actual = rasterizer.Rasterize(request);

        Assert.True(direct.RawBufferSize > direct.Coverage.Length);
        Assert.Equal(direct.Coverage, actual.Coverage.ToArray());
        Assert.Equal(actual.Stride * actual.Height, actual.Coverage.Length);
    }

    [Fact]
    public void FontHandlesUseTheSharedBoundedLruAndDisposeCleanly()
    {
        if (!WindowsGdiGlyphMaskRasterizer.TryGetAvailability(out _)) return;
        var rasterizer = new WindowsGdiGlyphMaskRasterizer(fontCacheCapacity: 1);

        rasterizer.Rasterize(NativeRequest("ＭＳ 明朝", 24, -12, 0, 0x3042));
        rasterizer.Rasterize(NativeRequest("ＭＳ 明朝", 24, -12, 700, 0x3042));

        Assert.Equal(1, rasterizer.FontCacheCount);
        Assert.Equal(1, rasterizer.FontCacheCapacity);
        rasterizer.Dispose();
        Assert.Throws<ObjectDisposedException>(
            () => rasterizer.Rasterize(NativeRequest("ＭＳ 明朝", 24, -12, 700, 0x3042)));
    }

    private static GlyphRasterRequest NativeRequest(
        string face, int height, int width, int weight, int scalar)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        byte[] encoded = Encoding.GetEncoding(
            932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
            .GetBytes(char.ConvertFromUtf32(scalar));
        Assert.InRange(encoded.Length, 1, 2);
        ushort cp932 = encoded.Length == 1
            ? encoded[0]
            : (ushort)((encoded[0] << 8) | encoded[1]);
        return new GlyphRasterRequest(
            face, height, width, weight, scalar, cp932, GlyphRasterPolicy.NativeCp932Gray4);
    }

    private static DirectGlyph RasterizeUnicodeDirect(
        string face, int height, int width, int weight, int scalar)
    {
        IntPtr dc = Native.CreateICW("DISPLAY", null, null, IntPtr.Zero);
        if (dc == IntPtr.Zero) ThrowWin32("CreateICW failed");
        IntPtr font = IntPtr.Zero;
        IntPtr previous = IntPtr.Zero;
        try
        {
            var logFont = new LogFontW
            {
                Height = -height,
                Width = width,
                Weight = weight,
                CharSet = 1,
                FaceName = face,
            };
            font = Native.CreateFontIndirectW(ref logFont);
            if (font == IntPtr.Zero) ThrowWin32("CreateFontIndirectW failed");
            previous = Native.SelectObject(dc, font);
            if (previous == IntPtr.Zero || previous == new IntPtr(-1))
                ThrowWin32("SelectObject failed");

            Mat2 identity = Mat2.Identity;
            uint size = Native.GetGlyphOutlineW(
                dc, (uint)scalar, GgoGray4Bitmap,
                out GlyphMetrics metrics, 0, null, ref identity);
            if (size == GdiError) ThrowWin32("GetGlyphOutlineW query failed");
            int glyphWidth = checked((int)metrics.BlackBoxX);
            int glyphHeight = checked((int)metrics.BlackBoxY);
            int stride = checked((glyphWidth + 3) & ~3);
            int expectedBytes = checked(stride * glyphHeight);
            Assert.True(size >= expectedBytes);
            byte[] rawCoverage = new byte[size];
            if (size > 0)
            {
                identity = Mat2.Identity;
                uint written = Native.GetGlyphOutlineW(
                    dc, (uint)scalar, GgoGray4Bitmap,
                    out GlyphMetrics second, size, rawCoverage, ref identity);
                Assert.Equal(size, written);
                Assert.Equal(metrics, second);
            }
            byte[] coverage = rawCoverage.AsSpan(0, expectedBytes).ToArray();

            string text = char.ConvertFromUtf32(scalar);
            if (!Native.GetTextExtentPoint32W(dc, text, text.Length, out NativeSize cell))
                ThrowWin32("GetTextExtentPoint32W failed");
            return new DirectGlyph(
                glyphWidth, glyphHeight, stride,
                metrics.GlyphOrigin.X, metrics.GlyphOrigin.Y,
                metrics.CellIncrementX, metrics.CellIncrementY,
                cell.Width, cell.Height, coverage, checked((int)size));
        }
        finally
        {
            if (previous != IntPtr.Zero && previous != new IntPtr(-1))
                Native.SelectObject(dc, previous);
            if (font != IntPtr.Zero) Native.DeleteObject(font);
            Native.DeleteDC(dc);
        }
    }

    private static void ThrowWin32(string message)
        => throw new Win32Exception(Marshal.GetLastPInvokeError(), message);

    private sealed record DirectGlyph(
        int Width,
        int Height,
        int Stride,
        int OriginX,
        int OriginY,
        int CellAdvanceX,
        int CellAdvanceY,
        int CellWidth,
        int CellHeight,
        byte[] Coverage,
        int RawBufferSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GlyphMetrics
    {
        public uint BlackBoxX;
        public uint BlackBoxY;
        public NativePoint GlyphOrigin;
        public short CellIncrementX;
        public short CellIncrementY;
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

    [StructLayout(LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct LogFontW
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

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FaceName;
    }

    private static class Native
    {
        [DllImport("gdi32.dll", EntryPoint = "CreateICW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateICW(
            string driver, string? device, string? output, IntPtr initializationData);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteDC(IntPtr deviceContext);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr gdiObject);

        [DllImport("gdi32.dll", EntryPoint = "CreateFontIndirectW", SetLastError = true)]
        public static extern IntPtr CreateFontIndirectW(ref LogFontW logFont);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr gdiObject);

        [DllImport("gdi32.dll", EntryPoint = "GetGlyphOutlineW", SetLastError = true)]
        public static extern uint GetGlyphOutlineW(
            IntPtr deviceContext,
            uint character,
            uint format,
            out GlyphMetrics metrics,
            uint bufferSize,
            [Out] byte[]? buffer,
            ref Mat2 transform);

        [DllImport("gdi32.dll", EntryPoint = "GetTextExtentPoint32W", CharSet = CharSet.Unicode,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetTextExtentPoint32W(
            IntPtr deviceContext,
            string text,
            int length,
            out NativeSize size);
    }
}
