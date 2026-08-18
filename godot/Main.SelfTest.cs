using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Age.Engine.Diagnostics;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Text;
using Age.Engine.Vm;
using Script = Age.Engine.Model.Script;

public partial class Main
{
    // Verifies that the Godot plumbing (background thread, semaphore suspension on input,
    // and deferred main-thread calls) drives the VM identically to a headless run.
    private void RunSelfTest()
    {
        var table = OmeRuntimeMetadata.LoadOpcodeTable();
        var (script, provider) = BuildSelfTestScene(table);
        var headless = new VirtualMachine(script, table, new CaptureHost(), null, provider);
        headless.Run();
        var expected = headless.Emitted.ConvertAll(e => e.Offset);

        var actual = _host.Captured.ConvertAll(c => c.Offset);
        bool ok = actual.Count == expected.Count;
        for (int i = 0; ok && i < actual.Count; i++) ok = actual[i] == expected[i];
        var debugEntries = DebugSceneCatalog.Build(_catalog);
        bool launcherOk = debugEntries.Any(entry => entry.Name == "DEBUG.BIN" && entry.Launchable)
                          && debugEntries.Select(entry => entry.PackedId).Distinct().Count() == debugEntries.Count;
        var launcherSmoke = new DebugSceneLauncher();
        AddChild(launcherSmoke);
        launcherSmoke.Open(debugEntries, "SYSTEM4.BIN > TITLE.BIN", present: false);
        launcherSmoke.Free();
        bool sleepMinimumOk = GodotAdvHost.NormalizeSleepMilliseconds(0, 1.0) == 1
                              && GodotAdvHost.NormalizeSleepMilliseconds(100, 1.0) == 100;
        bool inputTranslationOk = Win32VirtualKeyTranslator.TryTranslate(
                                      new InputEventKey { PhysicalKeycode = Key.Z }, out int zVk) && zVk == 0x5a
                                  && Win32VirtualKeyTranslator.TryTranslate(
                                      new InputEventKey { PhysicalKeycode = Key.Up }, out int upVk) && upVk == 0x26
                                  && Win32VirtualKeyTranslator.TryTranslate(
                                      new InputEventKey { PhysicalKeycode = Key.Ctrl }, out int ctrlVk) && ctrlVk == 0x11;
        var selftestResources = new ResourceMap(_catalog, _assetStore);
        AudioPayload glowSfx = selftestResources.ReadAudio(selftestResources.ResolveSoundEffect(0x28)!);
        byte[] glowGodotWav = RiffWaveSanitizer.PrepareForGodot(glowSfx.Bytes);
        bool cp932WavMetadataOk = glowGodotWav.Length == 688_336
                                 && AudioStreamWav.LoadFromBuffer(glowGodotWav) != null;
        AudioPayload bossSfx = selftestResources.ReadAudio(
            selftestResources.ResolveSoundEffect(0x125)!);
        byte[] bossGodotWav = RiffWaveSanitizer.PrepareForGodot(bossSfx.Bytes);
        bool firstRiffBoundaryOk = bossSfx.Bytes.Length == 323_009
                                   && bossGodotWav.Length == 157_940
                                   && AudioStreamWav.LoadFromBuffer(bossGodotWav) != null;
        AudioPayload bgm = selftestResources.ReadAudio(selftestResources.ResolveBgm(5)!);
        FadeBgm(0, 10.0);
        bool bgmFadeStarted = _bgmFadeTween?.IsValid() == true;
        PlayBgm(bgm.Bytes, bgm.Name);
        bool bgmReplacementCancelsFade = bgmFadeStarted
                                         && _bgmFadeTween == null
                                         && System.Math.Abs(_bgm.VolumeDb) < 0.001f;
        RestartBgm(bgm.Bytes, bgm.Name, 0);
        bool bgmOneShotModeOk = (_bgm.Stream as AudioStreamOggVorbis)?.Loop == false;
        RestartBgm(bgm.Bytes, bgm.Name, 1);
        bool bgmLoopModeOk = (_bgm.Stream as AudioStreamOggVorbis)?.Loop == true;
        StopBgm();
        bool bgmStopReleaseOk = _bgm.Stream == null
                                && !_bgm.Playing
                                && _bgmFadeTween == null
                                && System.Math.Abs(_bgm.VolumeDb) < 0.001f;
        IReadOnlyList<(int X, int Y)> outline =
            AgeGlyphMaskCompositor.GetMode3OutlineOffsets(1, 1);
        bool textEffectModesOk =
            outline.Count == 12
            && outline.Contains((1, 0))
            && outline.Contains((-1, 0))
            && outline.Contains((0, 1))
            && outline.Contains((0, -1));
        bool fontCalibrationOk =
            ImmediateSurfaceTextRenderer.NativePixelHeight(24) == 24
            && ImmediateSurfaceTextRenderer.NativePixelHeight(25) == 24
            && ImmediateSurfaceTextRenderer.NativePixelHeight(32) == 31
            && ImmediateSurfaceTextRenderer.NativePixelHeight(33) == 31;
        bool immediateSurfaceTextOk;
        string immediateSurfaceTextMode;
        _host.CreateTexture(997, 64, 24);
        var immediateStyle = AdvTextStyle.Default with
        {
            PrimaryFontSize = 16,
            TextColor = 0xffffff,
            FontFace = ImmediateSurfaceTextRenderer.DefaultFontFace,
        };
        _host.DrawStringToSurface(997, 1, 1, "A姫", immediateStyle);
        if (_host.UsesSurfaceTextPixels)
        {
            RgbaImage? pixels = _host.CaptureSurfacePixels(997);
            GlyphRasterizerBackendInfo? backend = _host.SurfaceTextBackendInfo;
            var cache = _host.SurfaceTextMaskCacheStats;
            bool pixelsPresent = pixels != null
                                 && pixels.Pixels.Where((_, index) => index % 4 == 3)
                                     .Any(alpha => alpha != 0);
            bool gpuAccepted = false;
            if (pixels != null)
            {
                _gpuRenderer.BeginFrame(false);
                gpuAccepted = _gpuRenderer.DrawTexture(
                    pixels, int.MinValue + 997, -1,
                    0, 0, pixels.Width, pixels.Height,
                    new Affine2D(1, 0, 0, 1, 2, 3),
                    0xff8080, 255, 0.5f, true,
                    dynamic: true, dynamicKey: 997, BlendKind.Alpha);
                GpuRetainedRenderer.FrameStats stats = _gpuRenderer.EndFrame();
                gpuAccepted &= stats.DrawItems == 1 && stats.TextureUploads == 1;
            }

            _host.CreateTexture(996, 64, 24);
            _host.CopySurfaceRect(new SurfaceRectCopy(
                997, 996, 0, 0, 64, 24, 0, 0));
            RgbaImage? copied = _host.CaptureSurfacePixels(996);
            bool copiedPixels = pixels != null
                                && copied != null
                                && pixels.Pixels.SequenceEqual(copied.Pixels);
            _host.FillSurfaceRect(new SurfaceRectFill(
                996, 0, 0, 64, 24, 0, 0));
            RgbaImage? cleared = _host.CaptureSurfacePixels(996);
            bool clearedPixels = cleared != null && cleared.Pixels.All(value => value == 0);
            immediateSurfaceTextOk =
                pixelsPresent
                && backend is not null
                && (backend.Policy == GlyphRasterPolicy.NativeCp932Gray4
                    ? backend is
                    {
                        Id: "windows-gdi-gray4",
                        NativePixelExact: true,
                    }
                    : backend is
                    {
                        Id: "portable-godot-textserver",
                        Policy: GlyphRasterPolicy.PortableUnicode,
                        NativePixelExact: false,
                    })
                && cache.Count is > 0 and <= 2048
                && cache.Capacity == 2048
                && gpuAccepted
                && copiedPixels
                && clearedPixels;
            immediateSurfaceTextMode =
                $"rgba:{backend?.Id}";
        }
        else
        {
            immediateSurfaceTextOk = false;
            immediateSurfaceTextMode = "unavailable";
        }
        _host.ReleaseSurface(996);
        _host.ReleaseSurface(997);
        bool liveRetainedTextOk;
        string liveRetainedTextMode;
        AdvTextLayoutPresentationBinding liveBinding =
            _vm.TextHistory.GetPresentationBinding(1);
        if (_host.UsesSurfaceTextPixels)
        {
            static bool InLiveRange(
                RenderObject item, AdvTextLayoutPresentationBinding binding)
                => item.Handle >= binding.FirstObjectHandle
                   && item.Handle - binding.FirstObjectHandle < binding.ObjectCapacity;
            IReadOnlyList<RenderObject> liveObjects = _vm.Gfx.SnapshotVisibleObjects()
                .Where(item => InLiveRange(item, liveBinding))
                .ToArray();
            RgbaImage? liveSurface =
                _host.CaptureSurfacePixels(liveBinding.SourceSurfaceSlot);
            bool builtCompleteLine =
                liveObjects.Count == "HelloWorldSub".Length
                && liveObjects.Select(item => item.Handle)
                    .SequenceEqual(Enumerable.Range(0, liveObjects.Count)
                        .Select(index => liveBinding.FirstObjectHandle + index))
                && liveSurface != null
                && liveSurface.Pixels.Where((_, index) => index % 4 == 3)
                    .Any(alpha => alpha != 0)
                && _vm.TextHistory.GetLayoutSnapshot(1).CursorX
                   > liveBinding.ResetCursorX;

            _vm.Gfx.EraseRange(liveBinding.FirstObjectHandle + 1, 1);
            _host.PublishAdvTextLayout(_vm.Gfx, liveBinding);
            bool republishedPartialErase =
                _vm.Gfx.SnapshotVisibleObjects()
                    .Count(item => InLiveRange(item, liveBinding))
                == liveObjects.Count;

            _host.SetAdvPagePresentationSuspended(_vm.Gfx, true);
            bool suspended =
                !_vm.Gfx.SnapshotVisibleObjects()
                    .Any(item => InLiveRange(item, liveBinding));
            _host.SetAdvPagePresentationSuspended(_vm.Gfx, false);
            bool restored =
                _vm.Gfx.SnapshotVisibleObjects()
                    .Count(item => InLiveRange(item, liveBinding))
                == liveObjects.Count;

            _vm.Gfx.EraseRange(
                liveBinding.FirstObjectHandle, liveBinding.ObjectCapacity);
            _host.ResetRenderedAdvTextLayout(_vm.Gfx, liveBinding);
            RgbaImage? resetSurface =
                _host.CaptureSurfacePixels(liveBinding.SourceSurfaceSlot);
            bool reset =
                resetSurface != null
                && resetSurface.Pixels.All(value => value == 0)
                && !_vm.Gfx.SnapshotVisibleObjects()
                    .Any(item => InLiveRange(item, liveBinding));
            liveRetainedTextOk =
                builtCompleteLine
                && republishedPartialErase
                && suspended
                && restored
                && reset;
            liveRetainedTextMode =
                $"retained-glyphs;built={builtCompleteLine};objects={liveObjects.Count};" +
                $"republished={republishedPartialErase};suspended={suspended};" +
                $"restored={restored};reset={reset}";
        }
        else
        {
            liveRetainedTextOk = false;
            liveRetainedTextMode = "unavailable";
        }
        _vm.TextHistory.DefineLayout(2, 256, 64, 10, 100);
        _vm.TextHistory.SetResetCursor(2, 1, 1);
        _vm.TextHistory.SetBounds(2, 255, 63);
        _vm.TextHistory.SetTextObjectRange(2, 710000, 64);
        _vm.TextHistory.ResetLayout(2);
        AdvTextLayoutSnapshot historyLayout =
            _vm.TextHistory.GetLayoutSnapshot(2);
        AdvTextLayoutPresentationBinding historyBinding =
            _vm.TextHistory.GetPresentationBinding(2);
        _host.ResetRenderedAdvTextLayout(_vm.Gfx, historyBinding);
        var historyBatch = new AdvTextHistoryRenderBatch(
            2,
            0,
            0,
            historyLayout,
            "History",
            immediateStyle);
        bool historyUsedRetained =
            _host.RenderTextHistory(
                _vm.Gfx,
                historyBinding,
                historyBatch);
        bool historyRetainedTextOk;
        string historyRetainedTextMode;
        if (_host.UsesSurfaceTextPixels)
        {
            RgbaImage? historySurface =
                _host.CaptureSurfacePixels(historyBinding.SourceSurfaceSlot);
            historyRetainedTextOk =
                historyUsedRetained
                && _vm.Gfx.SnapshotVisibleObjects()
                    .Count(item =>
                        item.Handle >= historyBinding.FirstObjectHandle
                        && item.Handle - historyBinding.FirstObjectHandle
                           < historyBinding.ObjectCapacity)
                   == historyBatch.Text.Length
                && historySurface != null
                && historySurface.Pixels.Where((_, index) => index % 4 == 3)
                    .Any(alpha => alpha != 0);
            _vm.Gfx.EraseRange(
                historyBinding.FirstObjectHandle,
                historyBinding.ObjectCapacity);
            _host.ClearRenderedAdvTextLayout(historyBinding.LayoutSlot);
            _host.EndTextHistoryPresentation(_vm.Gfx);
            historyRetainedTextOk &=
                !_vm.Gfx.SnapshotVisibleObjects()
                    .Any(item =>
                        item.Handle >= historyBinding.FirstObjectHandle
                        && item.Handle - historyBinding.FirstObjectHandle
                           < historyBinding.ObjectCapacity)
                && _host.CaptureSurfacePixels(historyBinding.SourceSurfaceSlot)
                    ?.Pixels.All(value => value == 0) == true;
            historyRetainedTextMode =
                $"retained-glyphs;used={historyUsedRetained}";
        }
        else
        {
            historyRetainedTextOk = false;
            _host.EndTextHistoryPresentation(_vm.Gfx);
            historyRetainedTextMode = "unavailable";
        }
        Window rootWindow = GetTree().Root;
        bool logicalCanvasOk = _host.LogicalCanvas == new Sys4LogicalCanvas(_screenWidth, _screenHeight)
                               && _screen.GetWidth() == _screenWidth
                               && _screen.GetHeight() == _screenHeight
                               && _screenPixels.Length == checked(_screenWidth * _screenHeight * 4)
                               && rootWindow.ContentScaleSize
                                  == new Vector2I(_screenWidth, _screenHeight)
                               && _windowOptions == WindowLaunchOptions.Resolve(
                                      OS.GetCmdlineUserArgs(),
                                      new Sys4LogicalCanvas(_screenWidth, _screenHeight));
        var backbufferSnapshot = new List<RenderObject>();
        _host.PresentFrame(_vm.Gfx);
        BackbufferPublicationPolicy fullPolicy =
            _host.SnapshotBackbufferObjects(
                _vm.Gfx, _clock.NowMs, backbufferSnapshot);
        bool backbufferPreservationOk =
            fullPolicy
                == new BackbufferPublicationPolicy(
                    PreserveExistingPixels: true,
                    AppendGpuLayers: backbufferSnapshot.Count == 0)
            && GodotAdvHost.ResolveBackbufferPublicationPolicy(
                true, GfxHandleRange.All, 1)
                == new BackbufferPublicationPolicy(true, false)
            && GodotAdvHost.ResolveBackbufferPublicationPolicy(
                true, new GfxHandleRange(0, 60000), 1)
                == new BackbufferPublicationPolicy(true, true);
        _host.ClearRenderTarget(-1);
        _host.PresentFrame(_vm.Gfx);
        BackbufferPublicationPolicy clearedPolicy =
            _host.SnapshotBackbufferObjects(
                _vm.Gfx, _clock.NowMs, backbufferSnapshot);
        backbufferPreservationOk &=
            clearedPolicy == new BackbufferPublicationPolicy(false, false);
        ok &= launcherOk && sleepMinimumOk && inputTranslationOk && cp932WavMetadataOk
              && firstRiffBoundaryOk
              && bgmReplacementCancelsFade && bgmOneShotModeOk && bgmLoopModeOk
              && bgmStopReleaseOk && textEffectModesOk && fontCalibrationOk
              && immediateSurfaceTextOk
              && liveRetainedTextOk
              && historyRetainedTextOk
              && logicalCanvasOk && backbufferPreservationOk;
        if (ok) GD.Print($"SELFTEST OK: threaded host matches headless ({actual.Count} lines, full handling); " +
                         $"debug launcher catalog/UI smoke ({debugEntries.Count} packed scripts); " +
                         $"sleep-min=1ms; native-key-translation=ok; cp932-wav-info=ok; " +
                         $"first-riff-boundary=ok; " +
                         $"bgm-fade-replacement=ok; bgm-start-modes-stop=ok; " +
                         $"text-effect-modes=ok; font-calibration=ok; " +
                         $"immediate-surface-text={immediateSurfaceTextMode}; " +
                         $"live-adv-text={liveRetainedTextMode}; " +
                         $"history-text={historyRetainedTextMode}; " +
                         $"backbuffer-preservation=ok; " +
                         $"logical-canvas={_screenWidth}x{_screenHeight}; " +
                         $"window-request={_windowOptions.Width}x{_windowOptions.Height}");
        else GD.Print($"SELFTEST FAIL: threaded={actual.Count} vs headless={expected.Count}; " +
                      $"debug-launcher={launcherOk}; sleep-min={sleepMinimumOk}; " +
                      $"native-key-translation={inputTranslationOk}; cp932-wav-info={cp932WavMetadataOk}; " +
                      $"first-riff-boundary={firstRiffBoundaryOk}; " +
                      $"bgm-fade-replacement={bgmReplacementCancelsFade}; " +
                      $"bgm-one-shot={bgmOneShotModeOk}; bgm-loop={bgmLoopModeOk}; " +
                      $"bgm-stop-release={bgmStopReleaseOk}; " +
                      $"text-effect-modes={textEffectModesOk}; font-calibration={fontCalibrationOk}; " +
                      $"immediate-surface-text={immediateSurfaceTextOk}({immediateSurfaceTextMode}); " +
                      $"live-adv-text={liveRetainedTextOk}({liveRetainedTextMode}); " +
                      $"history-text={historyRetainedTextOk}({historyRetainedTextMode}); " +
                      $"backbuffer-preservation={backbufferPreservationOk}; " +
                      $"logical-canvas={logicalCanvasOk}({_screenWidth}x{_screenHeight}); " +
                      $"window-request={_windowOptions.Width}x{_windowOptions.Height}");
        GetTree().Quit(ok ? 0 : 1);
    }

