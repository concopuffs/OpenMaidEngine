using System;
using System.IO;
using System.Text.Json.Nodes;
using Godot;
using Ome.Engine.Devices;
using Ome.Engine.Diagnostics;
using Ome.Engine.Frontend.Control;
using Ome.Engine.Hosting;
using Ome.Engine.Model;

// The front end's drawing: the graphics native lowers each frame to a frame description (Natives/Graphics.Frame.cs),
// and a renderer draws it. Normal play draws on the GPU; the reference renderer draws a frame the GPU renderer
// declines, every frame of a diagnostic run (--gfx-log, --timeline-log), and a --render-backend software run, and this
// shows its pixels.
public partial class Main : IFramePresenter
{
    private TextureRect _screenView = null!;              // shows the reference renderer's frame
    private Image _screen = null!;                        // SYS4INI-sized immediate-mode canvas
    private ImageTexture _screenTex = null!;
    private GpuRetainedRenderer _gpuRenderer = null!;
    private bool _useGpuBackend = true;
    private ReferenceRenderer _reference = null!;
    private GfxDecisionLog _decisionLog = null!;
    // The driver's reference-diff: once armed, the reference renderer draws every frame as well, into its own buffer,
    // so its pixels keep the history a preserving frame draws over.
    private ReferenceRenderer? _shadowReference;
    private bool _lastFrameOnGpu;
    private FrameDescription? _lastFrame;   // the description the screen shows, until the next frame is lowered

    private string? _gfxLogPath;                // --gfx-log <file>: log per-object draw/skip CHANGES

    /// <summary>Drops the prepared pixels of every catalog image, so the reference renderer takes each one again: after
    /// a language switch, its language assets apply (localization spec §12). Main thread, as the renderer.</summary>
    private void ForgetPreparedPixels() => _reference.ForgetPreparedPixels();

    private void Recomposite()
    {
        if (_perf != null)
        {
            var presentStep = _trace.LatestStep;
            _perf.RecordPresentationCoordinate(presentStep?.Script ?? "<startup>",
                                               presentStep?.Offset ?? -1, presentStep?.Opcode ?? -1);
        }
        // The decision strings of a diagnostic run are the reference renderer's evidence, so its frames draw there.
        var decisions = _decisionLog.NextFrame();
        FrameDescription frame = _graphics.LowerFrame(_clock.NowMs, _perf, decisions);
        _lastFrame = frame;
        _shadowReference?.Draw(frame);
        _lastFrameOnGpu = decisions == null && _useGpuBackend && _gpuRenderer.TryDraw(frame);
        if (_lastFrameOnGpu)
        {
            _screenView.Visible = false;
            _gpuRenderer.Visible = true;
            _perf?.EndRecomposite();
            return;
        }
        _gpuRenderer.Visible = false;
        _screenView.Visible = true;
        _reference.Draw(frame);
        if (decisions != null) _decisionLog.Record(_clock.NowMs, _pageCount);
        Present(_reference.Pixels, _reference.Width, _reference.Height);
        _perf?.EndRecomposite();
    }

    /// <summary>Show the reference renderer's frame: upload it to the screen texture.</summary>
    public void Present(byte[] rgba, int width, int height)
    {
        long phase = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
        long allocationPhase = _perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
        _screen.SetData(width, height, false, Image.Format.Rgba8, rgba);
        _perf?.RecordSetDataAllocation(PerformanceFrameLog.AllocatedBytes() - allocationPhase);
        _perf?.RecordSetData(PerformanceFrameLog.Timestamp() - phase);
        phase = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
        _screenTex.Update(_screen);
        _perf?.RecordTextureUpdate(PerformanceFrameLog.Timestamp() - phase);
    }

