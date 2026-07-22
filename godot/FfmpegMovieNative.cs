using System;
using System.IO;
using System.Runtime.InteropServices;
using Age.Engine.Sys4;
using Microsoft.Win32.SafeHandles;

internal readonly record struct FfmpegMovieInfo(
    int Width,
    int Height,
    long StopTimeMs,
    int FrameRateNumerator,
    int FrameRateDenominator,
    bool HasAudio);

internal sealed record FfmpegVideoFrame(RgbaImage Image, long PresentationTimeMs);

internal interface IFfmpegFrameSource : IDisposable
{
    FfmpegMovieInfo Info { get; }
    bool TryDecodeNextVideoFrame(out FfmpegVideoFrame frame);
}

/// <summary>Sequential, unpaced access to the project-owned FFmpeg C ABI for isolated probes and playback.</summary>
internal sealed class FfmpegMovieSession : IFfmpegFrameSource
{
    private FfmpegMovieHandle _handle;
    public FfmpegMovieInfo Info { get; }

    public FfmpegMovieSession(MoviePayload movie)
    {
        ArgumentNullException.ThrowIfNull(movie);
        if (FfmpegMovieNative.AbiVersion() != 1)
            throw new InvalidOperationException("unsupported age_movie_ffmpeg ABI version");

        byte[] error = new byte[1024];
        int result = FfmpegMovieNative.Open(movie.Bytes, (nuint)movie.Bytes.Length,
            out _handle, out var nativeInfo, error, (nuint)error.Length);
        if (result != 0 || _handle.IsInvalid)
        {
            _handle?.Dispose();
            throw new InvalidDataException(
                $"FFmpeg movie open failed for {movie.Name}: {FfmpegMovieNative.DecodeUtf8(error)}");
        }
        Info = new FfmpegMovieInfo(nativeInfo.Width, nativeInfo.Height, nativeInfo.StopTimeMs,
            nativeInfo.FrameRateNumerator, nativeInfo.FrameRateDenominator, nativeInfo.HasAudio != 0);
        if (Info.Width <= 0 || Info.Height <= 0 || Info.StopTimeMs <= 0)
        {
            _handle.Dispose();
            throw new InvalidDataException(
                $"FFmpeg returned invalid metadata for {movie.Name}: {Info.Width}x{Info.Height}, {Info.StopTimeMs} ms");
        }
    }

    public bool TryDecodeNextVideoFrame(out FfmpegVideoFrame frame)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        byte[] pixels = new byte[checked(Info.Width * Info.Height * 4)];
        int result = FfmpegMovieNative.DecodeVideo(_handle, pixels, (nuint)pixels.Length, out long ptsMs);
        if (result == FfmpegMovieNative.EndOfFile)
        {
            frame = default!;
            return false;
        }
        if (result != FfmpegMovieNative.Frame)
            throw new InvalidDataException($"FFmpeg movie decode failed: {FfmpegMovieNative.LastError(_handle)}");
        frame = new FfmpegVideoFrame(new RgbaImage(Info.Width, Info.Height, pixels), ptsMs);
        return true;
    }

    public void Dispose() => _handle.Dispose();
}

internal sealed class FfmpegMovieHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private FfmpegMovieHandle() : base(ownsHandle: true) { }
    protected override bool ReleaseHandle()
    {
        FfmpegMovieNative.Close(handle);
        return true;
    }
}

internal static class FfmpegMovieNative
{
    internal const int EndOfFile = 0;
    internal const int Frame = 1;
    private const string LibraryName = "age_movie_ffmpeg";

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeInfo
    {
        public int Width;
        public int Height;
        public long StopTimeMs;
        public int FrameRateNumerator;
        public int FrameRateDenominator;
        public int HasAudio;
    }

    static FfmpegMovieNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(FfmpegMovieNative).Assembly, ResolveLibrary);
    }

    private static IntPtr ResolveLibrary(string libraryName, System.Reflection.Assembly assembly,
                                         DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal)) return IntPtr.Zero;
        string fileName = OperatingSystem.IsWindows()
            ? "age_movie_ffmpeg.dll"
            : OperatingSystem.IsMacOS() ? "libage_movie_ffmpeg.dylib" : "libage_movie_ffmpeg.so";
        string? configured = Environment.GetEnvironmentVariable("AGE_FFMPEG_NATIVE_DIR");
        string rid = OperatingSystem.IsWindows() ? "win-x64"
            : OperatingSystem.IsMacOS() ? (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64")
            : "linux-x64";
        string[] candidates =
        {
            configured == null ? "" : Path.Combine(configured, fileName),
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", fileName),
        };
        foreach (string candidate in candidates)
            if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                return NativeLibrary.Load(candidate);
        throw new DllNotFoundException(
            $"{fileName} was not found; set AGE_FFMPEG_NATIVE_DIR or package runtimes/{rid}/native");
    }

    internal static string DecodeUtf8(byte[] buffer)
    {
        int length = Array.IndexOf(buffer, (byte)0);
        if (length < 0) length = buffer.Length;
        return System.Text.Encoding.UTF8.GetString(buffer, 0, length);
    }

    internal static string LastError(FfmpegMovieHandle handle)
    {
        IntPtr text = LastErrorNative(handle);
        return text == IntPtr.Zero ? "unknown decoder error" : Marshal.PtrToStringUTF8(text) ?? "unknown decoder error";
    }

    internal static uint AbiVersion() => AbiVersionNative();

    [DllImport(LibraryName, EntryPoint = "age_movie_abi_version", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint AbiVersionNative();

    [DllImport(LibraryName, EntryPoint = "age_movie_open", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Open(
        [In] byte[] bytes,
        nuint length,
        out FfmpegMovieHandle movie,
        out NativeInfo info,
        [Out] byte[] errorBuffer,
        nuint errorBufferSize);

    [DllImport(LibraryName, EntryPoint = "age_movie_decode_video", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int DecodeVideo(
        FfmpegMovieHandle movie,
        [Out] byte[] rgba,
        nuint rgbaSize,
        out long presentationTimeMs);

    [DllImport(LibraryName, EntryPoint = "age_movie_last_error", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr LastErrorNative(FfmpegMovieHandle movie);

    [DllImport(LibraryName, EntryPoint = "age_movie_close", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Close(IntPtr movie);
}