    private void RunSelfTestAndQuitOnFailure()
    {
        try
        {
            RunSelfTest();
        }
        catch (Exception exception)
        {
            GD.PushError($"SELFTEST FAILED: {exception}");
            GetTree().Quit(1);
        }
    }

    // A deterministic synthesized scene: show-text, wait-for-input (exercises the suspend plumbing), a
    // nested call-script into a synthetic subroutine (exercises call-script handling), shared globals.
    private static (Script, IScriptProvider) BuildSelfTestScene(OpcodeTable table)
    {
        (int, Operand[]) ShowText(int s) => (0x6e, new[] { new Operand(0, 0), new Operand(2, s) });
        (int, Operand[]) Wait() => (0x72, new[] { new Operand(0, 0) });
        (int, Operand[]) CallScript(long id) => (0x3, new[] { new Operand(0, id) });
        (int, Operand[]) MovGG(int d, int s) => (0x55, new[] { new Operand(3, d), new Operand(3, s) });
        (int, Operand[]) MovGI(int d, long v) => (0x55, new[] { new Operand(3, d), new Operand(0, v) });
        (int, Operand[]) Exit() => (0x2, System.Array.Empty<Operand>());

        var callee = ScriptAssembler.Assemble(table, "SUBSCENE",
            new List<(int, Operand[])> { ShowText(0), MovGI(0x31, 42), Exit() }, new[] { "Sub" });
        var caller = ScriptAssembler.Assemble(table, "SELFTEST",
            new List<(int, Operand[])>
            {
                (0x70, new[]
                {
                    new Operand(0, 1), new Operand(0, 512), new Operand(0, 64),
                    new Operand(0, 0), new Operand(0, 0),
                }),
                (0x79, new[]
                {
                    new Operand(0, 1), new Operand(0, 1), new Operand(0, 1),
                }),
                (0x71, new[] { new Operand(0, 1) }),
                (0x1c1, new[]
                {
                    new Operand(0, 1), new Operand(0, 511), new Operand(0, 63),
                }),
                (0x213, new[]
                {
                    new Operand(0, 1), new Operand(0, 700000), new Operand(0, 64),
                }),
                ShowText(0),
                Wait(),
                ShowText(1),
                CallScript(5),
                MovGG(0x30, 0x31),
                Exit(),
            },
            new[] { "Hello", "World" });
        return (caller, new SelfTestProvider(callee));
    }

    private sealed class SelfTestProvider : IScriptProvider
    {
        private readonly Script _callee;
        public SelfTestProvider(Script callee) => _callee = callee;
        public Script? GetById(long id) => id == 5 ? _callee : null;
    }
}
