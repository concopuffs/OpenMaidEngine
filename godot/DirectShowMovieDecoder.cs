using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Age.Engine.Sys4;

[ComVisible(true), Guid("0579154A-2B53-4994-B0D0-E773148EFF85"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISampleGrabberCB
{
    [PreserveSig] int SampleCB(double time, IntPtr sample);
    [PreserveSig] int BufferCB(double time, IntPtr buffer, int length);
}

/// <summary>
/// Windows DirectShow bridge for AGE's MPEG program-stream movie payloads. The payload is supplied by
/// IAssetStore; a private temporary file only adapts those owned bytes to DirectShow's stock MPEG source.
/// Decoded RGB32 samples are copied into process memory and consumed by Godot's retained compositor.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DirectShowMovieDecoder : IDisposable, ISampleGrabberCB
{
    private readonly byte[] _payload;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly object _frameLock = new();
    private byte[]? _latestRgba;
    private int _width, _height;
    private bool _bottomUp;
    private volatile bool _stopping;
    private volatile bool _completed;
    private string? _error;
    private string? _tempPath;
    private object? _graphObject;
    private IMediaControl? _control;

    public bool IsCompleted => _completed;
    /// <summary>The graph's IMediaPosition stop time converted exactly as native op 0x23f does:
    /// seconds * 1000, truncated toward zero. Null means DirectShow supplied no usable value.</summary>
    public long? StopTimeMs { get; private set; }

    public DirectShowMovieDecoder(MoviePayload movie)
    {
        _payload = movie.Bytes;
        _thread = new Thread(DecodeThread) { IsBackground = true, Name = $"AGE movie {movie.Name}" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
        // Native 0x236 builds the graph synchronously. Preserve that initialization boundary while keeping
        // actual playback asynchronous; timeout becomes a deterministic load error rather than a VM hang.
        if (!_ready.Wait(TimeSpan.FromSeconds(10))) throw new InvalidOperationException("movie graph initialization timed out");
        if (_error != null) throw new InvalidOperationException(_error);
    }

    public bool TryTakeFrame(out RgbaImage frame)
    {
        lock (_frameLock)
        {
            if (_latestRgba == null) { frame = default!; return false; }
            frame = new RgbaImage(_width, _height, _latestRgba);
            _latestRgba = null;
            return true;
        }
    }

    private void DecodeThread()
    {
        int co = CoInitializeEx(IntPtr.Zero, 0); // COINIT_MULTITHREADED; DirectShow graph and callbacks share this apartment.
        try
        {
            _tempPath = Path.Combine(Path.GetTempPath(), $"age-movie-{Guid.NewGuid():N}.mpg");
            File.WriteAllBytes(_tempPath, _payload);

            _graphObject = Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_FilterGraph, throwOnError: true)!)!;
            var graph = (IGraphBuilder)_graphObject;
            var grabberObject = Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_SampleGrabber, true)!)!;
            var nullObject = Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_NullRenderer, true)!)!;
            var grabberFilter = (IBaseFilter)grabberObject;
            var nullFilter = (IBaseFilter)nullObject;
            var grabber = (ISampleGrabber)grabberObject;

            Check(graph.AddFilter(grabberFilter, "AGE Sample Grabber"), "add sample grabber");
            Check(graph.AddFilter(nullFilter, "AGE Null Renderer"), "add null renderer");
            var requested = new AMMediaType { MajorType = MEDIATYPE_Video, SubType = MEDIASUBTYPE_RGB32,
                                              FormatType = FORMAT_VideoInfo };
            Check(grabber.SetMediaType(ref requested), "request RGB32 movie output");
            Check(graph.AddSourceFilter(_tempPath, "AGE VFS MPEG Source", out var source), "open MPEG source");

            var sourceOut = FirstPin(source, PinDirection.Output);
            var grabberIn = FirstPin(grabberFilter, PinDirection.Input);
            var grabberOut = FirstPin(grabberFilter, PinDirection.Output);
            var nullIn = FirstPin(nullFilter, PinDirection.Input);
            // Intelligent connection inserts only the MPEG splitter/video decoder needed to reach RGB32.
            // The splitter's audio pin remains unrendered, keeping movie audio outside this slice.
            Check(graph.Connect(sourceOut, grabberIn), "connect MPEG video decoder");
            Check(graph.ConnectDirect(grabberOut, nullIn, IntPtr.Zero), "connect null renderer");

            var connected = new AMMediaType();
            Check(grabber.GetConnectedMediaType(ref connected), "query movie format");
            try
            {
                if (connected.FormatPtr == IntPtr.Zero) throw new InvalidDataException("movie decoder returned no VIDEOINFOHEADER");
                var vi = Marshal.PtrToStructure<VideoInfoHeader>(connected.FormatPtr);
                _width = vi.BitmapInfo.Width;
                _bottomUp = vi.BitmapInfo.Height > 0;
                _height = Math.Abs(vi.BitmapInfo.Height);
                if (_width <= 0 || _height <= 0) throw new InvalidDataException($"invalid movie dimensions {_width}x{_height}");
            }
            finally { FreeMediaType(ref connected); }

            Check(grabber.SetOneShot(false), "configure continuous samples");
            Check(grabber.SetBufferSamples(false), "disable redundant frame buffering");
            Check(grabber.SetCallback(this, 1), "install decoded frame callback");
            _control = (IMediaControl)_graphObject;
            Check(_control.Run(), "start movie graph");
            int stateHr = _control.GetState(5000, out int graphState);
            if (stateHr < 0) Check(stateHr, "wait for running movie graph");
            if (graphState != 2) throw new InvalidOperationException($"movie graph entered unexpected state {graphState}");
            var mediaPosition = (IMediaPosition)_graphObject;
            int stopHr = mediaPosition.get_StopTime(out double stopTimeSeconds);
            double stopTimeMilliseconds = stopTimeSeconds * 1000.0;
            if (stopHr >= 0 && double.IsFinite(stopTimeMilliseconds)
                            && stopTimeMilliseconds >= int.MinValue
                            && stopTimeMilliseconds <= int.MaxValue)
                StopTimeMs = (long)System.Math.Truncate(stopTimeMilliseconds);
            _ready.Set();
            var mediaEvent = (IMediaEvent)_graphObject;
            while (!_stopping)
            {
                int eventHr = mediaEvent.WaitForCompletion(0, out _);
                if (eventHr >= 0) { _completed = true; break; }
                if (eventHr != E_ABORT) Check(eventHr, "poll movie completion");
                Thread.Sleep(10);
            }
        }
        catch (Exception e)
        {
            _error = $"DirectShow MPEG decode failed: {e}";
            _ready.Set();
        }
        finally
        {
            try { _control?.Stop(); } catch { }
            ReleaseCom(_control); _control = null;
            ReleaseCom(_graphObject); _graphObject = null;
            if (_tempPath != null) try { File.Delete(_tempPath); } catch { }
            if (co >= 0) CoUninitialize();
        }
    }

    public int SampleCB(double sampleTime, IntPtr sample) => 0;

    public int BufferCB(double sampleTime, IntPtr buffer, int length)
    { AcceptBgra(buffer, length); return 0; }

    private void AcceptBgra(IntPtr buffer, int length)
    {
        int rowBytes = checked(_width * 4);
        if (buffer == IntPtr.Zero || length < rowBytes * _height) return;
        byte[] bgra = new byte[rowBytes * _height];
        Marshal.Copy(buffer, bgra, 0, bgra.Length);
        byte[] rgba = new byte[bgra.Length];
        for (int y = 0; y < _height; y++)
        {
            int src = (_bottomUp ? _height - 1 - y : y) * rowBytes;
            int dst = y * rowBytes;
            for (int x = 0; x < _width; x++, src += 4, dst += 4)
            {
                rgba[dst] = bgra[src + 2]; rgba[dst + 1] = bgra[src + 1];
                rgba[dst + 2] = bgra[src]; rgba[dst + 3] = 255;
            }
        }
        lock (_frameLock) _latestRgba = rgba; // newest decoded frame wins if Godot renders more slowly
    }

    public void Dispose()
    {
        _stopping = true;
        if (_thread.IsAlive) _thread.Join(TimeSpan.FromSeconds(3));
        _ready.Dispose();
    }

    private static IPin FirstPin(IBaseFilter filter, PinDirection direction)
    {
        Check(filter.EnumPins(out var pins), "enumerate source pins");
        var one = new IPin[1];
        while (pins.Next(1, one, IntPtr.Zero) == 0)
        {
            Check(one[0].QueryDirection(out var found), "query source pin direction");
            if (found == direction) return one[0];
        }
        throw new InvalidOperationException($"DirectShow source has no {direction} pin");
    }

    private static void Check(int hr, string operation)
    { if (hr < 0) throw new COMException($"{operation} failed (HRESULT 0x{hr:x8})", hr); }
    private static void ReleaseCom(object? value)
    { if (value != null && Marshal.IsComObject(value)) try { Marshal.ReleaseComObject(value); } catch { } }
    private static void FreeMediaType(ref AMMediaType mt)
    {
        if (mt.FormatPtr != IntPtr.Zero) { Marshal.FreeCoTaskMem(mt.FormatPtr); mt.FormatPtr = IntPtr.Zero; }
        if (mt.UnknownPtr != IntPtr.Zero) { Marshal.Release(mt.UnknownPtr); mt.UnknownPtr = IntPtr.Zero; }
    }

    private static readonly Guid CLSID_FilterGraph = new("E436EBB3-524F-11CE-9F53-0020AF0BA770");
    private static readonly Guid CLSID_SampleGrabber = new("C1F400A0-3F08-11D3-9F0B-006008039E37");
    private static readonly Guid CLSID_NullRenderer = new("C1F400A4-3F08-11D3-9F0B-006008039E37");
    private static readonly Guid MEDIATYPE_Video = new("73646976-0000-0010-8000-00AA00389B71");
    private static readonly Guid MEDIASUBTYPE_RGB32 = new("E436EB7E-524F-11CE-9F53-0020AF0BA770");
    private static readonly Guid FORMAT_VideoInfo = new("05589F80-C356-11CE-BF01-00AA0055595A");
    private const int E_ABORT = unchecked((int)0x80004004);

    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint coInit);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();

    private enum PinDirection { Input, Output }
    [StructLayout(LayoutKind.Sequential)] private struct AMMediaType
    {
        public Guid MajorType, SubType; [MarshalAs(UnmanagedType.Bool)] public bool FixedSizeSamples;
        [MarshalAs(UnmanagedType.Bool)] public bool TemporalCompression; public int SampleSize;
        public Guid FormatType; public IntPtr UnknownPtr; public int FormatSize; public IntPtr FormatPtr;
    }
    [StructLayout(LayoutKind.Sequential)] private struct DsRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfoHeader
    { public int Size, Width, Height; public short Planes, BitCount; public int Compression, ImageSize, XPelsPerMeter, YPelsPerMeter, ColorsUsed, ColorsImportant; }
    [StructLayout(LayoutKind.Sequential)] private struct VideoInfoHeader
    { public DsRect Source, Target; public int BitRate, BitErrorRate; public long AvgTimePerFrame; public BitmapInfoHeader BitmapInfo; }

    [ComImport, Guid("6B652FFF-11FE-4FCE-92AD-0266B5D7C78F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISampleGrabber
    {
        [PreserveSig] int SetOneShot([MarshalAs(UnmanagedType.Bool)] bool value);
        [PreserveSig] int SetMediaType(ref AMMediaType type);
        [PreserveSig] int GetConnectedMediaType(ref AMMediaType type);
        [PreserveSig] int SetBufferSamples([MarshalAs(UnmanagedType.Bool)] bool value);
        [PreserveSig] int GetCurrentBuffer(ref int size, IntPtr buffer);
        [PreserveSig] int GetCurrentSample(out IntPtr sample);
        [PreserveSig] int SetCallback(ISampleGrabberCB callback, int method);
    }
    [ComImport, Guid("56A868A9-0AD4-11CE-B03A-0020AF0BA770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphBuilder
    {
        [PreserveSig] int AddFilter(IBaseFilter filter, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int RemoveFilter(IBaseFilter filter); [PreserveSig] int EnumFilters(out IntPtr filters);
        [PreserveSig] int FindFilterByName([MarshalAs(UnmanagedType.LPWStr)] string name, out IBaseFilter filter);
        [PreserveSig] int ConnectDirect(IPin output, IPin input, IntPtr mediaType);
        [PreserveSig] int Reconnect(IPin pin); [PreserveSig] int Disconnect(IPin pin); [PreserveSig] int SetDefaultSyncSource();
        [PreserveSig] int Connect(IPin output, IPin input); [PreserveSig] int Render(IPin output);
        [PreserveSig] int RenderFile([MarshalAs(UnmanagedType.LPWStr)] string file, [MarshalAs(UnmanagedType.LPWStr)] string? playList);
        [PreserveSig] int AddSourceFilter([MarshalAs(UnmanagedType.LPWStr)] string file, [MarshalAs(UnmanagedType.LPWStr)] string name, out IBaseFilter filter);
        [PreserveSig] int SetLogFile(IntPtr file); [PreserveSig] int Abort(); [PreserveSig] int ShouldOperationContinue();
    }
    [ComImport, Guid("56A86895-0AD4-11CE-B03A-0020AF0BA770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IBaseFilter
    {
        [PreserveSig] int GetClassID(out Guid clsid); [PreserveSig] int Stop(); [PreserveSig] int Pause();
        [PreserveSig] int Run(long start); [PreserveSig] int GetState(int timeout, out int state);
        [PreserveSig] int SetSyncSource(IntPtr clock); [PreserveSig] int GetSyncSource(out IntPtr clock);
        [PreserveSig] int EnumPins(out IEnumPins pins); [PreserveSig] int FindPin([MarshalAs(UnmanagedType.LPWStr)] string id, out IPin pin);
        [PreserveSig] int QueryFilterInfo(IntPtr info); [PreserveSig] int JoinFilterGraph(IntPtr graph, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int QueryVendorInfo([MarshalAs(UnmanagedType.LPWStr)] out string vendor);
    }
    [ComImport, Guid("56A86892-0AD4-11CE-B03A-0020AF0BA770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumPins
    { [PreserveSig] int Next(int count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IPin[] pins, IntPtr fetched); [PreserveSig] int Skip(int count); [PreserveSig] int Reset(); [PreserveSig] int Clone(out IEnumPins clone); }
    [ComImport, Guid("56A86891-0AD4-11CE-B03A-0020AF0BA770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPin
    {
        [PreserveSig] int Connect(IPin receive, IntPtr mediaType); [PreserveSig] int ReceiveConnection(IPin connector, IntPtr mediaType);
        [PreserveSig] int Disconnect(); [PreserveSig] int ConnectedTo(out IPin pin); [PreserveSig] int ConnectionMediaType(IntPtr mediaType);
        [PreserveSig] int QueryPinInfo(IntPtr info); [PreserveSig] int QueryDirection(out PinDirection direction);
        [PreserveSig] int QueryId([MarshalAs(UnmanagedType.LPWStr)] out string id); [PreserveSig] int QueryAccept(IntPtr mediaType);
        [PreserveSig] int EnumMediaTypes(out IntPtr types); [PreserveSig] int QueryInternalConnections(IntPtr pins, ref int count);
        [PreserveSig] int EndOfStream(); [PreserveSig] int BeginFlush(); [PreserveSig] int EndFlush(); [PreserveSig] int NewSegment(long start, long stop, double rate);
    }
    [ComImport, Guid("56A868B1-0AD4-11CE-B03A-0020AF0BA770"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IMediaControl
    { [PreserveSig] int Run(); [PreserveSig] int Pause(); [PreserveSig] int Stop(); [PreserveSig] int GetState(int timeout, out int state); }
    [ComImport, Guid("56A868B2-0AD4-11CE-B03A-0020AF0BA770"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IMediaPosition
    {
        [PreserveSig] int get_Duration(out double seconds);
        [PreserveSig] int put_CurrentPosition(double seconds);
        [PreserveSig] int get_CurrentPosition(out double seconds);
        [PreserveSig] int get_StopTime(out double seconds);
        [PreserveSig] int put_StopTime(double seconds);
        [PreserveSig] int get_PrerollTime(out double seconds);
        [PreserveSig] int put_PrerollTime(double seconds);
        [PreserveSig] int put_Rate(double rate);
        [PreserveSig] int get_Rate(out double rate);
        [PreserveSig] int CanSeekForward(out int canSeekForward);
        [PreserveSig] int CanSeekBackward(out int canSeekBackward);
    }
    [ComImport, Guid("56A868B6-0AD4-11CE-B03A-0020AF0BA770"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IMediaEvent
    {
        [PreserveSig] int GetEventHandle(out IntPtr eventHandle);
        [PreserveSig] int GetEvent(out int eventCode, out IntPtr param1, out IntPtr param2, int timeoutMs);
        [PreserveSig] int WaitForCompletion(int timeoutMs, out int eventCode);
        [PreserveSig] int CancelDefaultHandling(int eventCode);
        [PreserveSig] int RestoreDefaultHandling(int eventCode);
        [PreserveSig] int FreeEventParams(int eventCode, IntPtr param1, IntPtr param2);
    }
}