    /// <summary>
    /// The driver's <c>reference-diff</c>. With <paramref name="path"/> null, arm it: from the next frame on, the
    /// reference renderer draws every frame too. Otherwise compare the screen (the last frame drawn, as <c>shot</c>
    /// captures it) with the reference's drawing of the same frame, over the black the window clears to: write
    /// PATH-gpu.png, PATH-reference.png and PATH-diff.png (each channel's difference times eight; red where one is over
    /// <paramref name="tolerance"/>) and report how many pixels differ by more than it, the largest and mean channel
    /// difference, and the box around the pixels over it. Run it settled: the screen and the reference then show the
    /// same frame. It also counts the frame's draws, and the texture draws that are not a whole-pixel move (scaled,
    /// rotated or at a fractional position), where filtering may differ.
    /// </summary>
    private JsonObject DriveReferenceDiff(string? path, int tolerance)
    {
        if (path == null)
        {
            _shadowReference ??= new ReferenceRenderer(_graphics.Canvas);
            _graphics.RequestPresentation();
            return new JsonObject { ["armed"] = true };
        }
        if (_shadowReference == null)
            throw new DriveProtocolException("reference-diff on first: the reference draws from the next frame");
        Image? screen = CaptureGameImage();
        if (screen == null || screen.IsEmpty())
            throw new DriveProtocolException("no rendered frame to capture (a headless or minimized window)");
        int width = _shadowReference.Width, height = _shadowReference.Height;
        if (screen.GetWidth() != width || screen.GetHeight() != height)
            screen.Resize(width, height, Image.Interpolation.Bilinear);
        screen.Convert(Image.Format.Rgba8);
        byte[] shown = screen.GetData();
        byte[] drawn = _shadowReference.Pixels;
        var reference = new byte[drawn.Length];
        var difference = new byte[drawn.Length];
        long over = 0, sum = 0;
        int max = 0, left = width, top = height, right = -1, bottom = -1;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            int alpha = drawn[i + 3];
            int worst = 0;
            for (int c = 0; c < 3; c++)
            {
                int value = drawn[i + c] * alpha / 255;   // over the window's black
                reference[i + c] = (byte)value;
                int delta = Math.Abs(value - shown[i + c]);
                sum += delta;
                worst = Math.Max(worst, delta);
                difference[i + c] = (byte)Math.Min(255, delta * 8);
            }
            reference[i + 3] = 255;
            difference[i + 3] = 255;
            max = Math.Max(max, worst);
            if (worst <= tolerance) continue;
            over++;
            difference[i] = 255;
            left = Math.Min(left, x); top = Math.Min(top, y);
            right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        SavePng(screen, full + "-gpu.png");
        SavePng(Image.CreateFromData(width, height, false, Image.Format.Rgba8, reference), full + "-reference.png");
        SavePng(Image.CreateFromData(width, height, false, Image.Format.Rgba8, difference), full + "-diff.png");
        (int items, int transformed) = CountItems(_lastFrame);
        return new JsonObject
        {
            ["renderer"] = _lastFrameOnGpu ? "gpu" : "reference",
            ["items"] = items,
            ["transformed"] = transformed,
            ["path"] = full,
            ["pixels"] = (long)width * height,
            ["tolerance"] = tolerance,
            ["over"] = over,
            ["max"] = max,
            ["mean"] = Math.Round(sum / (3.0 * width * height), 4),
            ["bbox"] = right < 0 ? null : new JsonArray(left, top, right + 1, bottom + 1),
        };
    }

    // The frame's draws (a screen transition's source and target included), and how many of them are textures whose
    // matrix is not a whole-pixel translation.
    private static (int Items, int Transformed) CountItems(FrameDescription? frame)
    {
        int items = 0, transformed = 0;
        if (frame == null) return (0, 0);
        foreach (var list in frame.ScreenTransition is { } screen
                     ? new[] { frame.Items, screen.Source, screen.Target }
                     : new[] { frame.Items })
            foreach (FrameItem item in list)
            {
                items++;
                if (item.Kind == FrameItemKind.Texture && !WholePixelMove(item.Matrix)) transformed++;
            }
        return (items, transformed);
    }

    private static bool WholePixelMove(Affine2D m)
        => m.XX == 1 && m.YY == 1 && m.XY == 0 && m.YX == 0
           && m.TX == Math.Floor(m.TX) && m.TY == Math.Floor(m.TY);

    private static void SavePng(Image image, string path)
    {
        Error error = image.SavePng(path);
        if (error != Error.Ok) throw new DriveProtocolException($"saving {path} failed: {error}");
    }
}
