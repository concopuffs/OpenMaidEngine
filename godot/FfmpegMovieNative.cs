using System;
using System.IO;
using System.Runtime.InteropServices;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Microsoft.Win32.SafeHandles;

internal readonly record struct FfmpegMovieInfo(
    int Width,
    int Height,
    long StopTimeMs,
    int FrameRateNumerator,
    int FrameRateDenominator,
    bool HasAudio,
    int AudioSampleRate = 0,
    int AudioChannels = 0,
    int AudioFrameSamples = 0);

internal sealed record FfmpegVideoFrame(RgbaImage Image, long PresentationTimeMs);
internal sealed record FfmpegAudioChunk(float[] InterleavedStereo, int FrameCount, long PresentationTimeMs);

internal interface IFfmpegFrameSource : IDisposable
{
    FfmpegMovieInfo Info { get; }
    void Seek(long positionMs)
    {
        if (positionMs != 0)
            throw new NotSupportedException("movie source does not support positioned playback");
    }
    bool TryDecodeNextVideoFrame(out FfmpegVideoFrame frame);
    bool TryDecodeNextAudioChunk(out FfmpegAudioChunk chunk)
    {
        chunk = default!;
        return false;
    }
}

/// <summary>Sequential, unpaced access to the project-owned FFmpeg C ABI for isolated probes and playback.</summary>
internal sealed class FfmpegMovieSession : IFfmpegFrameSource
{
    private FfmpegMovieHandle _handle;
    private readonly object _decodeLock = new();
    public FfmpegMovieInfo Info { get; }

    public FfmpegMovieSession(MoviePayload movie)
    {
        ArgumentNullException.ThrowIfNull(movie);
        if (FfmpegMovieNative.AbiVersion() != 3)
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
            nativeInfo.FrameRateNumerator, nativeInfo.FrameRateDenominator, nativeInfo.HasAudio != 0,
            nativeInfo.AudioSampleRate, nativeInfo.AudioChannels, nativeInfo.AudioFrameSamples);
        if (Info.Width <= 0 || Info.Height <= 0 || Info.StopTimeMs <= 0)
        {
            _handle.Dispose();
            throw new InvalidDataException(
                $"FFmpeg returned invalid metadata for {movie.Name}: {Info.Width}x{Info.Height}, {Info.StopTimeMs} ms");
        }
        if (Info.HasAudio && (Info.AudioSampleRate <= 0 || Info.AudioChannels != 2
                              || Info.AudioFrameSamples <= 0))
        {
            _handle.Dispose();
            throw new InvalidDataException(
                $"FFmpeg returned invalid audio metadata for {movie.Name}: " +
                $"{Info.AudioSampleRate} Hz, {Info.AudioChannels} channels, {Info.AudioFrameSamples} frames");
        }
    }

    public void Seek(long positionMs)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        int result;
        string? error = null;
        lock (_decodeLock)
        {
            result = FfmpegMovieNative.Seek(_handle, Math.Max(0, positionMs));
            if (result != 0) error = FfmpegMovieNative.LastError(_handle);
        }
        if (result != 0)
            throw new InvalidDataException($"FFmpeg movie seek failed: {error}");
    }

    public bool TryDecodeNextVideoFrame(out FfmpegVideoFrame frame)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        byte[] pixels = new byte[checked(Info.Width * Info.Height * 4)];
        int result;
        long ptsMs;
        string? error = null;
        lock (_decodeLock)
        {
            result = FfmpegMovieNative.DecodeVideo(
                _handle, pixels, (nuint)pixels.Length, out ptsMs);
            if (result != FfmpegMovieNative.EndOfFile && result != FfmpegMovieNative.Frame)
                error = FfmpegMovieNative.LastError(_handle);
        }
        if (result == FfmpegMovieNative.EndOfFile)
        {
            frame = default!;
            return false;
        }
        if (result != FfmpegMovieNative.Frame)
            throw new InvalidDataException($"FFmpeg movie decode failed: {error}");
        frame = new FfmpegVideoFrame(new RgbaImage(Info.Width, Info.Height, pixels), ptsMs);
        return true;
    }

    public bool TryDecodeNextAudioChunk(out FfmpegAudioChunk chunk)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        if (!Info.HasAudio)
        {
            chunk = default!;
            return false;
        }

        int capacity = Math.Max(Info.AudioFrameSamples, 1);
        while (true)
        {
            float[] samples = new float[checked(capacity * 2)];
            int result;
            int frameCount;
            long ptsMs;
            string? error = null;
            lock (_decodeLock)
            {
                result = FfmpegMovieNative.DecodeAudio(
                    _handle, samples, (nuint)capacity, out frameCount, out ptsMs);
                if (result != FfmpegMovieNative.EndOfFile
                    && result != FfmpegMovieNative.Frame
                    && result != FfmpegMovieNative.BufferTooSmall)
                    error = FfmpegMovieNative.LastError(_handle);
            }
            if (result == FfmpegMovieNative.EndOfFile)
            {
                chunk = default!;
                return false;
            }
            if (result == FfmpegMovieNative.BufferTooSmall)
            {
                if (frameCount <= capacity)
                    throw new InvalidDataException("FFmpeg audio decode reported an invalid required buffer size");
                capacity = frameCount;
                continue;
            }
            if (result != FfmpegMovieNative.Frame)
                throw new InvalidDataException($"FFmpeg movie audio decode failed: {error}");
            if (frameCount <= 0 || frameCount > capacity)
                throw new InvalidDataException($"FFmpeg returned invalid audio frame count {frameCount}");
            if (frameCount != capacity) Array.Resize(ref samples, checked(frameCount * 2));
            chunk = new FfmpegAudioChunk(samples, frameCount, ptsMs);
            return true;
        }
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
    internal const int BufferTooSmall = -3;
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
        public int AudioSampleRate;
        public int AudioChannels;
        public int AudioFrameSamples;
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
        string rid = RuntimeIdentifierFor(
            OperatingSystem.IsWindows(),
            OperatingSystem.IsMacOS(),
            RuntimeInformation.ProcessArchitecture);
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

    internal static string RuntimeIdentifierFor(
        bool isWindows,
        bool isMacOS,
        Architecture architecture)
        => (isWindows, isMacOS, architecture) switch
        {
            (true, _, Architecture.X64) => "win-x64",
            (true, _, Architecture.Arm64) => "win-arm64",
            (false, true, Architecture.X64) => "osx-x64",
            (false, true, Architecture.Arm64) => "osx-arm64",
            (false, false, Architecture.X64) => "linux-x64",
            (false, false, Architecture.Arm64) => "linux-arm64",
            _ => throw new PlatformNotSupportedException(
                $"The FFmpeg movie backend has no reserved RID for " +
                $"{(isWindows ? "Windows" : isMacOS ? "macOS" : "Linux")}/{architecture}."),
        };

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

    [DllImport(LibraryName, EntryPoint = "age_movie_seek", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Seek(
        FfmpegMovieHandle movie,
        long positionMs);

    [DllImport(LibraryName, EntryPoint = "age_movie_decode_audio", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int DecodeAudio(
        FfmpegMovieHandle movie,
        [Out] float[] stereo,
        nuint frameCapacity,
        out int frameCount,
        out long presentationTimeMs);

    [DllImport(LibraryName, EntryPoint = "age_movie_last_error", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr LastErrorNative(FfmpegMovieHandle movie);

    [DllImport(LibraryName, EntryPoint = "age_movie_close", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Close(IntPtr movie);
}
