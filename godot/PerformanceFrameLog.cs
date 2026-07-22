using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Age.Engine.Model;

/// <summary>
/// Buffered, diagnostic-only CSV writer for real Godot frame and retained-compositor cost. The writer is
/// deliberately independent of Godot types so its schema and clipping arithmetic can be unit tested.
/// </summary>
public sealed class PerformanceFrameLog : IDisposable
{
    private const int FlushIntervalFrames = 120;
    private static readonly double MillisecondsPerTick = 1000.0 / Stopwatch.Frequency;
    private readonly StreamWriter _writer;
    private Frame _current = new();
    private bool _frameOpen;
    private bool _disposed;
    private int _framesSinceFlush;

    public long FrameCount { get; private set; }
    public long RecompositeCount { get; private set; }
    public string Path { get; }

    public PerformanceFrameLog(string path)
    {
        Path = path;
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        _writer = new StreamWriter(path, append: false, Encoding.UTF8, 64 * 1024);
        // Presentation coordinates are appended separately so a VM thread released by PulseFrame can be
        // distinguished from the coordinate observed at _Process entry.
        _writer.WriteLine(
            "frame,now_ms,delta_ms,main_ms,pulse_ms,movie_ms,should_recomposite_ms,recomposite_ms," +
            "clear_ms,snapshot_ms,resolve_ms,source_prep_ms,raster_ms,set_data_ms,texture_update_ms,ui_ms," +
            "allocated_bytes,recompose_allocated_bytes,snapshot_allocated_bytes," +
            "composite_allocated_bytes,source_prep_allocated_bytes,set_data_allocated_bytes," +
            "ui_allocated_bytes,gen0,gen1,gen2,recomposited,render_backend,screen_transition," +
            "gpu_draw_items,gpu_texture_uploads,gpu_texture_upload_ms," +
            "present_host_request,present_screen_transition,present_retained_mutation," +
            "present_continuous_channel,present_discrete_cell,object_visits," +
            "time_varying_objects,draw_layers,fill_layers,transition_layers,skipped_layers," +
            "integer_layers,fractional_translation_layers,axis_aligned_scale_layers," +
            "general_affine_layers,affine_layers,singular_layers,dynamic_layers,opaque_layers,alpha_layers," +
            "additive_layers,source_pixels,candidate_pixels,full_screen_layers,script,offset,opcode," +
            "present_script,present_offset,present_opcode");
    }

    public static long Timestamp() => Stopwatch.GetTimestamp();
    public static long AllocatedBytes() => GC.GetAllocatedBytesForCurrentThread();

    public void BeginFrame(int frame, long nowMs, double deltaSeconds,
                           string script, int offset, int opcode)
    {
        if (_disposed) return;
        if (_frameOpen) EndFrame();
        _current = new Frame
        {
            Number = frame,
            NowMs = nowMs,
            DeltaMs = deltaSeconds * 1000.0,
            Script = script,
            Offset = offset,
            Opcode = opcode,
            Started = Timestamp(),
            AllocatedStart = GC.GetAllocatedBytesForCurrentThread(),
            Gen0Start = GC.CollectionCount(0),
            Gen1Start = GC.CollectionCount(1),
            Gen2Start = GC.CollectionCount(2),
        };
        _frameOpen = true;
    }

    public void RecordPulse(long ticks) => _current.PulseTicks += ticks;
    public void RecordMovies(long ticks) => _current.MovieTicks += ticks;
    public void RecordShouldRecomposite(long ticks) => _current.ShouldTicks += ticks;
    public void RecordPresentationReasons(int reasons)
    {
        _current.PresentHostRequest |= (reasons & 1) != 0;
        _current.PresentScreenTransition |= (reasons & 2) != 0;
        _current.PresentRetainedMutation |= (reasons & 4) != 0;
        _current.PresentContinuousChannel |= (reasons & 8) != 0;
        _current.PresentDiscreteCell |= (reasons & 16) != 0;
    }
    public void RecordUi(long ticks) => _current.UiTicks += ticks;
    public void RecordClear(long ticks) => _current.ClearTicks += ticks;
    public void RecordSnapshot(long ticks) => _current.SnapshotTicks += ticks;
    public void RecordResolve(long ticks) => _current.ResolveTicks += ticks;
    public void RecordSourcePrep(long ticks) => _current.SourcePrepTicks += ticks;
    public void RecordRecomposeAllocation(long bytes) => _current.RecomposeAllocatedBytes += Math.Max(0, bytes);
    public void RecordSnapshotAllocation(long bytes) => _current.SnapshotAllocatedBytes += Math.Max(0, bytes);
    public void RecordCompositeAllocation(long bytes) => _current.CompositeAllocatedBytes += Math.Max(0, bytes);
    public void RecordSourcePrepAllocation(long bytes) => _current.SourcePrepAllocatedBytes += Math.Max(0, bytes);
    public void RecordSetDataAllocation(long bytes) => _current.SetDataAllocatedBytes += Math.Max(0, bytes);
    public void RecordUiAllocation(long bytes) => _current.UiAllocatedBytes += Math.Max(0, bytes);
    public void RecordSetData(long ticks) => _current.SetDataTicks += ticks;
    public void RecordTextureUpdate(long ticks) => _current.TextureUpdateTicks += ticks;
    public void RecordGpu(int drawItems, int textureUploads, long textureUploadTicks)
    {
        _current.GpuBackend = true;
        _current.GpuDrawItems += drawItems;
        _current.GpuTextureUploads += textureUploads;
        _current.GpuTextureUploadTicks += textureUploadTicks;
    }

    public void BeginRecomposite(bool screenTransition)
    {
        _current.Recomposited = true;
        _current.ScreenTransition |= screenTransition;
        _current.RecompositeStarted = Timestamp();
    }

    public void RecordPresentationCoordinate(string script, int offset, int opcode)
    {
        _current.PresentScript = script;
        _current.PresentOffset = offset;
        _current.PresentOpcode = opcode;
    }

    public void EndRecomposite()
    {
        if (_current.RecompositeStarted == 0) return;
        _current.RecompositeTicks += Timestamp() - _current.RecompositeStarted;
        _current.RecompositeStarted = 0;
    }

    public void RecordObject(bool timeVarying)
    {
        _current.ObjectVisits++;
        if (timeVarying) _current.TimeVaryingObjects++;
    }

    public void RecordFillLayer() => _current.FillLayers++;
    public void RecordTransitionLayer() => _current.TransitionLayers++;
    public void RecordSkippedLayer() => _current.SkippedLayers++;

    public void RecordRaster(int sourceWidth, int sourceHeight, Affine2D localToDest,
                             int destinationWidth, int destinationHeight, bool dynamic,
                             BlendKind blend, long ticks)
    {
        _current.RasterTicks += ticks;
        RecordLayer(sourceWidth, sourceHeight, localToDest, destinationWidth, destinationHeight,
                    dynamic, blend);
    }

    public void RecordGpuLayer(int sourceWidth, int sourceHeight, Affine2D localToDest,
                               int destinationWidth, int destinationHeight, bool dynamic,
                               BlendKind blend)
        => RecordLayer(sourceWidth, sourceHeight, localToDest, destinationWidth, destinationHeight,
                       dynamic, blend);

    private void RecordLayer(int sourceWidth, int sourceHeight, Affine2D localToDest,
                             int destinationWidth, int destinationHeight, bool dynamic,
                             BlendKind blend)
    {
        _current.DrawLayers++;
        _current.SourcePixels += Math.Max(0L, (long)sourceWidth * sourceHeight);
        long candidates = EstimateCandidatePixels(localToDest, sourceWidth, sourceHeight,
                                                   destinationWidth, destinationHeight);
        _current.CandidatePixels += candidates;
        if (candidates >= (long)destinationWidth * destinationHeight) _current.FullScreenLayers++;
        if (IsIntegerTranslation(localToDest)) _current.IntegerLayers++;
        else if (!localToDest.TryInverse(out _)) _current.SingularLayers++;
        else
        {
            _current.AffineLayers++;
            if (IsTranslation(localToDest)) _current.FractionalTranslationLayers++;
            else if (IsAxisAligned(localToDest)) _current.AxisAlignedScaleLayers++;
            else _current.GeneralAffineLayers++;
        }
        if (dynamic) _current.DynamicLayers++;
        switch (blend)
        {
            case BlendKind.Opaque: _current.OpaqueLayers++; break;
            case BlendKind.Additive: _current.AdditiveLayers++; break;
            default: _current.AlphaLayers++; break;
        }
    }

    public void EndFrame()
    {
        if (!_frameOpen || _disposed) return;
        long ended = Timestamp();
        _current.MainTicks = ended - _current.Started;
        _current.AllocatedBytes = Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - _current.AllocatedStart);
        _current.Gen0 = GC.CollectionCount(0) - _current.Gen0Start;
        _current.Gen1 = GC.CollectionCount(1) - _current.Gen1Start;
        _current.Gen2 = GC.CollectionCount(2) - _current.Gen2Start;
        Write(_current);
        FrameCount++;
        if (_current.Recomposited) RecompositeCount++;
        _frameOpen = false;
        if (++_framesSinceFlush >= FlushIntervalFrames)
        {
            _writer.Flush();
            _framesSinceFlush = 0;
        }
    }

    public static bool IsIntegerTranslation(Affine2D m)
        => IsTranslation(m) &&
           m.TX == Math.Truncate(m.TX) && m.TY == Math.Truncate(m.TY) &&
           m.TX >= int.MinValue && m.TX <= int.MaxValue &&
           m.TY >= int.MinValue && m.TY <= int.MaxValue;

    public static bool IsTranslation(Affine2D m)
        => m.XX == 1 && m.XY == 0 && m.YX == 0 && m.YY == 1;

    public static bool IsAxisAligned(Affine2D m)
        => m.XY == 0 && m.YX == 0;

    public static long EstimateCandidatePixels(Affine2D m, int width, int height,
                                                int destinationWidth, int destinationHeight)
    {
        if (width <= 0 || height <= 0 || destinationWidth <= 0 || destinationHeight <= 0) return 0;
        var a = m.Apply(0, 0);
        var b = m.Apply(width, 0);
        var c = m.Apply(0, height);
        var d = m.Apply(width, height);
        double left = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
        double top = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
        double right = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
        double bottom = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
        long x0 = Math.Max(0, ClampFloor(left));
        long y0 = Math.Max(0, ClampFloor(top));
        long x1 = Math.Min(destinationWidth, ClampCeiling(right));
        long y1 = Math.Min(destinationHeight, ClampCeiling(bottom));
        return x1 <= x0 || y1 <= y0 ? 0 : checked((x1 - x0) * (y1 - y0));
    }

    private static long ClampFloor(double value)
        => !double.IsFinite(value) ? 0 : value <= long.MinValue ? long.MinValue
           : value >= long.MaxValue ? long.MaxValue : (long)Math.Floor(value);

    private static long ClampCeiling(double value)
        => !double.IsFinite(value) ? 0 : value <= long.MinValue ? long.MinValue
           : value >= long.MaxValue ? long.MaxValue : (long)Math.Ceiling(value);

    private void Write(Frame f)
    {
        var b = new StringBuilder(512);
        Append(b, f.Number); Append(b, f.NowMs); Append(b, f.DeltaMs);
        AppendTicks(b, f.MainTicks); AppendTicks(b, f.PulseTicks); AppendTicks(b, f.MovieTicks);
        AppendTicks(b, f.ShouldTicks); AppendTicks(b, f.RecompositeTicks); AppendTicks(b, f.ClearTicks);
        AppendTicks(b, f.SnapshotTicks); AppendTicks(b, f.ResolveTicks); AppendTicks(b, f.SourcePrepTicks);
        AppendTicks(b, f.RasterTicks);
        AppendTicks(b, f.SetDataTicks); AppendTicks(b, f.TextureUpdateTicks); AppendTicks(b, f.UiTicks);
        Append(b, f.AllocatedBytes); Append(b, f.RecomposeAllocatedBytes);
        Append(b, f.SnapshotAllocatedBytes); Append(b, f.CompositeAllocatedBytes);
        Append(b, f.SourcePrepAllocatedBytes); Append(b, f.SetDataAllocatedBytes);
        Append(b, f.UiAllocatedBytes); Append(b, f.Gen0); Append(b, f.Gen1); Append(b, f.Gen2);
        Append(b, f.Recomposited ? 1 : 0); Append(b, f.GpuBackend ? 1 : 0);
        Append(b, f.ScreenTransition ? 1 : 0);
        Append(b, f.GpuDrawItems); Append(b, f.GpuTextureUploads); AppendTicks(b, f.GpuTextureUploadTicks);
        Append(b, f.PresentHostRequest ? 1 : 0); Append(b, f.PresentScreenTransition ? 1 : 0);
        Append(b, f.PresentRetainedMutation ? 1 : 0); Append(b, f.PresentContinuousChannel ? 1 : 0);
        Append(b, f.PresentDiscreteCell ? 1 : 0);
        Append(b, f.ObjectVisits); Append(b, f.TimeVaryingObjects); Append(b, f.DrawLayers);
        Append(b, f.FillLayers); Append(b, f.TransitionLayers); Append(b, f.SkippedLayers);
        Append(b, f.IntegerLayers); Append(b, f.FractionalTranslationLayers);
        Append(b, f.AxisAlignedScaleLayers); Append(b, f.GeneralAffineLayers);
        Append(b, f.AffineLayers); Append(b, f.SingularLayers);
        Append(b, f.DynamicLayers); Append(b, f.OpaqueLayers); Append(b, f.AlphaLayers);
        Append(b, f.AdditiveLayers); Append(b, f.SourcePixels); Append(b, f.CandidatePixels);
        Append(b, f.FullScreenLayers); AppendEscaped(b, f.Script); Append(b, f.Offset);
        Append(b, f.Opcode); AppendEscaped(b, f.PresentScript); Append(b, f.PresentOffset);
        Append(b, f.PresentOpcode, last: true);
        _writer.WriteLine(b.ToString());
    }

    private static void AppendTicks(StringBuilder b, long ticks)
        => Append(b, ticks * MillisecondsPerTick);

    private static void Append(StringBuilder b, long value, bool last = false)
    {
        b.Append(value.ToString(CultureInfo.InvariantCulture));
        if (!last) b.Append(',');
    }

    private static void Append(StringBuilder b, double value)
    {
        b.Append(value.ToString("0.0000", CultureInfo.InvariantCulture));
        b.Append(',');
    }

    private static void AppendEscaped(StringBuilder b, string value)
    {
        b.Append('"').Append(value.Replace("\"", "\"\"")).Append("\",");
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_frameOpen) EndFrame();
        _disposed = true;
        _writer.Dispose();
    }

    private sealed class Frame
    {
        public int Number, Offset, Opcode;
        public long NowMs, Started, MainTicks, PulseTicks, MovieTicks, ShouldTicks, RecompositeTicks;
        public long RecompositeStarted, ClearTicks, SnapshotTicks, ResolveTicks, SourcePrepTicks, RasterTicks;
        public long SetDataTicks, TextureUpdateTicks, UiTicks, AllocatedStart, AllocatedBytes;
        public long RecomposeAllocatedBytes, SnapshotAllocatedBytes, CompositeAllocatedBytes;
        public long SourcePrepAllocatedBytes, SetDataAllocatedBytes, UiAllocatedBytes;
        public int Gen0Start, Gen1Start, Gen2Start, Gen0, Gen1, Gen2;
        public double DeltaMs;
        public string Script = "<unknown>";
        public string PresentScript = "<none>";
        public int PresentOffset = -1, PresentOpcode = -1;
        public bool Recomposited, GpuBackend, ScreenTransition;
        public bool PresentHostRequest, PresentScreenTransition, PresentRetainedMutation;
        public bool PresentContinuousChannel, PresentDiscreteCell;
        public long ObjectVisits, TimeVaryingObjects, DrawLayers, FillLayers, TransitionLayers, SkippedLayers;
        public long IntegerLayers, FractionalTranslationLayers, AxisAlignedScaleLayers;
        public long GeneralAffineLayers, AffineLayers, SingularLayers, DynamicLayers;
        public long OpaqueLayers, AlphaLayers, AdditiveLayers, SourcePixels, CandidatePixels, FullScreenLayers;
        public long GpuDrawItems, GpuTextureUploads, GpuTextureUploadTicks;
    }
}
